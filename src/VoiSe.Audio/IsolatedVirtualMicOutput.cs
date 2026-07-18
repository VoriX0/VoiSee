using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using NAudio.Wave;

namespace VoiSe.Audio;

/// <summary>
/// Pulls the final virtual microphone mix from an ISampleProvider and sends it
/// to a detached helper process. The VoiSee UI process therefore owns no WASAPI
/// render stream for VB-CABLE.
/// </summary>
internal sealed class IsolatedVirtualMicOutput : IDisposable
{
    private const int BlockMilliseconds = 20;
    private readonly ISampleProvider _provider;
    private readonly string _deviceId;
    private readonly ManualResetEventSlim _stopRequested = new(false);
    private NamedPipeServerStream? _pipe;
    private BinaryWriter? _writer;
    private Process? _hostProcess;
    private Thread? _pumpThread;
    private volatile bool _running;
    private bool _disposed;

    public IsolatedVirtualMicOutput(ISampleProvider provider, string deviceId)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _deviceId = string.IsNullOrWhiteSpace(deviceId)
            ? throw new ArgumentException("A virtual output device ID is required.", nameof(deviceId))
            : deviceId;
    }

    public int? HostProcessId => _hostProcess?.HasExited == false ? _hostProcess.Id : null;
    public string? LastError { get; private set; }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_running)
        {
            throw new InvalidOperationException("The isolated virtual microphone output is already running.");
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new InvalidOperationException("The current VoiSee executable path could not be resolved.");
        }

        var pipeName = $"VoiSee.VirtualMic.{Environment.ProcessId}.{Guid.NewGuid():N}";
        _pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            inBufferSize: 0,
            outBufferSize: 64 * 1024);

        try
        {
            _hostProcess = DetachedProcessLauncher.StartWithExplorerParent(
                executablePath,
                VirtualMicOutputHost.BuildArguments(pipeName));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            _pipe.WaitForConnectionAsync(timeout.Token).GetAwaiter().GetResult();

            _writer = new BinaryWriter(_pipe, Encoding.UTF8, leaveOpen: true);
            VirtualMicOutputHost.WriteHandshake(_writer, _provider.WaveFormat, _deviceId);

            using (var reader = new BinaryReader(_pipe, Encoding.UTF8, leaveOpen: true))
            {
                if (!reader.ReadBoolean())
                {
                    throw new InvalidOperationException("The isolated virtual microphone helper did not initialize its output device.");
                }
            }

            LastError = null;
            _stopRequested.Reset();
            _running = true;
            _pumpThread = new Thread(PumpMain)
            {
                IsBackground = true,
                Name = "VoiSee Isolated Virtual Mic Pump",
                Priority = ThreadPriority.AboveNormal
            };
            _pumpThread.Start();
        }
        catch
        {
            StopCore(forceHostExit: true);
            throw;
        }
    }

    public void Stop()
    {
        StopCore(forceHostExit: true);
    }

    private void PumpMain()
    {
        var format = _provider.WaveFormat;
        var samplesPerBlock = checked(format.SampleRate * format.Channels * BlockMilliseconds / 1000);
        var samples = new float[samplesPerBlock];
        var bytes = new byte[samplesPerBlock * sizeof(float)];
        var stopwatch = Stopwatch.StartNew();
        var nextBlockAt = TimeSpan.Zero;

        try
        {
            while (_running && !_stopRequested.IsSet)
            {
                _provider.Read(samples, 0, samples.Length);
                Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

                var writer = _writer;
                if (writer is null)
                {
                    throw new IOException("The isolated virtual microphone pipe writer is not available.");
                }

                writer.Write(bytes.Length);
                writer.Write(bytes);
                writer.Flush();

                nextBlockAt += TimeSpan.FromMilliseconds(BlockMilliseconds);
                var remaining = nextBlockAt - stopwatch.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    _stopRequested.Wait(remaining);
                }
                else if (remaining < TimeSpan.FromMilliseconds(-100))
                {
                    nextBlockAt = stopwatch.Elapsed;
                }
            }
        }
        catch (Exception ex) when (!_stopRequested.IsSet)
        {
            LastError = ex.Message;
        }
        finally
        {
            _running = false;
        }
    }

    private void StopCore(bool forceHostExit)
    {
        _running = false;
        _stopRequested.Set();

        var pumpThread = _pumpThread;
        var pumpStopped = pumpThread is null || pumpThread == Thread.CurrentThread || pumpThread.Join(500);
        if (!pumpStopped)
        {
            try
            {
                _pipe?.Dispose();
            }
            catch
            {
                // Disposing the pipe is used to unblock a stalled write.
            }

            pumpStopped = pumpThread!.Join(1500);
        }

        if (pumpStopped && _writer is not null)
        {
            try
            {
                _writer.Write(0);
                _writer.Flush();
            }
            catch
            {
                // The helper may already have exited.
            }
        }

        try
        {
            _writer?.Dispose();
        }
        catch
        {
            // Best-effort teardown.
        }

        try
        {
            _pipe?.Dispose();
        }
        catch
        {
            // Best-effort teardown.
        }

        var hostProcess = _hostProcess;
        if (hostProcess is not null)
        {
            try
            {
                if (!hostProcess.HasExited && !hostProcess.WaitForExit(1500) && forceHostExit)
                {
                    hostProcess.Kill(entireProcessTree: true);
                    hostProcess.WaitForExit(1000);
                }
            }
            catch
            {
                // The process can disappear between checks.
            }
            finally
            {
                hostProcess.Dispose();
            }
        }

        _pumpThread = null;
        _writer = null;
        _pipe = null;
        _hostProcess = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopCore(forceHostExit: true);
        _stopRequested.Dispose();
        _disposed = true;
    }
}
