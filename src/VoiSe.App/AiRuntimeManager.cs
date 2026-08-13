using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using VoiSe.Audio;

namespace VoiSe.App;

internal sealed class AiRuntimeManager
{
    private const string ExpectedRuntimeVersion = "rvc:81eed5e8f68b6bed1789f682fe78cdd324495afc";
    private readonly string _dataDirectory;

    public AiRuntimeManager(string dataDirectory)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
    }

    public string RuntimeRoot => Path.Combine(_dataDirectory, "AI Runtime", "rvc");
    public string PythonPath => Path.Combine(RuntimeRoot, ".venv", "Scripts", "python.exe");
    public string RuntimeVersionMarkerPath => Path.Combine(RuntimeRoot, ".voisee-runtime-version");
    public string ModelCacheRoot => Path.Combine(_dataDirectory, "AI Runtime", "model-cache");
    public string BridgeScriptPath => Path.Combine(AppContext.BaseDirectory, "AiRuntime", "rvc_bridge.py");
    public string InstallerScriptPath => Path.Combine(AppContext.BaseDirectory, "AiRuntime", "install-rvc-runtime.ps1");

    public bool IsInstalled(out string? problem)
    {
        if (!File.Exists(PythonPath))
        {
            problem = "The AI runtime Python environment is not installed.";
            return false;
        }
        if (!File.Exists(RuntimeVersionMarkerPath))
        {
            problem = "The AI runtime version marker is missing; Install / Repair is required.";
            return false;
        }
        try
        {
            var installedVersion = File.ReadAllText(RuntimeVersionMarkerPath).Trim();
            if (!string.Equals(installedVersion, ExpectedRuntimeVersion, StringComparison.Ordinal))
            {
                problem = $"The installed AI runtime version '{installedVersion}' does not match this VoiSee build; Install / Repair is required.";
                return false;
            }
        }
        catch (Exception ex)
        {
            problem = $"The AI runtime version could not be read: {ex.Message}";
            return false;
        }
        if (!Directory.Exists(RuntimeRoot))
        {
            problem = "The RVC runtime folder is missing.";
            return false;
        }
        if (!File.Exists(Path.Combine(RuntimeRoot, "assets", "hubert_base", "pytorch_model.bin"))
            || !File.Exists(Path.Combine(RuntimeRoot, "assets", "hubert_base", "config.json")))
        {
            problem = "The RVC ContentVec/Hubert inference assets are incomplete.";
            return false;
        }
        if (!File.Exists(Path.Combine(RuntimeRoot, "assets", "rmvpe", "rmvpe.pt"))
            || !File.Exists(Path.Combine(RuntimeRoot, "assets", "rmvpe", "rmvpe.onnx")))
        {
            problem = "The RVC RMVPE pitch assets are incomplete (PyTorch/DirectML).";
            return false;
        }
        if (!File.Exists(BridgeScriptPath))
        {
            problem = "The VoiSee RVC bridge script is missing from this build.";
            return false;
        }

        problem = null;
        return true;
    }

    public Process StartInstaller()
    {
        if (!File.Exists(InstallerScriptPath))
        {
            throw new FileNotFoundException("AI runtime installer script was not found.", InstallerScriptPath);
        }

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(InstallerScriptPath);
        psi.ArgumentList.Add("-RuntimeRoot");
        psi.ArgumentList.Add(RuntimeRoot);
        return Process.Start(psi) ?? throw new InvalidOperationException("AI runtime installer could not be started.");
    }

    public AiVoiceRuntimeConfiguration PrepareRvcConfiguration(AiVoicePreset voice, AiPerformanceProfile profile)
    {
        if (voice.FilePath is null || !File.Exists(voice.FilePath))
        {
            throw new FileNotFoundException("The selected .vsee package no longer exists.", voice.FilePath);
        }
        if (!string.Equals(voice.Engine, "RVC", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("VoiSee 13.7 realtime runtime currently supports imported/downloaded RVC .pth voices. Quick/Optimized reference voices use the next zero-shot runtime Gate.");
        }
        if (!IsInstalled(out var problem))
        {
            throw new InvalidOperationException(problem ?? "AI runtime is not installed.");
        }

        var packageInfo = new FileInfo(voice.FilePath);
        var revision = $"{packageInfo.Length:x}-{packageInfo.LastWriteTimeUtc.Ticks:x}";
        var voiceRoot = Path.Combine(ModelCacheRoot, SanitizePathSegment(voice.Id));
        var revisionRoot = Path.Combine(voiceRoot, revision);
        Directory.CreateDirectory(revisionRoot);

        var modelPath = Path.Combine(revisionRoot, "voice.pth");
        var indexPath = Path.Combine(revisionRoot, "voice.index");
        if (!File.Exists(modelPath))
        {
            ExtractRvcAssets(voice.FilePath, modelPath, indexPath);
            try
            {
                foreach (var old in Directory.EnumerateDirectories(voiceRoot))
                {
                    if (!string.Equals(Path.GetFullPath(old), Path.GetFullPath(revisionRoot), StringComparison.OrdinalIgnoreCase))
                    {
                        try { Directory.Delete(old, recursive: true); } catch { }
                    }
                }
            }
            catch { }
        }

        if (!File.Exists(modelPath))
        {
            throw new InvalidDataException("The .vsee package does not contain an RVC .pth model.");
        }

        return new AiVoiceRuntimeConfiguration(
            AiVoiceRuntimeBackend.Rvc,
            PythonPath,
            BridgeScriptPath,
            RuntimeRoot,
            modelPath,
            File.Exists(indexPath) ? indexPath : null,
            profile,
            voice.Id,
            voice.Name);
    }

    private static void ExtractRvcAssets(string packagePath, string modelDestination, string indexDestination)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var model = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".pth", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The .vsee package does not contain an RVC .pth model.");
        var index = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".index", StringComparison.OrdinalIgnoreCase));

        Directory.CreateDirectory(Path.GetDirectoryName(modelDestination)!);
        model.ExtractToFile(modelDestination, overwrite: true);
        if (index is not null)
        {
            index.ExtractToFile(indexDestination, overwrite: true);
        }
        else if (File.Exists(indexDestination))
        {
            File.Delete(indexDestination);
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var result = new string(chars).Trim().Trim('.');
        // A manifest-provided ID is untrusted input. Never allow dot segments to
        // turn a cache folder into a parent-directory traversal on Windows.
        result = result.Replace('.', '_');
        return string.IsNullOrWhiteSpace(result) ? "voice" : result;
    }
}
