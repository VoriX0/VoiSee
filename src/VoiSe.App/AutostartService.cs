using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;

namespace VoiSe.App;

internal sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VoiSee";

    public string ExecutablePath => ResolveExecutablePath();

    public string ExpectedCommand => $"\"{ExecutablePath}\" --background";

    public bool RefreshAndRepairIfRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        var current = key?.GetValue(ValueName) as string;
        if (string.IsNullOrWhiteSpace(current))
        {
            return false;
        }

        if (!string.Equals(current.Trim(), ExpectedCommand, StringComparison.OrdinalIgnoreCase))
        {
            key!.SetValue(ValueName, ExpectedCommand, RegistryValueKind.String);
        }

        return true;
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var current = key?.GetValue(ValueName) as string;
        return string.Equals(current?.Trim(), ExpectedCommand, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException(UiText.L("Windows_autostart_registry_key_could_not_be_op_59d81aec"));
            key.SetValue(ValueName, ExpectedCommand, RegistryValueKind.String);
            return;
        }

        using var existing = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        existing?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string ResolveExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(UiText.L("VoiSee_executable_path_could_not_be_determined_7d2cb72e"));
        }

        return Path.GetFullPath(path);
    }
}
