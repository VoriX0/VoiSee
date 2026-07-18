namespace VoiSe.App;

internal sealed class CapturableWindowInfo
{
    public IntPtr Handle { get; init; }
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string? ExecutablePath { get; init; }
    public string WindowTitle { get; init; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(WindowTitle)
        ? ProcessName
        : $"{WindowTitle}  —  {ProcessName}";
}
