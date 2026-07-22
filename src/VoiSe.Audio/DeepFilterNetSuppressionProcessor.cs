using System.Runtime.InteropServices;
using System.Threading;

namespace VoiSe.Audio;

/// <summary>
/// Real-time adapter for the official DeepFilterNet LADSPA mono plug-in.
/// VoiSee's microphone bus is 48 kHz stereo; this class downmixes to mono,
/// processes the frame, and duplicates the enhanced result back to stereo.
/// </summary>
public sealed class DeepFilterNetSuppressionProcessor : IDisposable
{
    private const int Channels = 2;
    private const uint SampleRate = 48_000;
    private const string NativeFileName = "deep_filter_ladspa.dll";

    // The official LADSPA implementation owns a long-running worker thread.
    // Keep one shared runtime for the VoiSee process instead of repeatedly
    // unloading native code when the audio engine restarts.
    private static readonly object SharedSync = new();
    private static readonly float[][] SharedControls =
    {
        new float[1], // Attenuation Limit (dB)
        new float[1], // Min processing threshold (dB)
        new float[1], // Max ERB processing threshold (dB)
        new float[1], // Max DF processing threshold (dB)
        new float[1], // Min Processing Buffer (frames)
        new float[1], // Post Filter Beta
    };
    private static readonly GCHandle[] SharedControlPins = new GCHandle[6];

    private static IntPtr s_library;
    private static IntPtr s_descriptorPointer;
    private static IntPtr s_instance;
    private static ConnectPortDelegate? s_connectPort;
    private static RunDelegate? s_run;
    private static string? s_initializationError;

    private float[] _input = Array.Empty<float>();
    private float[] _output = Array.Empty<float>();
    private int _enabled;
    private float _strength;
    private bool _disposed;

    public DeepFilterNetSuppressionProcessor(bool enabled, float strength)
    {
        _enabled = enabled ? 1 : 0;
        _strength = Math.Clamp(strength, 0.0f, 1.0f);
        ConfigureControls(_strength);

        if (enabled)
        {
            TryInitialize();
        }
    }

    public bool IsAvailable => s_instance != IntPtr.Zero;
    public string? InitializationError => s_initializationError;

    public void UpdateSettings(bool enabled, float strength)
    {
        Volatile.Write(ref _strength, Math.Clamp(strength, 0.0f, 1.0f));
        Volatile.Write(ref _enabled, enabled ? 1 : 0);
        ConfigureControls(Volatile.Read(ref _strength));
    }

    public void ProcessInPlace(float[] stereoSamples)
    {
        if (_disposed || Volatile.Read(ref _enabled) == 0 || stereoSamples.Length < Channels)
        {
            return;
        }

        var strength = Volatile.Read(ref _strength);
        if (strength <= 0.0001f || s_instance == IntPtr.Zero || s_connectPort is null || s_run is null)
        {
            return;
        }

        lock (SharedSync)
        {
            try
            {
                var frames = stereoSamples.Length / Channels;
                EnsureCapacity(frames);

                for (var frame = 0; frame < frames; frame++)
                {
                    var source = frame * Channels;
                    _input[frame] = Math.Clamp(
                        (stereoSamples[source] + stereoSamples[source + 1]) * 0.5f,
                        -1.0f,
                        1.0f);
                }

                var inputPin = GCHandle.Alloc(_input, GCHandleType.Pinned);
                var outputPin = GCHandle.Alloc(_output, GCHandleType.Pinned);
                try
                {
                    s_connectPort(s_instance, 0, inputPin.AddrOfPinnedObject());
                    s_connectPort(s_instance, 1, outputPin.AddrOfPinnedObject());
                    s_run(s_instance, (uint)frames);
                }
                finally
                {
                    outputPin.Free();
                    inputPin.Free();
                }

                for (var frame = 0; frame < frames; frame++)
                {
                    var enhanced = Math.Clamp(_output[frame], -1.0f, 1.0f);
                    var target = frame * Channels;
                    stereoSamples[target] = enhanced;
                    stereoSamples[target + 1] = enhanced;
                }
            }
            catch (Exception ex)
            {
                s_initializationError = ex.Message;
                Volatile.Write(ref _enabled, 0);
            }
        }
    }

    private static void TryInitialize()
    {
        lock (SharedSync)
        {
            if (s_instance != IntPtr.Zero)
            {
                return;
            }

            try
            {
                var path = ResolveNativePath();
                s_library = NativeLibrary.Load(path);

                var descriptorExport = NativeLibrary.GetExport(s_library, "ladspa_descriptor");
                var descriptorFunction = Marshal.GetDelegateForFunctionPointer<DescriptorDelegate>(descriptorExport);
                s_descriptorPointer = descriptorFunction(0);
                if (s_descriptorPointer == IntPtr.Zero)
                {
                    throw new InvalidOperationException("DeepFilterNet mono LADSPA descriptor was not found.");
                }

                var descriptor = Marshal.PtrToStructure<LadspaDescriptor>(s_descriptorPointer);
                var label = Marshal.PtrToStringAnsi(descriptor.Label) ?? string.Empty;
                if (!string.Equals(label, "deep_filter_mono", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Unexpected DeepFilterNet plug-in label: {label}.");
                }

                var instantiate = GetDelegate<InstantiateDelegate>(descriptor.Instantiate, "instantiate");
                s_connectPort = GetDelegate<ConnectPortDelegate>(descriptor.ConnectPort, "connect_port");
                var activate = descriptor.Activate == IntPtr.Zero
                    ? null
                    : Marshal.GetDelegateForFunctionPointer<ActivateDelegate>(descriptor.Activate);
                s_run = GetDelegate<RunDelegate>(descriptor.Run, "run");

                s_instance = instantiate(s_descriptorPointer, SampleRate);
                if (s_instance == IntPtr.Zero)
                {
                    throw new InvalidOperationException("DeepFilterNet could not create a processing instance.");
                }

                PinAndConnectControls();
                activate?.Invoke(s_instance);
                s_initializationError = null;
            }
            catch (Exception ex)
            {
                s_initializationError = ex.Message;
                // It is safe to unload only when no native processing instance
                // and therefore no DeepFilterNet worker thread was created.
                if (s_instance == IntPtr.Zero && s_library != IntPtr.Zero)
                {
                    NativeLibrary.Free(s_library);
                    s_library = IntPtr.Zero;
                }
            }
        }
    }

    private static void PinAndConnectControls()
    {
        if (s_connectPort is null || s_instance == IntPtr.Zero)
        {
            return;
        }

        for (var index = 0; index < SharedControls.Length; index++)
        {
            if (!SharedControlPins[index].IsAllocated)
            {
                SharedControlPins[index] = GCHandle.Alloc(SharedControls[index], GCHandleType.Pinned);
            }

            s_connectPort(s_instance, (uint)(index + 2), SharedControlPins[index].AddrOfPinnedObject());
        }
    }

    private static void ConfigureControls(float strength)
    {
        lock (SharedSync)
        {
            // DeepFilterNet exposes several engineering controls. VoiSee deliberately
            // presents one user-facing Strength value and derives conservative values.
            SharedControls[0][0] = Math.Clamp(strength * 70.0f, 0.0f, 70.0f);
            SharedControls[1][0] = -10.0f;
            SharedControls[2][0] = 30.0f;
            SharedControls[3][0] = 20.0f;
            SharedControls[4][0] = 0.0f;
            SharedControls[5][0] = strength > 0.75f
                ? Math.Clamp((strength - 0.75f) / 0.25f * 0.015f, 0.0f, 0.015f)
                : 0.0f;
        }
    }

    private void EnsureCapacity(int frames)
    {
        if (_input.Length >= frames)
        {
            return;
        }

        _input = new float[frames];
        _output = new float[frames];
    }

    private static string ResolveNativePath()
    {
        var appPath = Path.Combine(AppContext.BaseDirectory, NativeFileName);
        if (File.Exists(appPath))
        {
            return appPath;
        }

        var assemblyDirectory = Path.GetDirectoryName(typeof(DeepFilterNetSuppressionProcessor).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            var assemblyPath = Path.Combine(assemblyDirectory, NativeFileName);
            if (File.Exists(assemblyPath))
            {
                return assemblyPath;
            }
        }

        throw new DllNotFoundException(
            $"{NativeFileName} was not found. Run scripts\\fetch-deepfilternet.ps1 or rebuild VoiSee on Windows.");
    }

    private static T GetDelegate<T>(IntPtr pointer, string name) where T : Delegate
    {
        if (pointer == IntPtr.Zero)
        {
            throw new InvalidOperationException($"DeepFilterNet LADSPA function '{name}' is missing.");
        }

        return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    public void Dispose()
    {
        // The shared official LADSPA runtime intentionally stays loaded until
        // process exit. This avoids unloading code while its worker thread lives.
        _disposed = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LadspaDescriptor
    {
        public uint UniqueId;
        public IntPtr Label;
        public uint Properties;
        public IntPtr Name;
        public IntPtr Maker;
        public IntPtr Copyright;
        public uint PortCount;
        public IntPtr PortDescriptors;
        public IntPtr PortNames;
        public IntPtr PortRangeHints;
        public IntPtr ImplementationData;
        public IntPtr Instantiate;
        public IntPtr ConnectPort;
        public IntPtr Activate;
        public IntPtr Run;
        public IntPtr RunAdding;
        public IntPtr SetRunAddingGain;
        public IntPtr Deactivate;
        public IntPtr Cleanup;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr DescriptorDelegate(uint index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr InstantiateDelegate(IntPtr descriptor, uint sampleRate);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ConnectPortDelegate(IntPtr instance, uint port, IntPtr dataLocation);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ActivateDelegate(IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RunDelegate(IntPtr instance, uint sampleCount);
}
