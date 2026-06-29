using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoiSe.Audio;

public static class SoundFileLoader
{
    public static float[] LoadToFormat(string filePath, WaveFormat targetFormat)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Sound file was not found.", filePath);
        }

        using var reader = CreateReader(filePath);
        ISampleProvider provider = reader.ToSampleProvider();

        if (provider.WaveFormat.SampleRate != targetFormat.SampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, targetFormat.SampleRate);
        }

        return ReadAllAndConvertChannels(provider, targetFormat.Channels);
    }

    private static WaveStream CreateReader(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".ogg" => new VorbisWaveReader(filePath),
            _ => new AudioFileReader(filePath)
        };
    }

    private static float[] ReadAllAndConvertChannels(ISampleProvider provider, int targetChannels)
    {
        var sourceChannels = provider.WaveFormat.Channels;
        var readBuffer = new float[provider.WaveFormat.SampleRate * sourceChannels];
        var output = new List<float>(readBuffer.Length);

        while (true)
        {
            var read = provider.Read(readBuffer, 0, readBuffer.Length);
            if (read == 0)
            {
                break;
            }

            ConvertChunkChannels(readBuffer, read, sourceChannels, targetChannels, output);
        }

        return output.ToArray();
    }

    private static void ConvertChunkChannels(float[] source, int samplesRead, int sourceChannels, int targetChannels, List<float> output)
    {
        var frameCount = samplesRead / sourceChannels;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sourceOffset = frame * sourceChannels;
            if (targetChannels == sourceChannels)
            {
                for (var ch = 0; ch < targetChannels; ch++)
                {
                    output.Add(source[sourceOffset + ch]);
                }
            }
            else if (targetChannels == 1)
            {
                var sum = 0.0f;
                for (var ch = 0; ch < sourceChannels; ch++)
                {
                    sum += source[sourceOffset + ch];
                }
                output.Add(sum / sourceChannels);
            }
            else if (sourceChannels == 1)
            {
                var mono = source[sourceOffset];
                for (var ch = 0; ch < targetChannels; ch++)
                {
                    output.Add(mono);
                }
            }
            else
            {
                for (var ch = 0; ch < targetChannels; ch++)
                {
                    output.Add(source[sourceOffset + Math.Min(ch, sourceChannels - 1)]);
                }
            }
        }
    }
}
