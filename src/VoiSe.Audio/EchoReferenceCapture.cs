using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiSe.Audio;

// NAudio's standard loopback constructor uses a long default capture buffer.
// AEC needs the render reference before the corresponding acoustic echo.
internal sealed class EchoReferenceCapture : WasapiCapture
{
    public EchoReferenceCapture(MMDevice device) : base(device, true, AudioTiming.CaptureMs) { }
    protected override AudioClientStreamFlags GetAudioClientStreamFlags()
        => base.GetAudioClientStreamFlags() | AudioClientStreamFlags.Loopback;
}
