using NAudio.Wave;

namespace VoiSe.Audio;

public sealed class SoundEditRequest
{
    public required string SourcePath { get; init; }
    public required string TargetPath { get; init; }
    public double TrimStartSeconds { get; init; }
    public double TrimEndSeconds { get; init; }
    public double GainDb { get; init; }
    public int SampleRate { get; init; } = 48_000;
    public int Channels { get; init; } = 2;
}

public sealed class SoundEditResult
{
    public required string TargetPath { get; init; }
    public double DurationSeconds { get; init; }
    public long SampleCount { get; init; }
}

public static class SoundEditProcessor
{
    public static double GetDurationSeconds(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return 0;
        }

        try
        {
            using var reader = CreateReader(filePath);
            return Math.Max(0, reader.TotalTime.TotalSeconds);
        }
        catch
        {
            return 0;
        }
    }

    public static SoundEditResult RenderToWav(SoundEditRequest request)
    {
        if (!File.Exists(request.SourcePath))
        {
            throw new FileNotFoundException("Source sound file was not found.", request.SourcePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.TargetPath) ?? AppContext.BaseDirectory);

        var format = WaveFormat.CreateIeeeFloatWaveFormat(
            Math.Clamp(request.SampleRate, 8_000, 192_000),
            Math.Clamp(request.Channels, 1, 2));

        var source = SoundFileLoader.LoadToFormat(request.SourcePath, format);
        var channels = format.Channels;
        var sampleRate = format.SampleRate;
        var totalFrames = source.Length / channels;
        var totalSeconds = totalFrames / (double)sampleRate;

        var startSeconds = Math.Clamp(request.TrimStartSeconds, 0, totalSeconds);
        var endSeconds = request.TrimEndSeconds <= 0
            ? totalSeconds
            : Math.Clamp(request.TrimEndSeconds, startSeconds, totalSeconds);

        if (endSeconds <= startSeconds)
        {
            endSeconds = Math.Min(totalSeconds, startSeconds + 0.01);
        }

        var startFrame = (int)Math.Clamp(Math.Round(startSeconds * sampleRate), 0, totalFrames);
        var endFrame = (int)Math.Clamp(Math.Round(endSeconds * sampleRate), startFrame, totalFrames);
        var startSample = startFrame * channels;
        var sampleCount = Math.Max(0, (endFrame - startFrame) * channels);
        var gain = DbToLinear(request.GainDb);

        var tempPath = request.TargetPath + ".tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        using (var writer = new WaveFileWriter(tempPath, format))
        {
            const int chunkSize = 16_384;
            var buffer = new float[Math.Min(chunkSize, Math.Max(channels, sampleCount))];
            var remaining = sampleCount;
            var position = startSample;

            while (remaining > 0)
            {
                var count = Math.Min(buffer.Length, remaining);
                for (var i = 0; i < count; i++)
                {
                    buffer[i] = Math.Clamp(source[position + i] * gain, -1.0f, 1.0f);
                }

                writer.WriteSamples(buffer, 0, count);
                position += count;
                remaining -= count;
            }
        }

        if (File.Exists(request.TargetPath))
        {
            File.Delete(request.TargetPath);
        }

        File.Move(tempPath, request.TargetPath);
        SoundFileLoader.Invalidate(request.TargetPath);

        return new SoundEditResult
        {
            TargetPath = request.TargetPath,
            DurationSeconds = sampleCount / (double)channels / sampleRate,
            SampleCount = sampleCount
        };
    }

    private static float DbToLinear(double db)
    {
        var clamped = Math.Clamp(db, -48.0, 24.0);
        return (float)Math.Pow(10.0, clamped / 20.0);
    }

    private static WaveStream CreateReader(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".ogg" => new NAudio.Vorbis.VorbisWaveReader(filePath),
            _ => new AudioFileReader(filePath)
        };
    }
}
