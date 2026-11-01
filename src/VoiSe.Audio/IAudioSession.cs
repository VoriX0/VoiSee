namespace VoiSe.Audio;

/// <summary>The lifecycle contract; transport and DSP stay in the audio engine.</summary>
public interface IAudioSession : IDisposable
{
    event Action<string>? Faulted;
    void Start();
}
