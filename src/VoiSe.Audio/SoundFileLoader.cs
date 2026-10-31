using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using System.Threading;
using System.Threading.Tasks;

namespace VoiSe.Audio;

public static class SoundFileLoader
{
    public const long CacheBudgetBytes = 64L * 1024 * 1024;
    public const long ShortClipBytes = 8L * 1024 * 1024;
    private const int MaxDecodedSamples = 32 * 1024 * 1024;
    private static readonly PcmCache Cache = new(CacheBudgetBytes);
    private static readonly object DecodeSync = new();
    public static long CachedBytes => Cache.RetainedBytes;

    public static float[] LoadToFormat(string filePath, WaveFormat targetFormat)
    {
        var fullPath = Path.GetFullPath(filePath);
        // One uncached full decode at a time. Cache hits do not allocate PCM copies.
        lock (DecodeSync)
        {
            var key = new SoundCacheKey(fullPath, File.GetLastWriteTimeUtc(fullPath).Ticks, targetFormat.SampleRate, targetFormat.Channels);
            if (Cache.Get(key) is { } hit) return hit;
            var result = LoadToFormatUncached(fullPath, targetFormat);
            if (result.LongLength * sizeof(float) <= ShortClipBytes) Cache.Put(key, result);
            return result;
        }
    }
    public static void Invalidate(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        lock (DecodeSync) Cache.Invalidate(Path.GetFullPath(filePath));
    }
    public static void ClearCache() { lock (DecodeSync) Cache.Clear(); }
    internal static long GetSampleCount(string filePath, WaveFormat target)
    {
        using var reader = CreateReader(filePath);
        return checked((long)Math.Ceiling(reader.TotalTime.TotalSeconds * target.SampleRate) * target.Channels);
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
                if (GetSampleCount(filePath, targetFormat) * sizeof(float) <= ShortClipBytes)
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
        if (reader.TotalTime.TotalSeconds * targetFormat.SampleRate * targetFormat.Channels > MaxDecodedSamples)
            throw new InvalidOperationException("This file is too large for full in-memory editing. Playback uses streaming; edit a shorter fragment.");
        ISampleProvider provider = reader.ToSampleProvider();

        if (provider.WaveFormat.SampleRate != targetFormat.SampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, targetFormat.SampleRate);
        }

        var expectedSamples = checked((int)Math.Ceiling(reader.TotalTime.TotalSeconds * targetFormat.SampleRate) * targetFormat.Channels);
        return ReadAllAndConvertChannels(provider, targetFormat.Channels, expectedSamples);
    }

    internal static WaveStream CreateReader(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".ogg" => new VorbisWaveReader(filePath),
            _ => new AudioFileReader(filePath)
        };
    }

    private static float[] ReadAllAndConvertChannels(ISampleProvider provider, int targetChannels, int expectedSamples)
    {
        var sourceChannels = provider.WaveFormat.Channels;
        var readBuffer = new float[4096 * sourceChannels];
        var output = new float[Math.Clamp(expectedSamples, 0, MaxDecodedSamples)];
        var used = 0;
        int read;
        while ((read = provider.Read(readBuffer, 0, readBuffer.Length)) > 0)
        {
            var frames = read / sourceChannels;
            var needed = checked(used + frames * targetChannels);
            if (needed > MaxDecodedSamples)
                throw new InvalidOperationException("Decoded audio exceeds the safe editing memory limit.");
            if (needed > output.Length)
                Array.Resize(ref output, Math.Min(MaxDecodedSamples, Math.Max(needed, output.Length + 4096 * targetChannels)));
            for (var frame = 0; frame < frames; frame++)
            {
                if (targetChannels == 1 && sourceChannels > 1)
                {
                    var sum = 0f;
                    for (var ch = 0; ch < sourceChannels; ch++) sum += readBuffer[frame * sourceChannels + ch];
                    output[used++] = sum / sourceChannels;
                }
                else
                    for (var ch = 0; ch < targetChannels; ch++)
                        output[used++] = readBuffer[frame * sourceChannels + Math.Min(ch, sourceChannels - 1)];
            }
        }
        if (used != output.Length) Array.Resize(ref output, used);
        return output;
    }

}


internal readonly record struct SoundCacheKey(string FilePath, long LastWriteTimeUtcTicks, int SampleRate, int Channels);
