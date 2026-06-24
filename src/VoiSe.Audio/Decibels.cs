namespace VoiSe.Audio;

public static class Decibels
{
    public static float DbToLinear(float db)
        => (float)Math.Pow(10.0, db / 20.0);

    public static float LinearToDb(float linear)
        => linear <= 0.0f ? -120.0f : 20.0f * (float)Math.Log10(linear);
}
