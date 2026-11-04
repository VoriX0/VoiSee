using NAudio.Wave;

namespace VoiSe.Audio;

public static class AudioWaveform
{
    public static float[] Read(string path, int requestedPeaks = 900, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = AudioSourceReader.Open(path);
        var provider = reader.ToSampleProvider();
        var channels = provider.WaveFormat.Channels;
        if (channels is < 1 or > 32) throw new InvalidDataException("Unsupported channel count.");
        var capacity = Math.Clamp(requestedPeaks, 64, 4096);
        var peaks = new float[capacity];
        var buffer = new float[2048 * channels];
        var estimatedFrames = Math.Max(1, reader.TotalTime.TotalSeconds * provider.WaveFormat.SampleRate);
        var framesPerBucket = Math.Max(256L, (long)Math.Ceiling(estimatedFrames / capacity));
        long bucketFrames = 0;
        int used = 0, read;
        float peak = 0;
        void StorePeak()
        {
            if (used == capacity)
            {
                // Malformed duration metadata must not grow the peak list without limit.
                var compacted = 0;
                for (var i = 0; i < used; i += 2) peaks[compacted++] = Math.Max(peaks[i], i + 1 < used ? peaks[i + 1] : 0);
                used = compacted;
                framesPerBucket *= 2;
            }
            peaks[used++] = peak; peak = 0; bucketFrames = 0;
        }
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var offset = 0; offset < read; offset += channels)
            {
                for (var ch = 0; ch < channels && offset + ch < read; ch++)
                    if (float.IsFinite(buffer[offset + ch])) peak = Math.Max(peak, Math.Abs(buffer[offset + ch]));
                if (++bucketFrames >= framesPerBucket) StorePeak();
            }
        }
        if (bucketFrames > 0 || used == 0) StorePeak();
        cancellationToken.ThrowIfCancellationRequested();
        return peaks.AsSpan(0, used).ToArray();
    }
}
