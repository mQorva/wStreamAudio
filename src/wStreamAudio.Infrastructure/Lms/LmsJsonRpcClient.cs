using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Core.Models;

namespace wStreamAudio.Infrastructure.Lms;

/// <summary>
/// LMS-JSON-RPC-Client. LMS hört auf POST /jsonrpc.js mit Body
/// {"id":1,"method":"slim.request","params":[playerid,[command,args...]]}.
/// </summary>
public sealed class LmsJsonRpcClient : ILmsClient
{
    private readonly HttpClient _http;
    private readonly ISettingsService _settings;
    private readonly ILogger<LmsJsonRpcClient> _log;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public LmsJsonRpcClient(HttpClient http, ISettingsService settings, ILogger<LmsJsonRpcClient> log)
    {
        _http = http;
        _settings = settings;
        _log = log;
    }

    public Uri? BaseAddress => _http.BaseAddress;
    public bool IsConnected { get; private set; }

    public event EventHandler<PlayerVolumeChangedEventArgs>? PlayerVolumeChanged;

    public void Configure(string host, int port)
    {
        if (!TryBuildBaseUri(host, port, out var uri, out var error))
        {
            throw new ArgumentException($"Ungültige LMS-Adresse: {error}", nameof(host));
        }
        _http.BaseAddress = uri;
    }

    /// <summary>
    /// Stellt sicher, dass <see cref="_http"/> eine BaseAddress hat, die zu den aktuellen
    /// Settings passt. Notwendig, weil <see cref="LmsJsonRpcClient"/> als typed HTTP client
    /// transient registriert ist — jede DI-Auflösung erzeugt eine neue Instanz mit frischem
    /// HttpClient, dessen BaseAddress nicht von früheren <see cref="Configure"/>-Calls gesetzt ist.
    /// </summary>
    private bool EnsureBaseAddress()
    {
        var lms = _settings.Current.Lms;
        if (string.IsNullOrWhiteSpace(lms.Host) || lms.Port <= 0) return false;

        if (!TryBuildBaseUri(lms.Host, lms.Port, out var uri, out _)) return false;

        // Auch bei abweichender Settings-Änderung neu setzen.
        if (_http.BaseAddress is null || _http.BaseAddress != uri)
        {
            _http.BaseAddress = uri;
        }
        return true;
    }

    public async Task<LmsConnectionTestResult> TestConnectionAsync(string host, int port, CancellationToken ct = default)
    {
        if (!TryBuildBaseUri(host, port, out var baseUri, out var error))
        {
            IsConnected = false;
            return LmsConnectionTestResult.Failure(error!);
        }

        // Stufe 1: TCP-Connect. Sagt uns, ob Adresse + Port überhaupt reagieren.
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            using var tcpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tcpCts.CancelAfter(TimeSpan.FromSeconds(3));
            await tcp.ConnectAsync(baseUri!.Host, baseUri.Port, tcpCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            IsConnected = false;
            return LmsConnectionTestResult.Failure(
                $"Port {baseUri!.Port} auf {baseUri.Host} antwortet nicht (Timeout). Firewall, falsche IP oder LMS läuft nicht?");
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            IsConnected = false;
            return LmsConnectionTestResult.Failure(
                $"Port {baseUri!.Port} auf {baseUri.Host} nicht erreichbar: {ex.SocketErrorCode}.");
        }
        catch (Exception ex)
        {
            IsConnected = false;
            return LmsConnectionTestResult.Failure($"TCP-Connect fehlgeschlagen: {ex.Message}");
        }

        // Stufe 2: HTTP-POST auf /jsonrpc.js mit minimalem JSON-RPC-Payload.
        // LMS antwortet auf GET /jsonrpc.js mit „leerer Antwort" und schließt — nur POST ist gültig.
        // Der Web-UI-Pfad GET / würde zwar 200 liefern, beweist aber nicht, dass es ein LMS ist.
        try
        {
            using var probe = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(5) };
            var payload = new StringContent(
                "{\"id\":1,\"method\":\"slim.request\",\"params\":[\"\",[\"version\",\"?\"]]}",
                System.Text.Encoding.UTF8, "application/json");
            using var resp = await probe.PostAsync("jsonrpc.js", payload, ct).ConfigureAwait(false);
            var status = (int)resp.StatusCode;
            if (resp.IsSuccessStatusCode)
            {
                IsConnected = true;
                return LmsConnectionTestResult.Success(status);
            }
            IsConnected = false;
            return LmsConnectionTestResult.Failure(
                $"LMS antwortet auf POST /jsonrpc.js mit HTTP {status}. Erwartet wäre 200 — läuft auf dem Port wirklich ein Logitech Media Server?");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            IsConnected = false;
            return LmsConnectionTestResult.Failure(
                $"HTTP-POST auf {baseUri}jsonrpc.js bricht mit Timeout ab. Port reagiert, aber kein HTTP-Server dahinter?");
        }
        catch (HttpRequestException ex)
        {
            _log.LogDebug(ex, "LMS-Verbindungstest fehlgeschlagen für {Uri}", baseUri);
            IsConnected = false;
            var inner = ex.InnerException?.Message;
            return LmsConnectionTestResult.Failure(
                $"HTTP-Fehler beim POST /jsonrpc.js: {(string.IsNullOrEmpty(inner) ? ex.Message : inner!)}");
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "LMS-Verbindungstest fehlgeschlagen für {Uri}", baseUri);
            IsConnected = false;
            return LmsConnectionTestResult.Failure(ex.Message);
        }
    }

    private static bool TryBuildBaseUri(string host, int port, out Uri? uri, out string? error)
    {
        uri = null;
        error = null;
        if (string.IsNullOrWhiteSpace(host))
        {
            error = "Host ist leer.";
            return false;
        }
        if (port <= 0 || port > 65535)
        {
            error = $"Ungültiger Port: {port}.";
            return false;
        }

        // Toleriere Eingaben wie "http://192.168.1.10", "192.168.1.10/", "http://lms.local:9000".
        var raw = host.Trim();
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) raw = raw[7..];
        else if (raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) raw = raw[8..];
        var slash = raw.IndexOf('/');
        if (slash >= 0) raw = raw[..slash];
        var colon = raw.IndexOf(':');
        if (colon >= 0) raw = raw[..colon];
        raw = raw.Trim();
        if (raw.Length == 0)
        {
            error = "Host ist nach Bereinigung leer.";
            return false;
        }

        if (!Uri.TryCreate($"http://{raw}:{port}/", UriKind.Absolute, out uri))
        {
            error = $"Adresse konnte nicht geparst werden: http://{raw}:{port}/";
            return false;
        }
        return true;
    }

    public async Task<IReadOnlyList<PlayerSnapshot>> GetPlayersAsync(CancellationToken ct = default)
    {
        // serverstatus liefert players[] mit playerid, name, connected, isplaying, power, volume, sync_master.
        var resp = await SendAsync(string.Empty, ["serverstatus", "0", "999"], ct).ConfigureAwait(false);
        var list = new List<PlayerSnapshot>();

        if (resp.TryGetProperty("result", out var result) &&
            result.TryGetProperty("players_loop", out var loop) &&
            loop.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in loop.EnumerateArray())
            {
                var id = p.TryGetProperty("playerid", out var pid) ? pid.GetString() ?? string.Empty : string.Empty;
                var name = p.TryGetProperty("name", out var nm) ? nm.GetString() ?? id : id;
                var connected = ReadInt(p, "connected") == 1;
                var playing = ReadInt(p, "isplaying") == 1;
                var powered = ReadInt(p, "power") == 1;
                var volume = ReadInt(p, "volume");
                string? syncMaster = p.TryGetProperty("sync_master", out var sm) ? sm.GetString() : null;
                // LMS liefert "ip" als "192.168.1.42:36918" — nur die Adresse vor dem Port behalten.
                string? ip = null;
                if (p.TryGetProperty("ip", out var ipEl) && ipEl.GetString() is { } rawIp && rawIp.Length > 0)
                {
                    var colon = rawIp.IndexOf(':');
                    ip = colon > 0 ? rawIp[..colon] : rawIp;
                }
                var kind = ResolveKind(name, p);
                list.Add(new PlayerSnapshot
                {
                    Id = id,
                    Name = name,
                    Kind = kind,
                    IsConnected = connected,
                    IsPowered = powered,
                    IsPlaying = playing,
                    Volume = Math.Clamp(volume, 0, 100),
                    SyncMaster = syncMaster,
                    Ip = ip,
                });
            }
        }

        return list;
    }

    private static int ReadInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetInt32(),
            JsonValueKind.String => int.TryParse(v.GetString(), out var n) ? n : 0,
            _ => 0
        };
    }

    private static PlayerKind ResolveKind(string name, JsonElement player)
    {
        // Manche LMS-Player tragen "[AirPlay]" / "UPnP" / "DLNA" im Namen, wenn sie von einer
        // externen Brücke (z.B. shairport-sync) gemeldet werden — die Kategorisierung erlaubt
        // den UIs, sie optisch von echten Squeeze-Playern zu unterscheiden.
        if (name.Contains("AirPlay", StringComparison.OrdinalIgnoreCase)) return PlayerKind.AirPlayBridge;
        if (name.Contains("UPnP", StringComparison.OrdinalIgnoreCase)) return PlayerKind.UpnpBridge;
        if (name.Contains("DLNA", StringComparison.OrdinalIgnoreCase)) return PlayerKind.UpnpBridge;
        return PlayerKind.Squeeze;
    }

    public Task SetPowerAsync(string playerId, bool on, CancellationToken ct = default)
        => SendNoResultAsync(playerId, ["power", on ? "1" : "0"], ct);

    public Task SetVolumeAsync(string playerId, int volume, CancellationToken ct = default)
        => SendNoResultAsync(playerId, ["mixer", "volume", Math.Clamp(volume, 0, 100).ToString()], ct);

    public Task SyncAsync(string masterId, string slaveId, CancellationToken ct = default)
        => SendNoResultAsync(slaveId, ["sync", masterId], ct);

    public Task UnsyncAsync(string playerId, CancellationToken ct = default)
        => SendNoResultAsync(playerId, ["sync", "-"], ct);

    public Task PlayUrlAsync(string playerId, string url, CancellationToken ct = default)
        => SendNoResultAsync(playerId, ["playlist", "play", url], ct);

    public Task PauseAsync(string playerId, CancellationToken ct = default)
        => SendNoResultAsync(playerId, ["pause"], ct);

    public Task StopAsync(string playerId, CancellationToken ct = default)
        => SendNoResultAsync(playerId, ["stop"], ct);

    private async Task SendNoResultAsync(string playerId, IReadOnlyList<string> command, CancellationToken ct)
    {
        try
        {
            await SendAsync(playerId, command, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "LMS-Command fehlgeschlagen: {Cmd}", string.Join(' ', command));
        }
    }

    private async Task<JsonElement> SendAsync(string playerId, IReadOnlyList<string> command, CancellationToken ct)
    {
        if (!EnsureBaseAddress())
            throw new InvalidOperationException("LMS-Client nicht konfiguriert (Host/Port in den Settings prüfen).");

        var payload = new
        {
            id = 1,
            method = "slim.request",
            @params = new object[] { playerId ?? string.Empty, command.Cast<object>().ToArray() }
        };

        using var resp = await _http.PostAsJsonAsync("jsonrpc.js", payload, JsonOpts, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, default, ct).ConfigureAwait(false);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Hilfsmethode zum Auslösen des Volume-Changed-Events (vom externen Subscriber/Polling-Loop).
    /// </summary>
    internal void RaiseVolumeChanged(string playerId, int volume)
        => PlayerVolumeChanged?.Invoke(this, new PlayerVolumeChangedEventArgs(playerId, volume));
}
