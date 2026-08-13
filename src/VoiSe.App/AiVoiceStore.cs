using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using NAudio.Wave;

namespace VoiSe.App;

public sealed class AiVoiceStore
{
    public const string VoiceExtension = ".vsee";
    private const string ManifestEntryName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AiVoiceStore(string storageDirectory)
    {
        if (string.IsNullOrWhiteSpace(storageDirectory))
        {
            throw new ArgumentException("AI Voice storage directory is required.", nameof(storageDirectory));
        }

        StorageDirectory = Path.GetFullPath(storageDirectory);
        Directory.CreateDirectory(StorageDirectory);
    }

    public string StorageDirectory { get; }

    public IReadOnlyList<AiVoicePreset> LoadVoices()
    {
        Directory.CreateDirectory(StorageDirectory);
        var voices = new List<AiVoicePreset>();
        foreach (var file in Directory.EnumerateFiles(StorageDirectory, "*" + VoiceExtension)
                     .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            try
            {
                voices.Add(LoadPackage(file));
            }
            catch
            {
                // A damaged or foreign package must never prevent VoiSee startup.
            }
        }

        return voices;
    }

    public AiVoicePreset LoadPackage(string packagePath)
    {
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("AI Voice package not found.", packagePath);
        }

        using var archive = ZipFile.OpenRead(packagePath);
        var manifestEntry = archive.GetEntry(ManifestEntryName)
            ?? throw new InvalidDataException("The selected .vsee file does not contain manifest.json.");

        using var stream = manifestEntry.Open();
        var preset = JsonSerializer.Deserialize<AiVoicePreset>(stream, JsonOptions)
            ?? throw new InvalidDataException("The selected .vsee manifest is invalid.");

        preset.SchemaVersion = Math.Max(1, preset.SchemaVersion);
        preset.Id = string.IsNullOrWhiteSpace(preset.Id) ? Guid.NewGuid().ToString("N") : preset.Id;
        // The installed package filename is the user-facing preset name. This lets Rename stay instant
        // even for very large model packages without rewriting their payload.
        preset.Name = Path.GetFileNameWithoutExtension(packagePath);
        preset.AssetEntries ??= new List<string>();
        NormalizeCompatibility(preset);
        preset.FilePath = Path.GetFullPath(packagePath);
        return preset;
    }

    public AiVoicePreset CreateReferenceVoice(string sourceAudioPath)
    {
        if (!File.Exists(sourceAudioPath))
        {
            throw new FileNotFoundException("Reference audio file not found.", sourceAudioPath);
        }

        var suggestedName = Path.GetFileNameWithoutExtension(sourceAudioPath);
        var packagePath = GetAvailablePackagePath(suggestedName);
        var audioEntry = "reference/" + SanitizeEntryFileName(Path.GetFileName(sourceAudioPath));
        var preset = new AiVoicePreset
        {
            Name = Path.GetFileNameWithoutExtension(packagePath),
            VoiceType = "Reference",
            Engine = "Pending",
            Compatibility = "Ready for Quick Clone",
            Source = "Reference audio",
            ReferenceAudioEntry = audioEntry,
            AssetEntries = new List<string> { audioEntry },
            Notes = "Reference audio stored. Use Quick Clone or Optimize Voice from the Selected Voice panel."
        };

        WritePackage(packagePath, preset, new[] { (sourceAudioPath, audioEntry) });
        preset.FilePath = packagePath;
        return preset;
    }

    public AiVoicePreset CreateImportedModel(
        IReadOnlyList<string> modelFiles,
        string? requestedName = null,
        string source = "Local import",
        string? notes = null)
    {
        var validFiles = modelFiles.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (validFiles.Count == 0)
        {
            throw new InvalidDataException("No model files were selected.");
        }

        var primary = validFiles.FirstOrDefault(p => string.Equals(Path.GetExtension(p), ".pth", StringComparison.OrdinalIgnoreCase))
            ?? validFiles.First();
        var packagePath = GetAvailablePackagePath(string.IsNullOrWhiteSpace(requestedName)
            ? Path.GetFileNameWithoutExtension(primary)
            : requestedName!);
        var assets = new List<(string Source, string Entry)>();
        foreach (var file in validFiles)
        {
            var entry = "model/" + SanitizeEntryFileName(Path.GetFileName(file));
            assets.Add((file, entry));
        }

        var hasPth = validFiles.Any(p => string.Equals(Path.GetExtension(p), ".pth", StringComparison.OrdinalIgnoreCase));
        var hasIndex = validFiles.Any(p => string.Equals(Path.GetExtension(p), ".index", StringComparison.OrdinalIgnoreCase));
        var hasOnnx = validFiles.Any(p => string.Equals(Path.GetExtension(p), ".onnx", StringComparison.OrdinalIgnoreCase));
        var engine = hasPth ? "RVC" : hasOnnx ? "ONNX" : "Unknown";
        var compatibility = hasPth
            ? (hasIndex ? "RVC activation candidate" : "RVC activation candidate · index optional")
            : hasOnnx ? "Not supported · conversion required" : "Not supported";

        var preset = new AiVoicePreset
        {
            Name = Path.GetFileNameWithoutExtension(packagePath),
            VoiceType = "Imported",
            Engine = engine,
            Compatibility = compatibility,
            Source = string.IsNullOrWhiteSpace(source) ? "Local import" : source,
            AssetEntries = assets.Select(a => a.Entry).ToList(),
            Notes = string.IsNullOrWhiteSpace(notes)
                ? "Stored successfully. Runtime compatibility is verified when the AI backend is connected."
                : notes
        };

        WritePackage(packagePath, preset, assets);
        preset.FilePath = packagePath;
        return preset;
    }

    public AiVoicePreset PrepareQuickClone(AiVoicePreset preset)
    {
        if (preset.FilePath is null || !File.Exists(preset.FilePath))
        {
            throw new FileNotFoundException("The selected AI Voice package no longer exists.", preset.FilePath);
        }

        if (!preset.CanQuickClone || string.IsNullOrWhiteSpace(preset.ReferenceAudioEntry))
        {
            throw new InvalidOperationException("Quick Clone can only be prepared from a Reference voice.");
        }

        var packagePath = Path.GetFullPath(preset.FilePath);
        var quickTempDirectory = Path.Combine(Path.GetTempPath(), "VoiSee", "QuickClone", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(quickTempDirectory);
        SpeechContentAnalysis speechAnalysis;
        double durationSeconds;
        try
        {
            var extractedReferencePath = ExtractReferenceAudio(packagePath, preset.ReferenceAudioEntry, quickTempDirectory);
            var decoded = DecodeReferenceToMono(extractedReferencePath);
            if (decoded.Samples.Length == 0 || decoded.SampleRate <= 0)
            {
                throw new InvalidDataException("The reference audio could not be decoded into usable samples.");
            }

            durationSeconds = decoded.Samples.Length / (double)decoded.SampleRate;
            if (durationSeconds < 10.0)
            {
                throw new InvalidDataException($"Reference audio is {durationSeconds:0.0} seconds long. Quick Clone requires at least 10 seconds of reference audio.");
            }

            speechAnalysis = AnalyzeSpeechContent(decoded.Samples, decoded.SampleRate);
            ValidateSpeechContent(speechAnalysis, "Quick Clone");
        }
        finally
        {
            try { Directory.Delete(quickTempDirectory, recursive: true); } catch { }
        }

        preset.SchemaVersion = Math.Max(4, preset.SchemaVersion);
        preset.VoiceType = "Quick Clone";
        preset.Engine = "Quick Clone";
        preset.Compatibility = "Quick Clone ready · runtime pending";
        preset.ReferenceDurationSeconds = durationSeconds;
        preset.QuickPreparedUtc = DateTimeOffset.UtcNow;
        preset.QuickProfileEntry = "quick/profile.json";
        if (!preset.AssetEntries.Contains(preset.QuickProfileEntry, StringComparer.OrdinalIgnoreCase))
        {
            preset.AssetEntries.Add(preset.QuickProfileEntry);
        }
        preset.Notes = "Quick Clone profile prepared from the stored reference. Realtime AI inference is connected in a later runtime Gate.";

        RewritePackageWithQuickProfile(packagePath, preset, speechAnalysis);
        preset.FilePath = packagePath;
        return preset;
    }

    public AiVoicePreset PrepareOptimizedVoice(AiVoicePreset preset)
    {
        if (preset.FilePath is null || !File.Exists(preset.FilePath))
        {
            throw new FileNotFoundException("The selected AI Voice package no longer exists.", preset.FilePath);
        }

        if (!preset.CanOptimize || string.IsNullOrWhiteSpace(preset.ReferenceAudioEntry))
        {
            throw new InvalidOperationException("Optimize Voice can only be prepared from a Reference or Quick Clone voice that still contains its reference audio.");
        }

        var packagePath = Path.GetFullPath(preset.FilePath);
        var tempDirectory = Path.Combine(Path.GetTempPath(), "VoiSee", "OptimizeVoice", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            string extractedReferencePath;
            using (var archive = ZipFile.OpenRead(packagePath))
            {
                var referenceEntry = archive.GetEntry(preset.ReferenceAudioEntry)
                    ?? throw new InvalidDataException("The .vsee package no longer contains its reference audio.");
                var extension = Path.GetExtension(referenceEntry.Name);
                extractedReferencePath = Path.Combine(tempDirectory, "reference" + extension);
                referenceEntry.ExtractToFile(extractedReferencePath, overwrite: true);
            }

            var decoded = DecodeReferenceToMono(extractedReferencePath);
            if (decoded.Samples.Length == 0 || decoded.SampleRate <= 0)
            {
                throw new InvalidDataException("The reference audio could not be decoded into usable samples.");
            }

            var speechAnalysis = AnalyzeSpeechContent(decoded.Samples, decoded.SampleRate);
            ValidateSpeechContent(speechAnalysis, "Optimize Voice");

            var analysis = AnalyzeAndNormalizeReference(decoded.Samples, decoded.SampleRate);
            if (analysis.UsableDurationSeconds < 10.0)
            {
                throw new InvalidDataException($"Reference audio contains only {analysis.UsableDurationSeconds:0.0} seconds after trimming. Optimize Voice requires at least 10 seconds of reference audio.");
            }

            var optimizedSamples = ResampleLinear(analysis.NormalizedSamples, decoded.SampleRate, 48000);
            var optimizedReferencePath = Path.Combine(tempDirectory, "reference_48k_mono.wav");
            WritePcm16Wave(optimizedReferencePath, optimizedSamples, 48000);

            stopwatch.Stop();
            preset.SchemaVersion = Math.Max(4, preset.SchemaVersion);
            preset.VoiceType = "Optimized";
            preset.Engine = "Optimized";
            preset.Compatibility = "Optimized voice · runtime pending";
            preset.ReferenceDurationSeconds = decoded.Samples.Length / (double)decoded.SampleRate;
            preset.OptimizedReferenceDurationSeconds = optimizedSamples.Length / 48000.0;
            preset.ReferenceQuality = analysis.Quality;
            preset.OptimizedPreparedUtc = DateTimeOffset.UtcNow;
            preset.OptimizationDurationMs = stopwatch.Elapsed.TotalMilliseconds;
            preset.OptimizedReferenceEntry = "optimized/reference_48k_mono.wav";
            preset.OptimizedProfileEntry = "optimized/profile.json";
            preset.QuickProfileEntry = null;
            preset.Notes = "Reference decoded, silence-trimmed, level-normalized and converted to canonical 48 kHz mono PCM. The neural realtime runtime is connected in a later Gate.";

            preset.AssetEntries.RemoveAll(entry =>
                string.Equals(entry, "quick/profile.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry, "optimized/profile.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry, "optimized/reference_48k_mono.wav", StringComparison.OrdinalIgnoreCase));
            preset.AssetEntries.Add(preset.OptimizedReferenceEntry);
            preset.AssetEntries.Add(preset.OptimizedProfileEntry);

            RewritePackageWithOptimizedProfile(packagePath, preset, optimizedReferencePath, analysis, speechAnalysis);
            preset.FilePath = packagePath;
            return preset;
        }
        finally
        {
            try { Directory.Delete(tempDirectory, recursive: true); } catch { }
        }
    }

    public AiVoicePreset ImportPackage(string sourcePackagePath)
    {
        var source = LoadPackage(sourcePackagePath); // validates before copying
        var destination = GetAvailablePackagePath(source.Name);
        File.Copy(sourcePackagePath, destination, overwrite: false);
        return LoadPackage(destination);
    }

    public string Rename(AiVoicePreset preset, string newName)
    {
        if (preset.FilePath is null || !File.Exists(preset.FilePath))
        {
            throw new FileNotFoundException("The selected AI Voice package no longer exists.", preset.FilePath);
        }

        var cleanName = MakeSafeFileName(newName);
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            throw new InvalidDataException("Enter a valid voice name.");
        }

        var currentPath = Path.GetFullPath(preset.FilePath);
        var targetPath = Path.Combine(StorageDirectory, cleanName + VoiceExtension);
        if (string.Equals(currentPath, Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            preset.Name = cleanName;
            return currentPath;
        }

        if (File.Exists(targetPath))
        {
            throw new IOException($"An AI Voice named '{cleanName}' already exists.");
        }

        File.Move(currentPath, targetPath);
        preset.Name = cleanName;
        preset.FilePath = targetPath;
        return targetPath;
    }

    public void Delete(AiVoicePreset preset)
    {
        if (!string.IsNullOrWhiteSpace(preset.FilePath) && File.Exists(preset.FilePath))
        {
            File.Delete(preset.FilePath);
        }
    }

    public void Export(AiVoicePreset preset, string destinationPath)
    {
        if (preset.FilePath is null || !File.Exists(preset.FilePath))
        {
            throw new FileNotFoundException("The selected AI Voice package no longer exists.", preset.FilePath);
        }

        File.Copy(preset.FilePath, destinationPath, overwrite: true);
    }

    private static void NormalizeCompatibility(AiVoicePreset preset)
    {
        var assets = preset.AssetEntries ?? new List<string>();
        var hasPth = assets.Any(entry => entry.EndsWith(".pth", StringComparison.OrdinalIgnoreCase));
        var hasIndex = assets.Any(entry => entry.EndsWith(".index", StringComparison.OrdinalIgnoreCase));
        var hasOnnx = assets.Any(entry => entry.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));

        if (string.Equals(preset.VoiceType, "Quick Clone", StringComparison.OrdinalIgnoreCase))
        {
            preset.Engine = "Quick Clone";
            preset.Compatibility = "Quick Clone ready · runtime pending";
            return;
        }

        if (string.Equals(preset.VoiceType, "Optimized", StringComparison.OrdinalIgnoreCase))
        {
            preset.Engine = string.IsNullOrWhiteSpace(preset.Engine) ? "Optimized" : preset.Engine;
            preset.Compatibility = "Optimized voice · runtime pending";
            return;
        }

        if (hasPth)
        {
            preset.Engine = "RVC";
            preset.Compatibility = hasIndex
                ? "RVC activation candidate"
                : "RVC activation candidate · index optional";
            return;
        }

        if (hasOnnx)
        {
            preset.Engine = "ONNX";
            preset.Compatibility = "Not supported · conversion required";
            return;
        }

        if (string.Equals(preset.VoiceType, "Reference", StringComparison.OrdinalIgnoreCase))
        {
            preset.Engine = "Pending";
            preset.Compatibility = "Ready for Quick Clone";
            return;
        }

        preset.Compatibility = "Not supported";
    }

    private sealed record DecodedReference(float[] Samples, int SampleRate);

    private sealed record ReferenceOptimizationAnalysis(
        float[] NormalizedSamples,
        double UsableDurationSeconds,
        double TrimmedLeadingSeconds,
        double TrimmedTrailingSeconds,
        double PeakBeforeNormalization,
        double RmsBeforeNormalization,
        double AppliedGain,
        string Quality);

    private sealed record SpeechContentAnalysis(
        double TotalDurationSeconds,
        double ActiveDurationSeconds,
        double VoicedDurationSeconds,
        double ActiveRatio,
        double VoicedRatioOfActive,
        double MedianActiveRms,
        double MedianVoicedPeriodicity);

    private static DecodedReference DecodeReferenceToMono(string referencePath)
    {
        var extension = Path.GetExtension(referencePath);
        if (string.Equals(extension, ".ogg", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new NAudio.Vorbis.VorbisWaveReader(referencePath);
            var provider = reader.ToSampleProvider();
            return ReadProviderToMono(provider, reader.WaveFormat.Channels, reader.WaveFormat.SampleRate);
        }

        using var audio = new NAudio.Wave.AudioFileReader(referencePath);
        return ReadProviderToMono(audio, audio.WaveFormat.Channels, audio.WaveFormat.SampleRate);
    }

    private static DecodedReference ReadProviderToMono(NAudio.Wave.ISampleProvider provider, int channels, int sampleRate)
    {
        channels = Math.Max(1, channels);
        var readBuffer = new float[4096 * channels];
        var mono = new List<float>();
        while (true)
        {
            var read = provider.Read(readBuffer, 0, readBuffer.Length);
            if (read <= 0) break;
            var frames = read / channels;
            for (var frame = 0; frame < frames; frame++)
            {
                double sum = 0;
                var baseIndex = frame * channels;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += readBuffer[baseIndex + channel];
                }
                mono.Add((float)(sum / channels));
            }
        }

        return new DecodedReference(mono.ToArray(), sampleRate);
    }

    private static string ExtractReferenceAudio(string packagePath, string referenceEntryName, string destinationDirectory)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var referenceEntry = archive.GetEntry(referenceEntryName)
            ?? throw new InvalidDataException("The .vsee package no longer contains its reference audio.");
        var extension = Path.GetExtension(referenceEntry.Name);
        var destinationPath = Path.Combine(destinationDirectory, "reference" + extension);
        referenceEntry.ExtractToFile(destinationPath, overwrite: true);
        return destinationPath;
    }

    private static void ValidateSpeechContent(SpeechContentAnalysis analysis, string operationName)
    {
        // Duration alone is not sufficient for a voice reference. Long breaths, room noise and
        // sustained non-speech sounds can have plenty of energy, so require a meaningful amount
        // of periodic/voiced material as well as active audio.
        const double MinimumActiveSeconds = 6.0;
        const double MinimumVoicedSeconds = 2.5;
        const double MinimumVoicedRatioOfActive = 0.18;

        if (analysis.ActiveDurationSeconds < MinimumActiveSeconds)
        {
            throw new InvalidDataException($"{operationName} needs spoken reference audio. Only {analysis.ActiveDurationSeconds:0.0} seconds of active audio were detected; record at least 10 seconds containing normal speech.");
        }

        if (analysis.VoicedDurationSeconds < MinimumVoicedSeconds
            || analysis.VoicedRatioOfActive < MinimumVoicedRatioOfActive)
        {
            throw new InvalidDataException($"{operationName} needs actual spoken voice, not only breathing, noise or silence. Detected about {analysis.VoicedDurationSeconds:0.0} seconds of voiced speech-like material. Use a reference with several seconds of normal words and phrases.");
        }
    }

    private static SpeechContentAnalysis AnalyzeSpeechContent(float[] samples, int sampleRate)
    {
        if (samples.Length == 0 || sampleRate <= 0)
        {
            return new SpeechContentAnalysis(0, 0, 0, 0, 0, 0, 0);
        }

        // Speech-content validation is intentionally lightweight and offline. Analyze at 8 kHz:
        // this keeps the CPU cost small while preserving the pitch band needed to distinguish
        // voiced speech from broadband breathing/noise.
        const int analysisRate = 8000;
        var analysisSamples = sampleRate == analysisRate
            ? samples
            : ResampleLinear(samples, sampleRate, analysisRate);

        var frameSize = Math.Max(80, (int)Math.Round(analysisRate * 0.030)); // 30 ms
        var hopSize = Math.Max(40, (int)Math.Round(analysisRate * 0.015));   // 15 ms
        if (analysisSamples.Length < frameSize)
        {
            return new SpeechContentAnalysis(samples.Length / (double)sampleRate, 0, 0, 0, 0, 0, 0);
        }

        var frameRms = new List<double>();
        var frameZcr = new List<double>();
        var framePeriodicity = new List<double>();

        for (var start = 0; start + frameSize <= analysisSamples.Length; start += hopSize)
        {
            double mean = 0;
            for (var i = 0; i < frameSize; i++) mean += analysisSamples[start + i];
            mean /= frameSize;

            double squares = 0;
            var zeroCrossings = 0;
            var previous = analysisSamples[start] - mean;
            for (var i = 0; i < frameSize; i++)
            {
                var value = analysisSamples[start + i] - mean;
                squares += value * value;
                if (i > 0 && ((previous >= 0 && value < 0) || (previous < 0 && value >= 0))) zeroCrossings++;
                previous = value;
            }

            var rms = Math.Sqrt(squares / frameSize);
            var zcr = zeroCrossings / (double)Math.Max(1, frameSize - 1);
            var periodicity = rms < 0.001 ? 0 : EstimateVoicedPeriodicity(analysisSamples, start, frameSize, analysisRate, mean, squares);
            frameRms.Add(rms);
            frameZcr.Add(zcr);
            framePeriodicity.Add(periodicity);
        }

        if (frameRms.Count == 0)
        {
            return new SpeechContentAnalysis(samples.Length / (double)sampleRate, 0, 0, 0, 0, 0, 0);
        }

        var sortedRms = frameRms.OrderBy(value => value).ToArray();
        var p20 = sortedRms[(int)Math.Clamp(Math.Round((sortedRms.Length - 1) * 0.20), 0, sortedRms.Length - 1)];
        var p90 = sortedRms[(int)Math.Clamp(Math.Round((sortedRms.Length - 1) * 0.90), 0, sortedRms.Length - 1)];
        // Do not assume the file contains silence from which a noise floor can be learned.
        // The percentile cap keeps continuously spoken references from being classified as inactive.
        var activeThreshold = Math.Max(0.003, Math.Min(p20 * 2.5, p90 * 0.25));
        var activeFrames = new List<int>();
        var voicedFrames = new List<int>();

        for (var i = 0; i < frameRms.Count; i++)
        {
            if (frameRms[i] < activeThreshold) continue;
            activeFrames.Add(i);

            // Normal voiced speech contains a stable fundamental period for a meaningful subset
            // of frames. Breathing is generally broadband/aperiodic and therefore fails this gate.
            if (framePeriodicity[i] >= 0.35 && frameZcr[i] <= 0.18)
            {
                voicedFrames.Add(i);
            }
        }

        var frameSeconds = hopSize / (double)analysisRate;
        var activeDuration = activeFrames.Count * frameSeconds;
        var voicedDuration = voicedFrames.Count * frameSeconds;
        var activeRatio = activeFrames.Count / (double)frameRms.Count;
        var voicedRatio = activeFrames.Count == 0 ? 0 : voicedFrames.Count / (double)activeFrames.Count;
        var medianActiveRms = activeFrames.Count == 0 ? 0 : Median(activeFrames.Select(i => frameRms[i]));
        var medianVoicedPeriodicity = voicedFrames.Count == 0 ? 0 : Median(voicedFrames.Select(i => framePeriodicity[i]));

        return new SpeechContentAnalysis(
            samples.Length / (double)sampleRate,
            activeDuration,
            voicedDuration,
            activeRatio,
            voicedRatio,
            medianActiveRms,
            medianVoicedPeriodicity);
    }

    private static double EstimateVoicedPeriodicity(float[] samples, int start, int frameSize, int sampleRate, double mean, double frameEnergy)
    {
        if (frameEnergy <= 1e-9) return 0;
        var minLag = Math.Max(1, sampleRate / 400); // ~400 Hz
        var maxLag = Math.Min(frameSize / 2, sampleRate / 70); // ~70 Hz
        double best = 0;

        for (var lag = minLag; lag <= maxLag; lag++)
        {
            double correlation = 0;
            double leftEnergy = 0;
            double rightEnergy = 0;
            var length = frameSize - lag;
            for (var i = 0; i < length; i++)
            {
                var left = samples[start + i] - mean;
                var right = samples[start + i + lag] - mean;
                correlation += left * right;
                leftEnergy += left * left;
                rightEnergy += right * right;
            }

            var denominator = Math.Sqrt(leftEnergy * rightEnergy);
            if (denominator <= 1e-9) continue;
            var normalized = correlation / denominator;
            if (normalized > best) best = normalized;
        }

        return Math.Clamp(best, 0, 1);
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0) return 0;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2.0
            : ordered[middle];
    }

    private static ReferenceOptimizationAnalysis AnalyzeAndNormalizeReference(float[] samples, int sampleRate)
    {
        if (samples.Length == 0) throw new InvalidDataException("Reference audio is empty.");

        double peak = 0;
        double sumSquares = 0;
        double mean = 0;
        foreach (var sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
            sumSquares += sample * sample;
            mean += sample;
        }
        mean /= samples.Length;
        var rms = Math.Sqrt(sumSquares / samples.Length);
        if (peak < 0.0005 || rms < 0.0001)
        {
            throw new InvalidDataException("Reference audio is effectively silent.");
        }

        var threshold = Math.Max(0.004, peak * 0.015);
        var first = 0;
        while (first < samples.Length && Math.Abs(samples[first] - mean) < threshold) first++;
        var last = samples.Length - 1;
        while (last > first && Math.Abs(samples[last] - mean) < threshold) last--;
        if (last <= first)
        {
            throw new InvalidDataException("No usable speech was detected in the reference audio.");
        }

        var padding = Math.Max(1, (int)Math.Round(sampleRate * 0.15));
        var start = Math.Max(0, first - padding);
        var end = Math.Min(samples.Length - 1, last + padding);
        var count = end - start + 1;
        var trimmed = new float[count];

        double trimmedPeak = 0;
        double trimmedSquares = 0;
        for (var i = 0; i < count; i++)
        {
            var centered = (float)(samples[start + i] - mean);
            trimmed[i] = centered;
            trimmedPeak = Math.Max(trimmedPeak, Math.Abs(centered));
            trimmedSquares += centered * centered;
        }
        var trimmedRms = Math.Sqrt(trimmedSquares / count);
        var gain = trimmedPeak > 0 ? Math.Min(4.0, 0.92 / trimmedPeak) : 1.0;
        for (var i = 0; i < trimmed.Length; i++)
        {
            trimmed[i] = (float)Math.Clamp(trimmed[i] * gain, -0.98, 0.98);
        }

        var usableSeconds = count / (double)sampleRate;
        var leading = start / (double)sampleRate;
        var trailing = (samples.Length - end - 1) / (double)sampleRate;
        var clippingRatio = samples.Count(sample => Math.Abs(sample) >= 0.995f) / (double)samples.Length;
        var quality = usableSeconds >= 30 && trimmedRms >= 0.025 && clippingRatio < 0.005
            ? "Good"
            : usableSeconds >= 10 && trimmedRms >= 0.012 && clippingRatio < 0.02
                ? "Fair"
                : "Poor";

        return new ReferenceOptimizationAnalysis(
            trimmed,
            usableSeconds,
            leading,
            trailing,
            trimmedPeak,
            trimmedRms,
            gain,
            quality);
    }

    private static float[] ResampleLinear(float[] input, int inputRate, int outputRate)
    {
        if (input.Length == 0 || inputRate <= 0 || outputRate <= 0) return Array.Empty<float>();
        if (inputRate == outputRate) return input.ToArray();
        var outputLength = Math.Max(1, (int)Math.Round(input.Length * (double)outputRate / inputRate));
        var output = new float[outputLength];
        var ratio = inputRate / (double)outputRate;
        for (var i = 0; i < outputLength; i++)
        {
            var sourcePosition = i * ratio;
            var index = Math.Min(input.Length - 1, (int)sourcePosition);
            var next = Math.Min(input.Length - 1, index + 1);
            var fraction = sourcePosition - index;
            output[i] = (float)(input[index] + ((input[next] - input[index]) * fraction));
        }
        return output;
    }

    private static void WritePcm16Wave(string path, float[] samples, int sampleRate)
    {
        using var writer = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(sampleRate, 16, 1));
        var bytes = new byte[8192];
        var byteIndex = 0;
        foreach (var sample in samples)
        {
            var pcm = (short)Math.Round(Math.Clamp(sample, -1.0f, 1.0f) * short.MaxValue);
            bytes[byteIndex++] = (byte)(pcm & 0xff);
            bytes[byteIndex++] = (byte)((pcm >> 8) & 0xff);
            if (byteIndex >= bytes.Length)
            {
                writer.Write(bytes, 0, byteIndex);
                byteIndex = 0;
            }
        }
        if (byteIndex > 0) writer.Write(bytes, 0, byteIndex);
    }

    private static void RewritePackageWithOptimizedProfile(
        string packagePath,
        AiVoicePreset preset,
        string optimizedReferencePath,
        ReferenceOptimizationAnalysis analysis,
        SpeechContentAnalysis speechAnalysis)
    {
        var tempPackage = packagePath + ".optimized.tmp";
        if (File.Exists(tempPackage)) File.Delete(tempPackage);

        using (var source = ZipFile.OpenRead(packagePath))
        using (var destination = ZipFile.Open(tempPackage, ZipArchiveMode.Create))
        {
            foreach (var entry in source.Entries)
            {
                if (string.Equals(entry.FullName, ManifestEntryName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.FullName, "quick/profile.json", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.FullName, "optimized/profile.json", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.FullName, "optimized/reference_48k_mono.wav", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var copy = destination.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                using var input = entry.Open();
                using var output = copy.Open();
                input.CopyTo(output);
            }

            var optimizedAudio = destination.CreateEntry("optimized/reference_48k_mono.wav", CompressionLevel.NoCompression);
            using (var input = File.OpenRead(optimizedReferencePath))
            using (var output = optimizedAudio.Open())
            {
                input.CopyTo(output);
            }

            var optimizedProfile = destination.CreateEntry("optimized/profile.json", CompressionLevel.Fastest);
            using (var stream = optimizedProfile.Open())
            {
                var profile = new
                {
                    schema = 2,
                    kind = "optimized-reference",
                    sourceReferenceEntry = preset.ReferenceAudioEntry,
                    canonicalReferenceEntry = preset.OptimizedReferenceEntry,
                    sampleRate = 48000,
                    channels = 1,
                    format = "pcm16",
                    usableDurationSeconds = analysis.UsableDurationSeconds,
                    trimmedLeadingSeconds = analysis.TrimmedLeadingSeconds,
                    trimmedTrailingSeconds = analysis.TrimmedTrailingSeconds,
                    peakBeforeNormalization = analysis.PeakBeforeNormalization,
                    rmsBeforeNormalization = analysis.RmsBeforeNormalization,
                    appliedGain = analysis.AppliedGain,
                    referenceQuality = analysis.Quality,
                    speechContent = new
                    {
                        activeDurationSeconds = speechAnalysis.ActiveDurationSeconds,
                        voicedDurationSeconds = speechAnalysis.VoicedDurationSeconds,
                        activeRatio = speechAnalysis.ActiveRatio,
                        voicedRatioOfActive = speechAnalysis.VoicedRatioOfActive,
                        medianActiveRms = speechAnalysis.MedianActiveRms,
                        medianVoicedPeriodicity = speechAnalysis.MedianVoicedPeriodicity
                    },
                    preparedUtc = preset.OptimizedPreparedUtc,
                    preparationDurationMs = preset.OptimizationDurationMs
                };
                JsonSerializer.Serialize(stream, profile, JsonOptions);
            }

            var manifest = destination.CreateEntry(ManifestEntryName, CompressionLevel.Fastest);
            using var manifestStream = manifest.Open();
            JsonSerializer.Serialize(manifestStream, preset, JsonOptions);
        }

        File.Move(tempPackage, packagePath, overwrite: true);
    }

    private static double MeasureReferenceDurationSeconds(string packagePath, string referenceEntryName)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "VoiSee", "QuickClone", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var referenceEntry = archive.GetEntry(referenceEntryName)
                ?? throw new InvalidDataException("The .vsee package no longer contains its reference audio.");
            var extension = Path.GetExtension(referenceEntry.Name);
            var tempPath = Path.Combine(tempDirectory, "reference" + extension);
            referenceEntry.ExtractToFile(tempPath, overwrite: true);

            if (string.Equals(extension, ".ogg", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new NAudio.Vorbis.VorbisWaveReader(tempPath);
                return reader.TotalTime.TotalSeconds;
            }

            using var audio = new NAudio.Wave.AudioFileReader(tempPath);
            return audio.TotalTime.TotalSeconds;
        }
        finally
        {
            try { Directory.Delete(tempDirectory, recursive: true); } catch { }
        }
    }

    private static void RewritePackageWithQuickProfile(string packagePath, AiVoicePreset preset, SpeechContentAnalysis speechAnalysis)
    {
        var tempPackage = packagePath + ".quick.tmp";
        if (File.Exists(tempPackage)) File.Delete(tempPackage);

        using (var source = ZipFile.OpenRead(packagePath))
        using (var destination = ZipFile.Open(tempPackage, ZipArchiveMode.Create))
        {
            foreach (var entry in source.Entries)
            {
                if (string.Equals(entry.FullName, ManifestEntryName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.FullName, "quick/profile.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var copy = destination.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                using var input = entry.Open();
                using var output = copy.Open();
                input.CopyTo(output);
            }

            var quickProfile = destination.CreateEntry("quick/profile.json", CompressionLevel.Fastest);
            using (var stream = quickProfile.Open())
            {
                var profile = new
                {
                    schema = 2,
                    kind = "zero-shot-reference",
                    referenceEntry = preset.ReferenceAudioEntry,
                    referenceDurationSeconds = preset.ReferenceDurationSeconds,
                    speechContent = new
                    {
                        activeDurationSeconds = speechAnalysis.ActiveDurationSeconds,
                        voicedDurationSeconds = speechAnalysis.VoicedDurationSeconds,
                        activeRatio = speechAnalysis.ActiveRatio,
                        voicedRatioOfActive = speechAnalysis.VoicedRatioOfActive,
                        medianActiveRms = speechAnalysis.MedianActiveRms,
                        medianVoicedPeriodicity = speechAnalysis.MedianVoicedPeriodicity
                    },
                    preparedUtc = preset.QuickPreparedUtc
                };
                JsonSerializer.Serialize(stream, profile, JsonOptions);
            }

            var manifest = destination.CreateEntry(ManifestEntryName, CompressionLevel.Fastest);
            using var manifestStream = manifest.Open();
            JsonSerializer.Serialize(manifestStream, preset, JsonOptions);
        }

        File.Move(tempPackage, packagePath, overwrite: true);
    }

    private static void WritePackage(
        string packagePath,
        AiVoicePreset preset,
        IEnumerable<(string Source, string Entry)> assets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var manifest = archive.CreateEntry(ManifestEntryName, CompressionLevel.Fastest);
        using (var stream = manifest.Open())
        {
            JsonSerializer.Serialize(stream, preset, JsonOptions);
        }

        foreach (var asset in assets)
        {
            var entry = archive.CreateEntry(asset.Entry, CompressionLevel.NoCompression);
            using var source = File.OpenRead(asset.Source);
            using var destination = entry.Open();
            source.CopyTo(destination);
        }
    }

    private string GetAvailablePackagePath(string requestedName)
    {
        var safeName = MakeSafeFileName(requestedName);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "AI Voice";
        var candidate = Path.Combine(StorageDirectory, safeName + VoiceExtension);
        var suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(StorageDirectory, $"{safeName} {suffix}{VoiceExtension}");
            suffix++;
        }
        return candidate;
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars).Trim().TrimEnd('.');
    }

    private static string SanitizeEntryFileName(string value)
    {
        var name = MakeSafeFileName(value);
        return string.IsNullOrWhiteSpace(name) ? "asset.bin" : name;
    }
}
