namespace VoiSe.Audio;

public sealed record AudioDeviceInfo(
    string Id,
    string FriendlyName,
    string DataFlow,
    string State
);
