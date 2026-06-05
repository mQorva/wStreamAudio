using wStreamAudio.Core.Models;

namespace wStreamAudio.Core.Abstractions;

public interface ILmsClient
{
    Uri? BaseAddress { get; }
    bool IsConnected { get; }

    Task<LmsConnectionTestResult> TestConnectionAsync(string host, int port, CancellationToken ct = default);
    void Configure(string host, int port);

    Task<IReadOnlyList<PlayerSnapshot>> GetPlayersAsync(CancellationToken ct = default);
    Task SetPowerAsync(string playerId, bool on, CancellationToken ct = default);
    Task SetVolumeAsync(string playerId, int volume, CancellationToken ct = default);
    Task SyncAsync(string masterId, string slaveId, CancellationToken ct = default);
    Task UnsyncAsync(string playerId, CancellationToken ct = default);
    Task PlayUrlAsync(string playerId, string url, CancellationToken ct = default);
    Task PauseAsync(string playerId, CancellationToken ct = default);
    Task StopAsync(string playerId, CancellationToken ct = default);

    /// <summary>Wird gefeuert, wenn der LMS Player-Volume-Änderungen meldet (Subscribe).</summary>
    event EventHandler<PlayerVolumeChangedEventArgs>? PlayerVolumeChanged;
}

public sealed class PlayerVolumeChangedEventArgs(string playerId, int volume) : EventArgs
{
    public string PlayerId { get; } = playerId;
    public int Volume { get; } = volume;
}

public readonly record struct LmsConnectionTestResult(bool Ok, int? StatusCode, string? Error)
{
    public static LmsConnectionTestResult Success(int statusCode) => new(true, statusCode, null);
    public static LmsConnectionTestResult Failure(string error) => new(false, null, error);
}
