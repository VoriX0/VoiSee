namespace VoiSe.Audio;

/// <summary>
/// VoiSee 13 latency foundation. Fast bypasses the regular voice-effect DSP chain
/// while preserving microphone cleanup and mixer routing. Full runs the existing
/// ordered Voice Changer processing chain.
/// </summary>
public enum AudioProcessingPath
{
    Fast = 0,
    Full = 1,
}
