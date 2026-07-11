using NAudio.Wave;

namespace VoiSe.Audio;

public sealed class SoundEditRequest
{
    public required string SourcePath { get; init; }
    public required string TargetPath { get; init; }
    public double TrimStartSeconds { get; init; }
    public double TrimEndSeconds { get; init; }
    public double GainDb { get; init; }
    public bool Normalize { get; init; }
    public double FadeInSeconds { get; init; }
    public double FadeOutSeconds { get; init; }
    public double DistortionAmount { get; init; }
    public double EffectTimelineOffsetSeconds { get; init; }
    public double EffectTimelineDurationSeconds { get; init; }
    public int SampleRate { get; init; } = 48_000;
    public int Channels { get; init; } = 2;
}

public sealed class SoundCutRequest
{
    public required string SourcePath { get; init; }
    public required string TargetPath { get; init; }
    public double CutStartSeconds { get; init; }
    public double CutEndSeconds { get; init; }
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
        var edited = new float[sampleCount];
        if (sampleCount > 0)
        {
            Array.Copy(source, startSample, edited, 0, sampleCount);
        }

        var normalizeGain = 1.0f;
        if (request.Normalize)
        {
            var sourcePeak = 0.0f;
            for (var i = 0; i < source.Length; i++)
            {
                sourcePeak = Math.Max(sourcePeak, Math.Abs(source[i]));
            }

            if (sourcePeak > 0.000001f)
            {
                normalizeGain = 0.98f / sourcePeak;
            }
        }

        ApplyEffects(
            edited,
            channels,
            sampleRate,
            DbToLinear(request.GainDb) * normalizeGain,
            request.FadeInSeconds,
            request.FadeOutSeconds,
            request.DistortionAmount,
            request.EffectTimelineOffsetSeconds,
            request.EffectTimelineDurationSeconds <= 0 ? totalSeconds : request.EffectTimelineDurationSeconds);

        var tempPath = request.TargetPath + ".tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        using (var writer = new WaveFileWriter(tempPath, format))
        {
            WriteRange(writer, edited, 0, edited.Length, 1.0f, channels);
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

    public static SoundEditResult RenderCutToWav(SoundCutRequest request)
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

        var cutStartSeconds = Math.Clamp(request.CutStartSeconds, 0, totalSeconds);
        var cutEndSeconds = Math.Clamp(request.CutEndSeconds, cutStartSeconds, totalSeconds);
        if (cutEndSeconds <= cutStartSeconds)
        {
            throw new InvalidOperationException("The cut selection is empty.");
        }

        var cutStartFrame = (int)Math.Clamp(Math.Round(cutStartSeconds * sampleRate), 0, totalFrames);
        var cutEndFrame = (int)Math.Clamp(Math.Round(cutEndSeconds * sampleRate), cutStartFrame, totalFrames);
        var beforeSampleCount = cutStartFrame * channels;
        var afterStartSample = cutEndFrame * channels;
        var afterSampleCount = Math.Max(0, source.Length - afterStartSample);
        var sampleCount = beforeSampleCount + afterSampleCount;
        if (sampleCount <= 0)
        {
            throw new InvalidOperationException("Cutting this selection would remove the entire sound.");
        }

        var gain = DbToLinear(request.GainDb);
        var tempPath = request.TargetPath + ".tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        using (var writer = new WaveFileWriter(tempPath, format))
        {
            WriteRange(writer, source, 0, beforeSampleCount, gain, channels);
            WriteRange(writer, source, afterStartSample, afterSampleCount, gain, channels);
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


    private static void ApplyEffects(
        float[] samples,
        int channels,
        int sampleRate,
        float gain,
        double fadeInSeconds,
        double fadeOutSeconds,
        double distortionAmount,
        double timelineOffsetSeconds,
        double timelineDurationSeconds)
    {
        if (samples.Length == 0)
        {
            return;
        }

        channels = Math.Max(1, channels);
        sampleRate = Math.Max(1, sampleRate);
        var frameCount = samples.Length / channels;
        var timelineFrames = Math.Max(frameCount, (int)Math.Round(Math.Max(0, timelineDurationSeconds) * sampleRate));
        var offsetFrames = Math.Max(0, (int)Math.Round(Math.Max(0, timelineOffsetSeconds) * sampleRate));
        var fadeInFrames = Math.Max(0, (int)Math.Round(Math.Max(0, fadeInSeconds) * sampleRate));
        var fadeOutFrames = Math.Max(0, (int)Math.Round(Math.Max(0, fadeOutSeconds) * sampleRate));
        var distortion = Math.Clamp(distortionAmount, 0.0, 1.0);
        var drive = 1.0 + distortion * 11.0;
        var driveScale = distortion > 0.0001 ? Math.Tanh(drive) : 1.0;

        for (var frame = 0; frame < frameCount; frame++)
        {
            var timelineFrame = offsetFrames + frame;
            var envelope = 1.0;
            if (fadeInFrames > 0 && timelineFrame < fadeInFrames)
            {
                envelope *= timelineFrame / (double)fadeInFrames;
            }

            if (fadeOutFrames > 0)
            {
                var framesFromEnd = Math.Max(0, timelineFrames - 1 - timelineFrame);
                if (framesFromEnd < fadeOutFrames)
                {
                    envelope *= framesFromEnd / (double)fadeOutFrames;
                }
            }

            for (var channel = 0; channel < channels; channel++)
            {
                var index = frame * channels + channel;
                var value = samples[index] * gain * (float)envelope;
                if (distortion > 0.0001)
                {
                    value = (float)(Math.Tanh(value * drive) / driveScale);
                }

                samples[index] = Math.Clamp(value, -1.0f, 1.0f);
            }
        }
    }

    private static void WriteRange(
        WaveFileWriter writer,
        IReadOnlyList<float> source,
        int startSample,
        int sampleCount,
        float gain,
        int channels)
    {
        if (sampleCount <= 0)
        {
            return;
        }

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
