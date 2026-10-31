using System.Runtime.InteropServices;

namespace VoiSe.Audio;

// Mono 48k/10ms adapter. Caller serializes render and capture access.
internal sealed unsafe class WebRtcEchoCanceller : IDisposable
{
    private IntPtr _apm, _stream;
    private readonly float[] _render = new float[480], _capture = new float[480], _output = new float[480];
    private int _renderCount, _captureCount, _outputPosition = 480;
    public WebRtcEchoCanceller()
    {
        try
        {
            _apm = Native.Create(); _stream = Native.StreamCreate(48000, (UIntPtr)1);
            if (_apm == IntPtr.Zero || _stream == IntPtr.Zero) throw new InvalidOperationException("WebRTC initialization failed.");
            var config = Native.ConfigCreate();
            if (config == IntPtr.Zero) throw new InvalidOperationException("WebRTC configuration failed.");
            try
            {
                Native.Echo(config, 1, 0); Native.Noise(config, 0, 0);
                Native.Gain1(config, 0, 1, 3, 0, 0); Native.Gain2(config, 0); Native.HighPass(config, 0);
                Check(Native.Apply(_apm, config));
            }
            finally { Native.ConfigDestroy(config); }
        }
        catch { Dispose(); throw; }
    }
    public void AddReference(ReadOnlySpan<float> stereo)
    {
        ObjectDisposedException.ThrowIf(_apm == IntPtr.Zero, this);
        for (var i = 0; i + 1 < stereo.Length; i += 2)
        {
            _render[_renderCount++] = (stereo[i] + stereo[i + 1]) * .5f;
            if (_renderCount != 480) continue;
            fixed (float* p = _render) { float* channel = p; Check(Native.Reverse(_apm, &channel, _stream)); }
            _renderCount = 0;
        }
    }
    public void Process(Span<float> stereo)
    {
        ObjectDisposedException.ThrowIf(_apm == IntPtr.Zero, this);
        for (var i = 0; i + 1 < stereo.Length; i += 2)
        {
            _capture[_captureCount++] = (stereo[i] + stereo[i + 1]) * .5f;
            if (_captureCount == 480)
            {
                Native.Delay(_apm, 20);
                fixed (float* input = _capture, output = _output)
                {
                    float* source = input; float* destination = output;
                    Check(Native.Process(_apm, &source, _stream, _stream, &destination));
                }
                _captureCount = 0; _outputPosition = 0;
            }
            var sample = _outputPosition < 480 ? _output[_outputPosition++] : 0;
            stereo[i] = stereo[i + 1] = float.IsFinite(sample) ? sample : 0;
        }
    }
    private static void Check(int code) { if (code < 0) throw new InvalidOperationException($"WebRTC processing failed ({code})."); }
    public void Dispose()
    {
        if (_apm != IntPtr.Zero) { Native.Destroy(_apm); _apm = IntPtr.Zero; }
        if (_stream != IntPtr.Zero) { Native.StreamDestroy(_stream); _stream = IntPtr.Zero; }
    }
    private static class Native
    {
        private const string Lib = "webrtc-apm.dll";
        [DllImport(Lib, EntryPoint = "webrtc_apm_create", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr Create();
        [DllImport(Lib, EntryPoint = "webrtc_apm_destroy", CallingConvention = CallingConvention.Cdecl)] internal static extern void Destroy(IntPtr apm);
        [DllImport(Lib, EntryPoint = "webrtc_apm_config_create", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ConfigCreate();
        [DllImport(Lib, EntryPoint = "webrtc_apm_config_destroy", CallingConvention = CallingConvention.Cdecl)] internal static extern void ConfigDestroy(IntPtr config);
        [DllImport(Lib, EntryPoint = "webrtc_apm_config_set_echo_canceller", CallingConvention = CallingConvention.Cdecl)] internal static extern void Echo(IntPtr config, int enabled, int mobile);
        [DllImport(Lib, EntryPoint = "webrtc_apm_config_set_noise_suppression", CallingConvention = CallingConvention.Cdecl)] internal static extern void Noise(IntPtr config, int enabled, int level);
        [DllImport(Lib, EntryPoint = "webrtc_apm_config_set_gain_controller1", CallingConvention = CallingConvention.Cdecl)] internal static extern void Gain1(IntPtr config, int enabled, int mode, int target, int gain, int limiter);
        [DllImport(Lib, EntryPoint = "webrtc_apm_config_set_gain_controller2", CallingConvention = CallingConvention.Cdecl)] internal static extern void Gain2(IntPtr config, int enabled);
        [DllImport(Lib, EntryPoint = "webrtc_apm_config_set_high_pass_filter", CallingConvention = CallingConvention.Cdecl)] internal static extern void HighPass(IntPtr config, int enabled);
        [DllImport(Lib, EntryPoint = "webrtc_apm_apply_config", CallingConvention = CallingConvention.Cdecl)] internal static extern int Apply(IntPtr apm, IntPtr config);
        [DllImport(Lib, EntryPoint = "webrtc_apm_stream_config_create", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr StreamCreate(int rate, UIntPtr channels);
        [DllImport(Lib, EntryPoint = "webrtc_apm_stream_config_destroy", CallingConvention = CallingConvention.Cdecl)] internal static extern void StreamDestroy(IntPtr config);
        [DllImport(Lib, EntryPoint = "webrtc_apm_analyze_reverse_stream", CallingConvention = CallingConvention.Cdecl)] internal static extern int Reverse(IntPtr apm, float** data, IntPtr config);
        [DllImport(Lib, EntryPoint = "webrtc_apm_process_stream", CallingConvention = CallingConvention.Cdecl)] internal static extern int Process(IntPtr apm, float** input, IntPtr inputConfig, IntPtr outputConfig, float** output);
        [DllImport(Lib, EntryPoint = "webrtc_apm_set_stream_delay_ms", CallingConvention = CallingConvention.Cdecl)] internal static extern void Delay(IntPtr apm, int delay);
    }
}
