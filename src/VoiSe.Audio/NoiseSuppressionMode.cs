namespace VoiSe.Audio;

/// <summary>
/// Global microphone-only noise suppression engine. Noise suppression is kept
/// outside voice presets so switching character effects does not change the
/// user's microphone cleanup choice.
/// </summary>
public enum NoiseSuppressionMode
{
    Off = 0,
    RnNoise = 1,
    DeepFilterNet = 2,
}
