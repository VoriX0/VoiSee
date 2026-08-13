using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace VoiSe.Audio;

/// <summary>
/// Compute-only realtime AI Voice adapter. VoiSee owns the audio devices and sends
/// 48 kHz mono PCM to a separately installed RVC Python runtime over redirected
/// standard streams. The audio callback never waits for neural inference.
/// </summary>
internal sealed class RvcRealtimeAiVoiceProcessor : IAiVoiceRealtimeProcessor
{
    private const int SampleRate = 48_000;
    private const int MaxQueuedAudioMs = 500;

    private readonly AiVoiceRuntimeConfiguration _configuration;
    private readonly FloatSampleQueue _inputQueue;
    private readonly FloatSampleQueue _outputQueue;
    private readonly object _statusSync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private Task? _workerTask;
    private TaskCompletionSource<bool>? _readyTcs;
    private AiVoiceRuntimeState _state = AiVoiceRuntimeState.Disabled;
    private string? _error;
    private double _lastInferenceMs;
    private double _lastRoundTripMs;
    private long _outputUnderrunSamples;
    private int _blockSamples;
    private readonly int _blockMs;
    private bool _disposed;

    public RvcRealtimeAiVoiceProcessor(AiVoiceRuntimeConfiguration configuration)
    {
        _configuration = configuration;
        _blockMs = ProfileBlockMs(configuration.PerformanceProfile);
        _blockSamples = Math.Max(480, SampleRate * _blockMs / 1000);
        var maxQueuedSamples = SampleRate * MaxQueuedAudioMs / 1000;
        _inputQueue = new FloatSampleQueue(maxQueuedSamples);
        _outputQueue = new FloatSampleQueue(maxQueuedSamples);
    }

    public event Action<AiVoiceRuntimeStatus>? StatusChanged;

    public AiVoiceRuntimeStatus GetStatus()
    {
        lock (_statusSync)
        {
            return new AiVoiceRuntimeStatus(
                _state,
                _configuration.VoiceId,
                _configuration.VoiceName,
                _error,
                _blockMs,
                _lastInferenceMs,
                _lastRoundTripMs,
                SamplesToMilliseconds(_inputQueue.Count),
                SamplesToMilliseconds(_outputQueue.Count),
                _inputQueue.DroppedSampleCount,
                _outputUnderrunSamples);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateConfiguration();

        lock (_statusSync)
        {
            if (_state is AiVoiceRuntimeState.Starting or AiVoiceRuntimeState.Ready)
            {
                return;
            }
            _state = AiVoiceRuntimeState.Starting;
            _error = null;
            _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        RaiseStatusChanged();

        var startInfo = new ProcessStartInfo
        {
            FileName = _configuration.PythonExecutablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = _configuration.RuntimeRootPath,
        };
        startInfo.ArgumentList.Add(_configuration.BridgeScriptPath);
        startInfo.ArgumentList.Add("--rvc-root");
        startInfo.ArgumentList.Add(_configuration.RuntimeRootPath);
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(_configuration.PrimaryAssetPath);
        if (!string.IsNullOrWhiteSpace(_configuration.SecondaryAssetPath))
        {
            startInfo.ArgumentList.Add("--index");
            startInfo.ArgumentList.Add(_configuration.SecondaryAssetPath!);
        }
        startInfo.ArgumentList.Add("--profile");
        startInfo.ArgumentList.Add(_configuration.PerformanceProfile.ToString().ToLowerInvariant());
        startInfo.ArgumentList.Add("--sample-rate");
        startInfo.ArgumentList.Add(SampleRate.ToString(CultureInfo.InvariantCulture));

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.ErrorDataReceived += OnRuntimeErrorDataReceived;
        _process.Exited += (_, _) =>
        {
            if (_disposed || _lifetime.IsCancellationRequested)
            {
                return;
            }

            var code = 0;
            try { code = _process?.ExitCode ?? -1; } catch { code = -1; }
            SetFault($"AI runtime exited unexpectedly (code {code}).");
        };

        if (!_process.Start())
        {
            SetFault("The AI runtime process could not be started.");
            throw new InvalidOperationException(_error);
        }
        _process.BeginErrorReadLine();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        linked.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            await _readyTcs!.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            SetFault("AI runtime initialization timed out.");
            throw new TimeoutException(_error);
        }

        lock (_statusSync)
        {
            if (_state == AiVoiceRuntimeState.Faulted)
            {
                throw new InvalidOperationException(_error ?? "AI runtime initialization failed.");
            }
            _state = AiVoiceRuntimeState.Ready;
        }
        RaiseStatusChanged();
        _workerTask = Task.Run(() => RunInferenceLoopAsync(_lifetime.Token));
    }

    public void ProcessInPlace(float[] stereoSamples)
    {
        if (stereoSamples.Length == 0)
        {
            return;
        }

        var frameCount = stereoSamples.Length / 2;
        if (frameCount <= 0)
        {
            Array.Clear(stereoSamples);
            return;
        }

        var input = ArrayPool<float>.Shared.Rent(frameCount);
        var output = ArrayPool<float>.Shared.Rent(frameCount);
        try
        {
            for (var frame = 0; frame < frameCount; frame++)
            {
                var left = stereoSamples[frame * 2];
                var right = stereoSamples[(frame * 2) + 1];
                input[frame] = (left + right) * 0.5f;
            }
            AiVoiceRuntimeState state;
            lock (_statusSync) state = _state;
            if (state != AiVoiceRuntimeState.Ready)
            {
                // Never leak the raw microphone if an already-active AI runtime
                // faults. Silence/artifacts are safer than silently bypassing AI.
                Array.Clear(stereoSamples);
                return;
            }

            _inputQueue.Add(input.AsSpan(0, frameCount));
            var read = _outputQueue.Read(output, 0, frameCount);
            if (read < frameCount)
            {
                lock (_statusSync)
                {
                    _outputUnderrunSamples += frameCount - read;
                }
            }

            for (var frame = 0; frame < frameCount; frame++)
            {
                var sample = output[frame];
                stereoSamples[frame * 2] = sample;
                stereoSamples[(frame * 2) + 1] = sample;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(input);
            ArrayPool<float>.Shared.Return(output);
        }
    }

    public double EstimatedStageLatencyMs
    {
        get
        {
            var status = GetStatus();
            if (status.State != AiVoiceRuntimeState.Ready)
            {
                return 0.0;
            }
            var computeAndIpc = Math.Max(status.LastInferenceMs, status.LastRoundTripMs);
            return status.BlockMs + computeAndIpc + status.InputQueuedMs + status.OutputQueuedMs;
        }
    }

    private async Task RunInferenceLoopAsync(CancellationToken cancellationToken)
    {
        var process = _process ?? throw new InvalidOperationException("AI runtime process is not available.");
        var inputStream = process.StandardInput.BaseStream;
        var outputStream = process.StandardOutput.BaseStream;
        var inputSamples = new float[_blockSamples];
        var inputBytes = new byte[_blockSamples * sizeof(float)];
        var outputBytes = new byte[_blockSamples * sizeof(float)];
        var outputSamples = new float[_blockSamples];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_inputQueue.Count < _blockSamples)
                {
                    await Task.Delay(2, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _inputQueue.Read(inputSamples, 0, _blockSamples);
                Buffer.BlockCopy(inputSamples, 0, inputBytes, 0, inputBytes.Length);

                var stopwatch = Stopwatch.StartNew();
                await inputStream.WriteAsync(inputBytes, cancellationToken).ConfigureAwait(false);
                await inputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                await ReadExactlyAsync(outputStream, outputBytes, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();

                Buffer.BlockCopy(outputBytes, 0, outputSamples, 0, outputBytes.Length);
                _outputQueue.Add(outputSamples);
                lock (_statusSync)
                {
                    _lastRoundTripMs = stopwatch.Elapsed.TotalMilliseconds;
                    // The bridge also reports its own compute time on stderr. Until
                    // that first report arrives, round-trip is the conservative value.
                    if (_lastInferenceMs <= 0.0)
                    {
                        _lastInferenceMs = _lastRoundTripMs;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetFault($"AI runtime audio bridge failed: {ex.Message}");
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                throw new EndOfStreamException("AI runtime closed its audio output stream.");
            }
            offset += read;
        }
    }

    private void OnRuntimeErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        var line = e.Data;
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (line.StartsWith("VOISEE_READY ", StringComparison.Ordinal))
        {
            var blockSamples = ParseIntegerField(line, "block_samples");
            if (blockSamples > 0)
            {
                _blockSamples = blockSamples;
            }
            _readyTcs?.TrySetResult(true);
            return;
        }

        if (line.StartsWith("VOISEE_INFER ", StringComparison.Ordinal))
        {
            var inference = ParseDoubleField(line, "inference_ms");
            if (inference >= 0.0)
            {
                lock (_statusSync) _lastInferenceMs = inference;
            }
            return;
        }

        if (line.StartsWith("VOISEE_FATAL ", StringComparison.Ordinal))
        {
            var message = line["VOISEE_FATAL ".Length..].Trim();
            SetFault(message);
            _readyTcs?.TrySetException(new InvalidOperationException(message));
        }
    }

    private static int ParseIntegerField(string line, string key)
    {
        var raw = ParseField(line, key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static double ParseDoubleField(string line, string key)
    {
        var raw = ParseField(line, key);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : -1.0;
    }

    private static string? ParseField(string line, string key)
    {
        var marker = key + "=";
        var start = line.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        var end = line.IndexOf(' ', start);
        return end < 0 ? line[start..] : line[start..end];
    }

    private void ValidateConfiguration()
    {
        if (_configuration.Backend != AiVoiceRuntimeBackend.Rvc)
            throw new NotSupportedException($"The RVC processor cannot run backend '{_configuration.Backend}'.");
        if (!File.Exists(_configuration.PythonExecutablePath))
            throw new FileNotFoundException("AI runtime Python executable was not found.", _configuration.PythonExecutablePath);
        if (!File.Exists(_configuration.BridgeScriptPath))
            throw new FileNotFoundException("VoiSee RVC bridge script was not found.", _configuration.BridgeScriptPath);
        if (!Directory.Exists(_configuration.RuntimeRootPath))
            throw new DirectoryNotFoundException($"RVC runtime directory was not found: {_configuration.RuntimeRootPath}");
        if (!File.Exists(_configuration.PrimaryAssetPath))
            throw new FileNotFoundException("The selected RVC model was not found.", _configuration.PrimaryAssetPath);
    }

    private void SetFault(string message)
    {
        lock (_statusSync)
        {
            if (_disposed) return;
            _state = AiVoiceRuntimeState.Faulted;
            _error = message;
        }
        _readyTcs?.TrySetException(new InvalidOperationException(message));
        RaiseStatusChanged();
    }

    private void RaiseStatusChanged()
    {
        try { StatusChanged?.Invoke(GetStatus()); } catch { }
    }

    private static int ProfileBlockMs(AiPerformanceProfile profile) => profile switch
    {
        AiPerformanceProfile.Gaming => 80,
        AiPerformanceProfile.Quality => 200,
        _ => 120,
    };

    private static double SamplesToMilliseconds(int samples)
        => samples <= 0 ? 0.0 : samples * 1000.0 / SampleRate;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _inputQueue.Clear();
        _outputQueue.Clear();

        try { _process?.StandardInput.Close(); } catch { }
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch { }
        try { _process?.Dispose(); } catch { }
        _lifetime.Dispose();
    }
}
