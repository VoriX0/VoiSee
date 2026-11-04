using VoiSe.Audio;

namespace VoiSe.App;

internal sealed record AudioResumePlan(string Input, string Output, string? Monitor)
{
    public static AudioResumePlan? Capture(AudioDeviceSelection selection, bool running) => !running || selection.Input is null || selection.VirtualOutput is null
        ? null : new(selection.Input.Id, selection.VirtualOutput.Id, selection.Monitor?.Id);
    public bool Matches(AudioDeviceSelection selection) => selection.Input?.Id == Input && selection.VirtualOutput?.Id == Output && selection.Monitor?.Id == Monitor;
}
