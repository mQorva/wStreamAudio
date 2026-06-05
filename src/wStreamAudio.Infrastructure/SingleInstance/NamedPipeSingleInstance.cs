using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging;
using wStreamAudio.Core.Abstractions;

namespace wStreamAudio.Infrastructure.SingleInstance;

/// <summary>
/// Single-Instance-Manager auf Mutex-Basis. Zweitstart kann der laufenden
/// Instanz über eine Named Pipe einen Befehl signalisieren (z.B. "show-popup").
/// </summary>
public sealed class NamedPipeSingleInstance : ISingleInstance
{
    private readonly IAppProfile _profile;
    private readonly ILogger<NamedPipeSingleInstance> _log;
    private readonly Mutex _mutex;
    private CancellationTokenSource? _serverCts;

    public NamedPipeSingleInstance(IAppProfile profile, ILogger<NamedPipeSingleInstance> log)
    {
        _profile = profile;
        _log = log;
        _mutex = new Mutex(initiallyOwned: true, _profile.MutexName, out var first);
        IsFirstInstance = first;
    }

    public bool IsFirstInstance { get; }

    public event EventHandler<string>? CommandReceived;

    public Task StartListeningAsync(CancellationToken ct = default)
    {
        if (!IsFirstInstance) return Task.CompletedTask;

        _serverCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = ListenLoopAsync(_serverCts.Token);
        return Task.CompletedTask;
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _profile.SingleInstancePipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(line))
                {
                    CommandReceived?.Invoke(this, line);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Pipe-Loop-Fehler — neu starten");
                try { await Task.Delay(250, ct).ConfigureAwait(false); } catch { break; }
            }
        }
    }

    public async Task<bool> SignalRunningInstanceAsync(string command, CancellationToken ct = default)
    {
        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                _profile.SingleInstancePipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            await client.ConnectAsync(1500, ct).ConfigureAwait(false);
            await using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            await writer.WriteLineAsync(command).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Konnte laufende Instanz nicht signalisieren");
            return false;
        }
    }

    public void Dispose()
    {
        try { _serverCts?.Cancel(); } catch { /* ignore */ }
        _serverCts?.Dispose();
        if (IsFirstInstance)
        {
            try { _mutex.ReleaseMutex(); } catch { /* ignore */ }
        }
        _mutex.Dispose();
    }
}
