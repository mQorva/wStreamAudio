using Microsoft.Extensions.Logging;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Core.Models;
using wStreamAudio.Core.Settings;

namespace wStreamAudio.Services;

/// <summary>
/// Verkabelt Capture → StreamServer → LMS-Player. Orchestriert Start/Stop des
/// FLAC/PCM-Streams und kümmert sich um Sync-Gruppen.
/// </summary>
public sealed class StreamPipelineCoordinator : IAsyncDisposable
{
    private readonly IAudioCapture _capture;
    private readonly IStreamServer _server;
    private readonly ILmsClient _lms;
    private readonly IVolumeService _volume;
    private readonly IDlnaService _dlna;
    private readonly IAirPlaySender _airPlay;
    private readonly ISettingsService _settings;
    private readonly IFirewallService _firewall;
    private readonly ILogger<StreamPipelineCoordinator> _log;
    private bool _firewallEnsuredThisSession;
    // Serialisiert Start/Stop — sonst können Auto-Resume (Threadpool) und Play-Klick (UI)
    // parallel StartAsync feuern, beide passieren das IsStreaming-Gate und kollidieren auf
    // dem MMDevice (COM-Apartment-Marshaling schlägt fehl).
    private readonly SemaphoreSlim _gate = new(1, 1);

    public StreamPipelineCoordinator(
        IAudioCapture capture,
        IStreamServer server,
        ILmsClient lms,
        IVolumeService volume,
        IDlnaService dlna,
        IAirPlaySender airPlay,
        ISettingsService settings,
        IFirewallService firewall,
        ILogger<StreamPipelineCoordinator> log)
    {
        _capture = capture;
        _server = server;
        _lms = lms;
        _volume = volume;
        _dlna = dlna;
        _airPlay = airPlay;
        _settings = settings;
        _firewall = firewall;
        _log = log;

        _capture.Frame += OnAudioFrame;
    }

    public bool IsStreaming { get; private set; }
    public Uri? StreamUrl => _server.StreamUrl;

    public event EventHandler<bool>? StreamingChanged;

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsStreaming) return;

            var settings = _settings.Current;
            var profile = ResolveActiveProfile(settings) ?? CreateDefaultProfile(settings);

            // Firewall-Regel pro App-Session nur einmal sicherstellen — sonst löst jeder Stream-Start
            // den UAC/Netzwerk-Dialog aus.
            if (settings.Streaming.SetFirewallRule && !_firewallEnsuredThisSession)
            {
                _firewallEnsuredThisSession = true;
                try
                {
                    await _firewall.EnsureInboundRuleAsync("wStreamAudio", settings.Streaming.HttpPort, ct).ConfigureAwait(false);
                }
                catch (Exception ex) { _log.LogWarning(ex, "Firewall-Regel konnte nicht gesetzt werden"); }
            }

            await _server.StartAsync(settings.Streaming.HttpPort, ct).ConfigureAwait(false);
            await _capture.StartAsync(profile, ct).ConfigureAwait(false);

            // Format direkt vom Capture an den Server reichen, damit HTTP-Header sofort ausgehen,
            // auch wenn die Audio-Engine grad keine Frames liefert (z.B. Stille auf SPDIF).
            if (_capture.SampleRate > 0)
            {
                _server.SetFormat(_capture.SampleRate, _capture.Channels);
            }
            Infrastructure.Streaming.StreamLog.Write(
                $"pipeline started — capture {_capture.SampleRate}Hz/{_capture.Channels}ch, profile='{profile.Name}'");

            var url = _server.StreamUrl?.ToString();
            if (!string.IsNullOrEmpty(url))
            {
                await _volume.ApplyAllAsync(ct).ConfigureAwait(false);
                await ApplyToActiveSyncGroupAsync(url, ct).ConfigureAwait(false);
                await StartActiveDlnaRenderersAsync(url, ct).ConfigureAwait(false);
            }
            await StartActiveAirPlayDevicesAsync(ct).ConfigureAwait(false);

            IsStreaming = true;
            StreamingChanged?.Invoke(this, true);
            _log.LogInformation("Streaming gestartet — URL {Url}", url);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!IsStreaming) return;

            try
            {
                foreach (var p in _settings.Current.Players.Where(p => p.IsEnabled && p.InActiveSyncGroup && !p.IsLocalDevice))
                {
                    await _lms.StopAsync(p.Id, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { _log.LogDebug(ex, "Stop-Notify an LMS fehlgeschlagen"); }

            await StopAllDlnaRenderersAsync(ct).ConfigureAwait(false);
            await StopAllAirPlayDevicesAsync(ct).ConfigureAwait(false);

            await _capture.StopAsync(ct).ConfigureAwait(false);
            await _server.StopAsync(ct).ConfigureAwait(false);

            IsStreaming = false;
            StreamingChanged?.Invoke(this, false);
            _log.LogInformation("Streaming gestoppt");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PauseAsync(CancellationToken ct = default)
    {
        var master = ResolveSyncMaster();
        if (master is null) return;
        await _lms.PauseAsync(master.Id, ct).ConfigureAwait(false);
    }

    private async Task ApplyToActiveSyncGroupAsync(string url, CancellationToken ct)
    {
        var settings = _settings.Current;
        // Filter: Player muss IM Mini sichtbar sein (IsEnabled) UND der Lautsprecher-Toggle
        // (InActiveSyncGroup) muss an sein. Damit gilt: aktiv-CheckBox = Sichtbarkeit,
        // Lautsprecher-Icon = „Stream genau auf diesem Gerät".
        var members = settings.Players
            .Where(p => p.IsEnabled && p.InActiveSyncGroup && !p.IsLocalDevice)
            .ToList();
        if (members.Count == 0) return;

        var master = members[0];
        for (int i = 1; i < members.Count; i++)
        {
            try { await _lms.SyncAsync(master.Id, members[i].Id, ct).ConfigureAwait(false); }
            catch (Exception ex) { _log.LogDebug(ex, "Sync zu Master fehlgeschlagen für {Id}", members[i].Id); }
        }

        try { await _lms.PlayUrlAsync(master.Id, url, ct).ConfigureAwait(false); }
        catch (Exception ex) { _log.LogWarning(ex, "play URL am Master fehlgeschlagen"); }
    }

    /// <summary>Schickt PlayUrl an alle „aktiven" DLNA-Renderer (IsEnabled = true). Wird
    /// von StartAsync aufgerufen — egal ob Stream aus Streaming-Seite oder Mini-Fenster
    /// gestartet wurde. Damit kennt die Pipeline genau eine Quelle: den IsEnabled-Flag.</summary>
    private async Task StartActiveDlnaRenderersAsync(string url, CancellationToken ct)
    {
        var prebuffer = _settings.Current.Services.DlnaBufferMs;
        foreach (var r in _settings.Current.DlnaRenderers.Where(d => d.IsEnabled && d.IsPlayActive))
        {
            var renderer = BuildRendererFrom(r);
            if (renderer is null) continue;
            try { await _dlna.PlayUrlAsync(renderer, url, prebufferMs: prebuffer, ct: ct).ConfigureAwait(false); }
            catch (Exception ex) { _log.LogWarning(ex, "DLNA PlayUrl fehlgeschlagen für {Udn}", r.Udn); }
        }
    }

    private async Task StopAllDlnaRenderersAsync(CancellationToken ct)
    {
        foreach (var r in _settings.Current.DlnaRenderers)
        {
            var renderer = BuildRendererFrom(r);
            if (renderer is null) continue;
            try { await _dlna.StopAsync(renderer, ct).ConfigureAwait(false); }
            catch (Exception ex) { _log.LogDebug(ex, "DLNA Stop fehlgeschlagen für {Udn}", r.Udn); }
        }
    }

    private static DlnaRenderer? BuildRendererFrom(PersistedDlnaRenderer p)
    {
        if (!Uri.TryCreate(p.AvTransportControlUrl, UriKind.Absolute, out var avt)) return null;
        Uri? rc = null;
        if (!string.IsNullOrEmpty(p.RenderingControlUrl))
            Uri.TryCreate(p.RenderingControlUrl, UriKind.Absolute, out rc);
        return new DlnaRenderer
        {
            Udn = p.Udn,
            FriendlyName = p.FriendlyName,
            AvTransportControlUrl = avt,
            RenderingControlUrl = rc,
            Manufacturer = p.Manufacturer,
            ModelName = p.ModelName,
        };
    }

    private PersistedPlayer? ResolveSyncMaster()
        => _settings.Current.Players.FirstOrDefault(p => p.InActiveSyncGroup);

    private void OnAudioFrame(object? sender, AudioFrameEventArgs e)
    {
        var span = e.Buffer.AsSpan(0, e.Length);
        _server.PushPcmFrame(span, _capture.SampleRate, _capture.Channels);
        // AirPlay-Sessions verlangen Stereo-16-bit-PCM. Capture liefert das laut Profile.
        _airPlay.PushPcmFrame(span, _capture.SampleRate);
    }

    /// <summary>Baut Sessions zu allen aktivierten AirPlay-Empfängern auf.</summary>
    private async Task StartActiveAirPlayDevicesAsync(CancellationToken ct)
    {
        foreach (var a in _settings.Current.AirPlayDevices.Where(x => x.IsEnabled && x.IsPlayActive))
        {
            var dev = BuildAirPlayDeviceFrom(a);
            try
            {
                await _airPlay.PlayAsync(dev, ct).ConfigureAwait(false);
                await _airPlay.SetVolumeAsync(dev, a.VolumePercent, ct).ConfigureAwait(false);
            }
            catch (Exception ex) { _log.LogWarning(ex, "AirPlay Play fehlgeschlagen für {Host}", a.Host); }
        }
    }

    private async Task StopAllAirPlayDevicesAsync(CancellationToken ct)
    {
        foreach (var a in _settings.Current.AirPlayDevices)
        {
            var dev = BuildAirPlayDeviceFrom(a);
            try { await _airPlay.StopAsync(dev, ct).ConfigureAwait(false); }
            catch (Exception ex) { _log.LogDebug(ex, "AirPlay Stop fehlgeschlagen für {Host}", a.Host); }
        }
    }

    private static AirPlayDevice BuildAirPlayDeviceFrom(PersistedAirPlayDevice p)
        => new()
        {
            Id = p.Id,
            FriendlyName = string.IsNullOrEmpty(p.CustomName) ? p.FriendlyName : p.CustomName!,
            Host = p.Host,
            Port = p.Port,
            SupportsAirPlay1 = true,
            SupportsAirPlay2 = p.SupportsAirPlay2,
            Model = p.Model,
            Manufacturer = p.Manufacturer,
        };

    private CaptureProfile? ResolveActiveProfile(SettingsModel settings)
    {
        if (string.IsNullOrEmpty(settings.ActiveCaptureProfileId)) return null;
        return settings.CaptureProfiles.FirstOrDefault(p => p.Id == settings.ActiveCaptureProfileId);
    }

    private static CaptureProfile CreateDefaultProfile(SettingsModel settings)
    {
        var profile = new CaptureProfile { Name = "Default Speakers", FollowDefaultEndpoint = true };
        settings.CaptureProfiles.Add(profile);
        settings.ActiveCaptureProfileId = profile.Id;
        return profile;
    }

    public async ValueTask DisposeAsync()
    {
        _capture.Frame -= OnAudioFrame;
        try { await StopAsync().ConfigureAwait(false); } catch { /* ignore */ }
    }
}
