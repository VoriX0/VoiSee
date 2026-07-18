using System.Runtime.InteropServices;
using NAudio.Wave;

namespace VoiSe.Audio;

/// <summary>
/// Captures the render stream produced by one Windows process and its child processes.
/// The class deliberately exposes only 48 kHz stereo IEEE-float samples because that is
/// the native VoiSee mix format.
/// </summary>
internal sealed class ProcessLoopbackCapture : IDisposable
{
    private const string ProcessLoopbackDevice = "VAD\\Process_Loopback";
    private const ushort VtBlob = 65;
    private const int AudclntStreamflagsLoopback = 0x00020000;
    private const int AudclntStreamflagsEventcallback = 0x00040000;
    private const int AudclntBufferflagsSilent = 0x00000002;
    private const long ReferenceTimesPerMillisecond = 10_000;

    private static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid IidAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

    private readonly object _sync = new();
    private readonly int _bufferMilliseconds;
    private IAudioClientNative? _audioClient;
    private IAudioCaptureClientNative? _captureClient;
    private IActivateAudioInterfaceAsyncOperation? _activationOperation;
    private AutoResetEvent? _sampleReadyEvent;
    private Thread? _captureThread;
    private volatile bool _captureRequested;
    private bool _disposed;

    public ProcessLoopbackCapture(int bufferMilliseconds = 50)
    {
        _bufferMilliseconds = Math.Clamp(bufferMilliseconds, 20, 250);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
    }

    public WaveFormat WaveFormat { get; }
    public bool IsCapturing { get; private set; }

    public event Action<float[]>? SamplesAvailable;
    public event Action<Exception?>? CaptureStopped;

    public async Task StartAsync(uint processId)
    {
        ThrowIfDisposed();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
        {
            throw new PlatformNotSupportedException(
                "Media Bridge process capture requires Windows 10 build 20348 or newer.");
        }

        lock (_sync)
        {
            if (_captureRequested || IsCapturing)
            {
                throw new InvalidOperationException("Process capture is already running.");
            }
        }

        var activation = new AudioClientActivationParams
        {
            ActivationType = AudioClientActivationType.ProcessLoopback,
            ProcessLoopbackParams = new AudioClientProcessLoopbackParams
            {
                TargetProcessId = processId,
                ProcessLoopbackMode = ProcessLoopbackMode.IncludeTargetProcessTree
            }
        };

        var activationSize = Marshal.SizeOf<AudioClientActivationParams>();
        var activationPtr = Marshal.AllocHGlobal(activationSize);
        var propVariantPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
        try
        {
            Marshal.StructureToPtr(activation, activationPtr, false);
            var propVariant = new PropVariant
            {
                VariantType = VtBlob,
                Blob = new Blob
                {
                    Size = activationSize,
                    Data = activationPtr
                }
            };
            Marshal.StructureToPtr(propVariant, propVariantPtr, false);

            var completion = new ActivationCompletionHandler();
            var iid = IidAudioClient;
            var hr = ActivateAudioInterfaceAsync(
                ProcessLoopbackDevice,
                ref iid,
                propVariantPtr,
                completion,
                out var operation);
            Marshal.ThrowExceptionForHR(hr);
            _activationOperation = operation;

            _audioClient = await completion.Completion.ConfigureAwait(false);
        }
        finally
        {
            Marshal.FreeHGlobal(propVariantPtr);
            Marshal.FreeHGlobal(activationPtr);
            ReleaseComObject(ref _activationOperation);
        }

        var waveFormatPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
        try
        {
            var nativeFormat = new WaveFormatEx
            {
                FormatTag = 3,
                Channels = 2,
                SamplesPerSec = 48_000,
                AvgBytesPerSec = 48_000 * 2 * sizeof(float),
                BlockAlign = 2 * sizeof(float),
                BitsPerSample = 32,
                ExtraSize = 0
            };
            Marshal.StructureToPtr(nativeFormat, waveFormatPtr, false);

            var bufferDuration = _bufferMilliseconds * ReferenceTimesPerMillisecond;
            var initHr = _audioClient.Initialize(
                AudioClientShareMode.Shared,
                AudclntStreamflagsLoopback | AudclntStreamflagsEventcallback,
                bufferDuration,
                0,
                waveFormatPtr,
                IntPtr.Zero);
            Marshal.ThrowExceptionForHR(initHr);
        }
        finally
        {
            Marshal.FreeHGlobal(waveFormatPtr);
        }

        _sampleReadyEvent = new AutoResetEvent(false);
        Marshal.ThrowExceptionForHR(_audioClient.SetEventHandle(_sampleReadyEvent.SafeWaitHandle.DangerousGetHandle()));

        var serviceIid = IidAudioCaptureClient;
        Marshal.ThrowExceptionForHR(_audioClient.GetService(ref serviceIid, out var captureClientPtr));
        try
        {
            _captureClient = (IAudioCaptureClientNative)Marshal.GetObjectForIUnknown(captureClientPtr);
        }
        finally
        {
            Marshal.Release(captureClientPtr);
        }

        Marshal.ThrowExceptionForHR(_audioClient.Start());
        _captureRequested = true;
        IsCapturing = true;
        _captureThread = new Thread(CaptureThreadMain)
        {
            IsBackground = true,
            Name = "VoiSee Media Bridge Capture"
        };
        _captureThread.Start();
    }

    public void Stop()
    {
        _captureRequested = false;
        _sampleReadyEvent?.Set();

        var thread = _captureThread;
        if (thread is not null && thread != Thread.CurrentThread)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }

        SafeStopClient();
        CleanupCaptureResources();
        IsCapturing = false;
    }

    private void CaptureThreadMain()
    {
        Exception? error = null;
        try
        {
            while (_captureRequested)
            {
                _sampleReadyEvent?.WaitOne(_bufferMilliseconds * 4);
                if (!_captureRequested)
                {
                    break;
                }

                DrainPackets();
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            _captureRequested = false;
            SafeStopClient();
            IsCapturing = false;
            CaptureStopped?.Invoke(error);
        }
    }

    private void DrainPackets()
    {
        var captureClient = _captureClient;
        if (captureClient is null)
        {
            return;
        }

        Marshal.ThrowExceptionForHR(captureClient.GetNextPacketSize(out var packetFrames));
        while (packetFrames > 0 && _captureRequested)
        {
            var bufferPtr = IntPtr.Zero;
            uint framesRead = 0;
            var bufferAcquired = false;
            try
            {
                Marshal.ThrowExceptionForHR(captureClient.GetBuffer(
                    out bufferPtr,
                    out framesRead,
                    out var flags,
                    out _,
                    out _));
                bufferAcquired = true;

                if (framesRead > 0)
                {
                    var samples = new float[checked((int)framesRead * 2)];
                    if ((flags & AudclntBufferflagsSilent) == 0 && bufferPtr != IntPtr.Zero)
                    {
                        Marshal.Copy(bufferPtr, samples, 0, samples.Length);
                    }

                    SamplesAvailable?.Invoke(samples);
                }
            }
            finally
            {
                if (bufferAcquired)
                {
                    captureClient.ReleaseBuffer(framesRead);
                }
            }

            Marshal.ThrowExceptionForHR(captureClient.GetNextPacketSize(out packetFrames));
        }
    }

    private void SafeStopClient()
    {
        try { _audioClient?.Stop(); } catch { }
        try { _audioClient?.Reset(); } catch { }
    }

    private void CleanupCaptureResources()
    {
        _captureThread = null;
        _sampleReadyEvent?.Dispose();
        _sampleReadyEvent = null;
        ReleaseComObject(ref _captureClient);
        ReleaseComObject(ref _audioClient);
    }

    private static void ReleaseComObject<T>(ref T? value) where T : class
    {
        var current = value;
        value = null;
        if (current is not null && Marshal.IsComObject(current))
        {
            try { Marshal.FinalReleaseComObject(current); } catch { }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    [DllImport("Mmdevapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int ActivateAudioInterfaceAsync(
        string deviceInterfacePath,
        ref Guid riid,
        IntPtr activationParams,
        [MarshalAs(UnmanagedType.Interface)] IActivateAudioInterfaceCompletionHandler completionHandler,
        [MarshalAs(UnmanagedType.Interface)] out IActivateAudioInterfaceAsyncOperation activationOperation);

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientActivationParams
    {
        public AudioClientActivationType ActivationType;
        public AudioClientProcessLoopbackParams ProcessLoopbackParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientProcessLoopbackParams
    {
        public uint TargetProcessId;
        public ProcessLoopbackMode ProcessLoopbackMode;
    }

    private enum AudioClientActivationType
    {
        Default = 0,
        ProcessLoopback = 1
    }

    private enum ProcessLoopbackMode
    {
        IncludeTargetProcessTree = 0,
        ExcludeTargetProcessTree = 1
    }

    private enum AudioClientShareMode
    {
        Shared = 0,
        Exclusive = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Size;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public Blob Blob;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [ComImport]
    [Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    [ComImport]
    [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig]
        int GetActivateResult(out int activateResult, out IntPtr activatedInterface);
    }

    [ComImport]
    [Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject
    {
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class ActivationCompletionHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        private readonly TaskCompletionSource<IAudioClientNative> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IAudioClientNative> Completion => _completion.Task;

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            try
            {
                Marshal.ThrowExceptionForHR(activateOperation.GetActivateResult(out var activationHr, out var interfacePtr));
                Marshal.ThrowExceptionForHR(activationHr);
                try
                {
                    var audioClient = (IAudioClientNative)Marshal.GetObjectForIUnknown(interfacePtr);
                    _completion.TrySetResult(audioClient);
                }
                finally
                {
                    if (interfacePtr != IntPtr.Zero)
                    {
                        Marshal.Release(interfacePtr);
                    }
                }
            }
            catch (Exception ex)
            {
                _completion.TrySetException(ex);
            }
        }
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClientNative
    {
        [PreserveSig]
        int Initialize(
            AudioClientShareMode shareMode,
            int streamFlags,
            long bufferDuration,
            long periodicity,
            IntPtr format,
            IntPtr audioSessionGuid);

        [PreserveSig] int GetBufferSize(out uint bufferFrameCount);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint currentPadding);
        [PreserveSig] int IsFormatSupported(AudioClientShareMode shareMode, IntPtr format, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr deviceFormat);
        [PreserveSig] int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid interfaceId, out IntPtr service);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClientNative
    {
        [PreserveSig]
        int GetBuffer(
            out IntPtr data,
            out uint framesToRead,
            out int flags,
            out ulong devicePosition,
            out ulong qpcPosition);

        [PreserveSig] int ReleaseBuffer(uint framesRead);
        [PreserveSig] int GetNextPacketSize(out uint packetFrames);
    }
}
