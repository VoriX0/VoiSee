using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace VoiSe.Audio;

public static class SoundFileLoader
{
    private static readonly ConcurrentDictionary<SoundCacheKey, Lazy<float[]>> Cache = new();

    public static float[] LoadToFormat(string filePath, WaveFormat targetFormat)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Sound file was not found.", filePath);
        }

        var fullPath = Path.GetFullPath(filePath);
        var key = new SoundCacheKey(
            fullPath,
            File.GetLastWriteTimeUtc(fullPath).Ticks,
            targetFormat.SampleRate,
            targetFormat.Channels);

        // Decoding/resampling can take hundreds of milliseconds for larger files.
        // Cache immutable PCM float buffers so repeated SoundBoard/Scene starts do not
        // hit the UI thread or disk again. Multiple simultaneous requests share the
        // same Lazy value, so a double-click/hotkey burst does not decode twice.
        var lazy = Cache.GetOrAdd(key, cacheKey => new Lazy<float[]>(
            () => LoadToFormatUncached(cacheKey.FilePath, targetFormat),
            LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }


    public static void Invalidate(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(filePath);
        foreach (var key in Cache.Keys)
        {
            if (string.Equals(key.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                Cache.TryRemove(key, out _);
            }
        }
    }

    public static void ClearCache()
    {
        Cache.Clear();
    }

    public static Task PreloadToFormatAsync(string filePath, WaveFormat targetFormat)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            try
            {
                _ = LoadToFormat(filePath, targetFormat);
            }
            catch
            {
                // Preload is best-effort. Normal playback will report errors.
            }
        });
    }

    private static float[] LoadToFormatUncached(string filePath, WaveFormat targetFormat)
    {
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


internal readonly record struct SoundCacheKey(string FilePath, long LastWriteTimeUtcTicks, int SampleRate, int Channels);
