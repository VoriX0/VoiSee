using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VoiSe.App;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string ActivateCommand = "activate";
    private readonly Mutex? _mutex;
    private readonly bool _ownsMutex;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listenerTask;
    private bool _disposed;

    private SingleInstanceCoordinator(Mutex? mutex, bool ownsMutex, string pipeName)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
        PipeName = pipeName;
    }

    public bool IsPrimary => _ownsMutex;

    public string PipeName { get; }

    public static SingleInstanceCoordinator Create()
    {
        var suffix = BuildPerUserSessionSuffix();
        var mutexName = $@"Local\VoiSee.SingleInstance.{suffix}";
        var pipeName = $"VoiSee.SingleInstance.{suffix}";
        var mutex = new Mutex(true, mutexName, out var createdNew);
        return new SingleInstanceCoordinator(mutex, createdNew, pipeName);
    }

    public async Task<bool> SignalPrimaryInstanceAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    serverName: ".",
                    pipeName: PipeName,
                    direction: PipeDirection.Out,
                    options: PipeOptions.Asynchronous);
                await client.ConnectAsync(300).ConfigureAwait(false);
                await using var writer = new StreamWriter(client, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: false)
                {
                    AutoFlush = true
                };
                await writer.WriteLineAsync(ActivateCommand).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                await Task.Delay(120).ConfigureAwait(false);
            }
            catch (IOException)
            {
                await Task.Delay(120).ConfigureAwait(false);
            }
        }

        return false;
    }

    public void StartListening(Action activationRequested)
    {
        ArgumentNullException.ThrowIfNull(activationRequested);
        if (!IsPrimary || _listenerTask is not null)
        {
            return;
        }

        _listenerTask = Task.Run(() => ListenLoopAsync(activationRequested, _cancellation.Token));
    }

    private async Task ListenLoopAsync(Action activationRequested, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
                var command = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.Equals(command, ActivateCommand, StringComparison.OrdinalIgnoreCase))
                {
                    activationRequested();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                StartupLog.Write("Single-instance listener error: " + ex.Message);
                try
                {
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static string BuildPerUserSessionSuffix()
    {
        var source = $"{Environment.UserDomainName}\\{Environment.UserName}|{Process.GetCurrentProcess().SessionId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The process is already leaving; the kernel object will be released.
            }
        }

        _mutex?.Dispose();
        _cancellation.Dispose();
    }
}
