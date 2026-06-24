using NAudio.Wave;

namespace VoiSe.Audio;

public static class PcmFloatConverter
{
    public static bool CanProcess(WaveFormat format)
    {
        return (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
               || (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16);
    }

    public static float[] ToFloatArray(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            var sampleCount = bytesRecorded / 4;
            var samples = new float[sampleCount];
            Buffer.BlockCopy(buffer, 0, samples, 0, bytesRecorded);
            return samples;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            var sampleCount = bytesRecorded / 2;
            var samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                var value = BitConverter.ToInt16(buffer, i * 2);
                samples[i] = value / 32768.0f;
            }
            return samples;
        }

        throw new NotSupportedException($"Unsupported capture format: {format.Encoding}, {format.BitsPerSample} bit");
    }

    public static byte[] FromFloatArray(float[] samples, WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            var bytes = new byte[samples.Length * 4];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            var bytes = new byte[samples.Length * 2];
            for (var i = 0; i < samples.Length; i++)
            {
                var clamped = Math.Clamp(samples[i], -1.0f, 1.0f);
                var value = (short)Math.Round(clamped * short.MaxValue);
                var b = BitConverter.GetBytes(value);
                bytes[i * 2] = b[0];
                bytes[i * 2 + 1] = b[1];
            }
            return bytes;
        }

        throw new NotSupportedException($"Unsupported capture format: {format.Encoding}, {format.BitsPerSample} bit");
    }
}
