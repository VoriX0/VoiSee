namespace VoiSe.Audio;

/// <summary>
/// VoiSee 13 processing route. Fast bypasses both AI Voice and the regular DSP
/// chain; Ai runs the neural voice stage without regular DSP; Full runs only the
/// regular DSP chain; AiFull runs AI Voice followed by the regular DSP chain.
/// </summary>
public enum AudioProcessingPath
{
    Fast = 0,
    Full = 1,
    Ai = 2,
    AiFull = 3,
}
