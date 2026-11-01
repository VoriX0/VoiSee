using NAudio.Vorbis;
using NAudio.Wave;

namespace VoiSe.Audio;

// Shared decoder selection for playback, duration, waveform and editing.
// The NAudio umbrella package also imports WinForms; the actual decoders
// below need only Core, Wasapi and WinMM. Callers convert PCM to samples.
public static class AudioSourceReader
{
    public static WaveStream Open(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ogg" => new VorbisWaveReader(path),
            ".wav" => OpenWave(path),
            ".aif" or ".aiff" => new AiffFileReader(path),
            _ => new MediaFoundationReader(path)
        };
    }

    private static WaveStream OpenWave(string path)
    {
        WaveStream reader = new WaveFileReader(path);
        try
        {
            if (reader.WaveFormat.Encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat))
            {
                reader = WaveFormatConversionStream.CreatePcmStream(reader);
                reader = new BlockAlignReductionStream(reader);
            }
            return reader;
        }
        catch { reader.Dispose(); throw; }
    }
}
