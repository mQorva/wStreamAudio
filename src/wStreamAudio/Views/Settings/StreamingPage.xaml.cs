using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Core.Models;
using wStreamAudio.Core.Settings;
using wStreamAudio.Infrastructure.Logging;
using wStreamAudio.Services;

namespace wStreamAudio.Views.Settings;

public sealed partial class StreamingPage : Page
{
    private readonly ISettingsService _settings;
    private readonly ILmsClient _lms;
    private readonly IDlnaService _dlna;
    private readonly IAudioCapture _capture;
    private readonly IVolumeService _volume;
    private readonly StreamPipelineCoordinator _pipeline;
    private readonly IPlayerStateBus _bus;
    private readonly IAirPlayDiscovery _airPlay;
    private readonly IAirPlaySender _airPlaySender;
    private readonly ObservableCollection<PlayerSettingsItem> _items = new();
    private readonly ObservableCollection<DlnaRendererItem> _dlnaItems = new();
    private readonly ObservableCollection<AirPlayRendererItem> _airPlayItems = new();
    private bool _suppress;

    public StreamingPage(ISettingsService settings, ILmsClient lms, IDlnaService dlna, IAudioCapture capture, IVolumeService volume, StreamPipelineCoordinator pipeline, IPlayerStateBus bus, IAirPlayDiscovery airPlay, IAirPlaySender airPlaySender)
    {
        _settings = settings;
        _lms = lms;
        _dlna = dlna;
        _capture = capture;
        _volume = volume;
        _pipeline = pipeline;
        _bus = bus;
        _airPlay = airPlay;
        _airPlaySender = airPlaySender;
        InitializeComponent();
        Load();
        _ = RefreshFromLmsAsync();

        _capture.LevelChanged += OnLevelChanged;
        _pipeline.StreamingChanged += OnStreamingChanged;
        _bus.PlayerChanged += OnBusPlayerChanged;
        _settings.Saved += OnSettingsSaved;
        Unloaded += OnUnloaded;
        UpdateLevelStatus();
        ApplyServiceVisibility();

        // Auto-Discovery beim Öffnen — wenn der jeweilige Dienst aktiv UND auf Auto steht.
        var svc = _settings.Current.Services;
        if (svc.Dlna && svc.DlnaAutoDiscover) _ = TriggerDlnaDiscoveryAsync();
        if (svc.AirPlay && svc.AirPlayAutoDiscover) _ = TriggerAirPlayDiscoveryAsync();
    }

    private Task TriggerDlnaDiscoveryAsync()
    {
        try { DlnaDiscover_Click(this, new RoutedEventArgs()); }
        catch (Exception ex) { AppLogger.Write(ex); }
        return Task.CompletedTask;
    }

    private Task TriggerAirPlayDiscoveryAsync()
    {
        try { AirPlayDiscover_Click(this, new RoutedEventArgs()); }
        catch (Exception ex) { AppLogger.Write(ex); }
        return Task.CompletedTask;
    }

    /// <summary>Sektionen ein-/ausblenden je nach Service-Toggles auf der Dienste-Seite.</summary>
    private void ApplyServiceVisibility()
    {
        var s = _settings.Current.Services;
        var sb = s.SqueezeBox ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        var dl = s.Dlna ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        var ap = s.AirPlay ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        SqueezeBoxHeaderGrid.Visibility = sb;
        PlayersList.Visibility = sb;
        DlnaHeaderGrid.Visibility = dl;
        DlnaList.Visibility = dl;
        AirPlayHeaderGrid.Visibility = ap;
        AirPlayList.Visibility = ap;
    }

    private void OnSettingsSaved(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(ApplyServiceVisibility);

    // (Stream-URL/HTTP-Port/Firewall sind in die Dienste-Seite gewandert.)

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _capture.LevelChanged -= OnLevelChanged;
        _pipeline.StreamingChanged -= OnStreamingChanged;
        _bus.PlayerChanged -= OnBusPlayerChanged;
        _settings.Saved -= OnSettingsSaved;
    }

    /// <summary>
    /// Wenn Volume/Power irgendwo anders geändert wird (Tray, Mini-Fenster), live übernehmen.
    /// </summary>
    private void OnBusPlayerChanged(object? sender, PlayerChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _suppress = true;
            try
            {
                // DLNA-/AirPlay-IDs sind prefix-getaggt — anderes Datenset.
                if (e.PlayerId.StartsWith("dlna:", StringComparison.Ordinal))
                {
                    var udn = e.PlayerId["dlna:".Length..];
                    var d = _dlnaItems.FirstOrDefault(x => x.Udn == udn);
                    if (d is not null && e.Kind == PlayerChangeKind.Power && e.Powered is bool pwd)
                        d.InSyncGroup = pwd;
                    return;
                }
                if (e.PlayerId.StartsWith("airplay:", StringComparison.Ordinal))
                {
                    var id = e.PlayerId["airplay:".Length..];
                    var a = _airPlayItems.FirstOrDefault(x => x.Id == id);
                    if (a is not null && e.Kind == PlayerChangeKind.Power && e.Powered is bool pwa)
                        a.InSyncGroup = pwa;
                    return;
                }

                var item = _items.FirstOrDefault(p => p.Id == e.PlayerId);
                if (item is null) return;
                if (e.Volume is int v) item.TrimPercent = v;
                if (e.Kind == PlayerChangeKind.Enabled && e.Enabled is bool en)
                    item.IsEnabled = en;
                if (e.Kind == PlayerChangeKind.Power && e.Powered is bool pw)
                    item.InSyncGroup = pw;
            }
            finally { _suppress = false; }
        });
    }

    private void OnLevelChanged(object? sender, AudioLevelEventArgs e)
    {
        // Wenn der Stream gestoppt wurde, dürfen noch in der Pipeline puffernde
        // Level-Events die Anzeige NICHT mehr aufwecken — sonst „laufen" die Balken
        // weiter, obwohl der User pausiert hat.
        if (!_pipeline.IsStreaming) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_pipeline.IsStreaming) return; // Race-Re-Check auf dem UI-Thread
            LevelLeftBar.Value = e.PeakLeft;
            LevelRightBar.Value = e.PeakRight;
            LevelStatusText.Text = "Audio fließt — letzter Spitzenpegel: "
                + $"L {ToDb(e.PeakLeft)}, R {ToDb(e.PeakRight)}";
        });
    }

    private void OnStreamingChanged(object? sender, bool isStreaming)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateLevelStatus();
            UpdateStreamToggleUi();
        });
    }

    private void UpdateLevelStatus()
    {
        if (_pipeline.IsStreaming)
        {
            LevelStatusText.Text = "Stream läuft. Wenn der Balken still bleibt, kommt vom gewählten Endpoint nichts an.";
        }
        else
        {
            LevelLeftBar.Value = 0;
            LevelRightBar.Value = 0;
            LevelStatusText.Text = "Stream nicht aktiv.";
        }
    }

    private void UpdateStreamToggleUi()
    {
        if (_pipeline.IsStreaming)
        {
            StreamToggleIcon.Symbol = Symbol.Stop;
            StreamToggleText.Text = "anhalten";
        }
        else
        {
            StreamToggleIcon.Symbol = Symbol.Play;
            StreamToggleText.Text = "abspielen";
        }
    }

    private async void StreamToggleButton_Click(object sender, RoutedEventArgs e)
    {
        StreamToggleButton.IsEnabled = false;
        try
        {
            // Start/Stop kümmert sich um Squeeze (Sync-Gruppe) UND DLNA-Renderer.
            // Egal ob Klick von hier oder Mini-Fenster — die Pipeline ist die einzige Stelle,
            // die Capture, Squeeze-Sync und DLNA gemeinsam orchestriert.
            if (_pipeline.IsStreaming) await _pipeline.StopAsync().ConfigureAwait(true);
            else await _pipeline.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Stream-Befehl fehlgeschlagen: {ex.Message}", "Stream");
        }
        finally
        {
            StreamToggleButton.IsEnabled = true;
            UpdateStreamToggleUi();
        }
    }

    /// <summary>Sendet PlayUrl an alle „aktiven" DLNA-Renderer (IsEnabled = true).
    /// Wird vom globalen Stream-Start aufgerufen, damit DLNA mit anläuft. LMS-Player
    /// kümmert sich der Pipeline-StartAsync selbst über die Sync-Gruppe.</summary>
    private async Task StartAllActiveDlnaAsync()
    {
        var url = _pipeline.StreamUrl?.ToString();
        if (string.IsNullOrEmpty(url)) return;
        var prebuffer = _settings.Current.Services.DlnaBufferMs;
        foreach (var r in _settings.Current.DlnaRenderers.Where(r => r.IsEnabled))
        {
            var renderer = BuildRendererFrom(r);
            if (renderer is null) continue;
            try { await _dlna.PlayUrlAsync(renderer, url, prebufferMs: prebuffer).ConfigureAwait(true); }
            catch (Exception ex) { AppLogger.Write(ex); }
        }
        foreach (var item in _dlnaItems.Where(d => d.IsEnabled)) item.IsPlaying = true;
    }

    private async Task StopAllActiveDlnaAsync()
    {
        foreach (var r in _settings.Current.DlnaRenderers)
        {
            var renderer = BuildRendererFrom(r);
            if (renderer is null) continue;
            try { await _dlna.StopAsync(renderer).ConfigureAwait(true); }
            catch (Exception ex) { AppLogger.Write(ex); }
        }
        foreach (var item in _dlnaItems) item.IsPlaying = false;
    }

    private static string ToDb(float linear)
    {
        if (linear <= 0.0001f) return "−∞ dB";
        var db = 20.0 * Math.Log10(linear);
        return $"{db:F1} dB";
    }

    private void Load()
    {
        _suppress = true;

        RebuildPlayerList();
        PlayersList.ItemsSource = _items;

        RebuildDlnaList();
        DlnaList.ItemsSource = _dlnaItems;
        RebuildAirPlayList();
        AirPlayList.ItemsSource = _airPlayItems;
        UpdateAirPlayHint();
        UpdateStreamToggleUi();
        _suppress = false;
    }

    private void UpdateAirPlayHint()
    {
        AirPlayHeader.Text = "AirPlay-Empfänger";
        AirPlayHint.Text = "(es wird nur AirPlay 1 unterstützt)";
    }

    private async void AirPlayDiscover_Click(object sender, RoutedEventArgs e)
    {
        AirPlayDiscoverButton.IsEnabled = false;
        AirPlayHint.Text = "Suche AirPlay-Empfänger im LAN (ca. 6 Sek.) …";
        AppLogger.WriteMessage("AirPlay-Discovery: gestartet");
        try
        {
            var devices = await _airPlay.DiscoverAsync().ConfigureAwait(true);
            AppLogger.WriteMessage($"AirPlay-Discovery: {devices.Count} Geräte gefunden");
            foreach (var d in devices)
            {
                AppLogger.WriteMessage(
                    $"  → '{d.FriendlyName}' @ {d.Host}:{d.Port}  AP2={d.SupportsAirPlay2}  Model={d.Model ?? "-"}  Mfg={d.Manufacturer ?? "-"}");
            }

            // Gefundene Geräte in die persistierte Liste mergen (analog DLNA), damit die
            // Pipeline beim Start ohne erneute Discovery die Geräte kennt.
            MergeAirPlaySnapshot(devices);
            _settings.NotifyChanged();
            _suppress = true;
            RebuildAirPlayList();
            _suppress = false;
            if (_airPlayItems.Count == 0)
            {
                AirPlayHint.Text =
                    "Keine AirPlay-Empfänger gefunden. Mögliche Ursachen: " +
                    "(a) shairport-sync auf piCorePlayer/Pi nicht aktiviert, " +
                    "(b) Windows-Firewall blockiert mDNS/UDP-5353, " +
                    "(c) Geräte sind in einem anderen Subnetz.";
            }
            else
            {
                UpdateAirPlayHint();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Write(ex);
            AirPlayHint.Text = $"Fehler bei der Suche: {ex.Message}";
        }
        finally
        {
            AirPlayDiscoverButton.IsEnabled = true;
        }
    }

    private static string BuildAirPlayStatus(wStreamAudio.Core.Models.AirPlayDevice d)
    {
        var parts = new List<string> { d.Host };
        if (!string.IsNullOrEmpty(d.Manufacturer)) parts.Add(d.Manufacturer!);
        if (!string.IsNullOrEmpty(d.Model)) parts.Add(d.Model!);
        return string.Join(" · ", parts);
    }

    private async void AirPlayPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        await ShowMessageAsync(
            "AirPlay-Senden ist in Vorbereitung. Discovery, Liste und Konfiguration sind schon da — der eigentliche Stream-Pfad folgt in einer der nächsten Versionen.",
            "AirPlay");
    }

    private void AirPlayForget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var item = _airPlayItems.FirstOrDefault(x => x.Id == id);
        if (item is not null) _airPlayItems.Remove(item);
        var entry = _settings.Current.AirPlayDevices.FirstOrDefault(x => x.Id == id);
        if (entry is not null) _settings.Current.AirPlayDevices.Remove(entry);
        _settings.NotifyChanged();
        UpdateAirPlayHint();
    }

    private void MergeAirPlaySnapshot(IReadOnlyList<AirPlayDevice> found)
    {
        var settings = _settings.Current;
        foreach (var d in found)
        {
            // Lokale Bridges rausfiltern.
            if (wStreamAudio.Core.Networking.LocalNetwork.IsLocal(d.Host)) continue;
            var existing = settings.AirPlayDevices.FirstOrDefault(p => p.Id == d.Id);
            if (existing is null)
            {
                var nextOrder = settings.AirPlayDevices.Count == 0 ? 1 : settings.AirPlayDevices.Max(a => a.SortOrder) + 1;
                settings.AirPlayDevices.Add(new PersistedAirPlayDevice
                {
                    Id = d.Id,
                    FriendlyName = d.FriendlyName,
                    Host = d.Host,
                    Port = d.Port,
                    SupportsAirPlay2 = d.SupportsAirPlay2,
                    Model = d.Model,
                    Manufacturer = d.Manufacturer,
                    LastSeenUtc = DateTimeOffset.UtcNow,
                    // Default: AP1-Geräte aktiv, reine AP2-Geräte deaktiviert. Wenn die Auto-
                    // Aktivierung in den Allgemein-Einstellungen aus ist, bleibt alles aus.
                    IsEnabled = d.SupportsAirPlay1 && settings.General.AutoActivateNewDevices,
                    SortOrder = nextOrder,
                });
            }
            else
            {
                existing.FriendlyName = d.FriendlyName;
                existing.Host = d.Host;
                existing.Port = d.Port;
                existing.SupportsAirPlay2 = d.SupportsAirPlay2;
                existing.Model = d.Model;
                existing.Manufacturer = d.Manufacturer;
                existing.LastSeenUtc = DateTimeOffset.UtcNow;
            }
        }
    }

    /// <summary>
    /// Migration: wenn alle Einträge SortOrder == 0 haben (alte Settings), nach
    /// bestehender Reihenfolge mit 1, 2, 3, … durchnummerieren. Einmaliger Vorgang.
    /// Gibt true zurück, wenn Werte geändert wurden — dann sollte NotifyChanged gerufen werden.
    /// </summary>
    private static bool MigrateSortOrder<T>(IList<T> items, Func<T, int> get, Action<T, int> set)
    {
        if (items.Count == 0) return false;
        if (items.Any(i => get(i) != 0)) return false;
        for (int i = 0; i < items.Count; i++) set(items[i], i + 1);
        return true;
    }

    private void RebuildAirPlayList()
    {
        _airPlayItems.Clear();
        var devices = _settings.Current.AirPlayDevices;
        if (MigrateSortOrder(devices, a => a.SortOrder, (a, v) => a.SortOrder = v))
            _settings.NotifyChanged();
        foreach (var p in devices.OrderBy(d => d.SortOrder).ToList())
        {
            // Reine AP2-Geräte können wir noch nicht streamen — als „nicht-aktivierbar" zeigen.
            var canStream = !p.SupportsAirPlay2 || true;
            // canStream-Bestimmung: wenn AP1 unterstützt (= das ist die Annahme, weil Discovery
            // sonst nicht in die persistierte Liste landet), dann ja. Reine AP2-only-Geräte
            // ohne RAOP-Anbindung schließen wir derzeit aus. Da wir das Flag „SupportsAirPlay1"
            // nicht persistieren, leiten wir's per default ab: alle persistierten Geräte gelten
            // als AP1-tauglich, weil Discovery sie sonst nicht aufgenommen hätte.
            string proto = p.SupportsAirPlay2 ? "AirPlay 1+2" : "AirPlay 1";
            var item = new AirPlayRendererItem
            {
                Id = p.Id,
                DisplayName = string.IsNullOrEmpty(p.CustomName) ? p.FriendlyName : p.CustomName!,
                StatusText = BuildAirPlayStatusFromPersisted(p),
                ProtocolText = proto,
                CanStream = true,
                IsEnabled = p.IsEnabled,
                InSyncGroup = p.IsPlayActive,
                VolumePercent = p.VolumePercent,
            };
            item.PropertyChanged += (_, ev) => UpdateAirPlayItem(item, ev.PropertyName);
            _airPlayItems.Add(item);
        }
    }

    private static string BuildAirPlayStatusFromPersisted(PersistedAirPlayDevice p)
    {
        var parts = new List<string> { p.Host };
        if (!string.IsNullOrEmpty(p.Manufacturer)) parts.Add(p.Manufacturer!);
        if (!string.IsNullOrEmpty(p.Model)) parts.Add(p.Model!);
        if (p.LastSeenUtc is { } seen) parts.Add($"zuletzt: {seen.LocalDateTime:dd.MM. HH:mm}");
        return string.Join(" · ", parts);
    }

    private void UpdateAirPlayItem(AirPlayRendererItem item, string? prop)
    {
        if (_suppress) return;
        var entry = _settings.Current.AirPlayDevices.FirstOrDefault(d => d.Id == item.Id);
        if (entry is null) return;
        var syncGroupChanged = entry.IsPlayActive != item.InSyncGroup;
        entry.IsEnabled = item.IsEnabled;
        entry.IsPlayActive = item.InSyncGroup;
        entry.VolumePercent = item.VolumePercent;
        _settings.NotifyChanged();

        var device = BuildAirPlayDeviceFrom(entry);

        if (prop == nameof(AirPlayRendererItem.VolumePercent))
        {
            _ = _airPlaySender.SetVolumeAsync(device, item.VolumePercent);
        }

        if (syncGroupChanged)
        {
            _bus.RaisePlayerChanged(new PlayerChangedEventArgs
            {
                PlayerId = "airplay:" + item.Id,
                Kind = PlayerChangeKind.Power,
                Powered = item.InSyncGroup,
            });
            if (_pipeline.IsStreaming)
            {
                if (item.InSyncGroup) _ = AttachAirPlayToLiveStreamAsync(device, item.VolumePercent);
                else _ = DetachAirPlayFromLiveStreamAsync(device);
            }
        }
    }

    private async Task AttachAirPlayToLiveStreamAsync(AirPlayDevice device, int volume)
    {
        try
        {
            await _airPlaySender.PlayAsync(device).ConfigureAwait(true);
            await _airPlaySender.SetVolumeAsync(device, volume).ConfigureAwait(true);
        }
        catch (Exception ex) { AppLogger.Write(ex); }
    }

    private async Task DetachAirPlayFromLiveStreamAsync(AirPlayDevice device)
    {
        try { await _airPlaySender.StopAsync(device).ConfigureAwait(true); }
        catch (Exception ex) { AppLogger.Write(ex); }
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

    private void RebuildDlnaList()
    {
        _dlnaItems.Clear();
        var renderers = _settings.Current.DlnaRenderers;
        if (MigrateSortOrder(renderers, d => d.SortOrder, (d, v) => d.SortOrder = v))
            _settings.NotifyChanged();
        foreach (var r in renderers.OrderBy(d => d.SortOrder).ToList())
        {
            var item = new DlnaRendererItem
            {
                Udn = r.Udn,
                DisplayName = string.IsNullOrEmpty(r.CustomName) ? r.FriendlyName : r.CustomName!,
                StatusText = BuildDlnaStatus(r),
                IsEnabled = r.IsEnabled,
                InSyncGroup = r.IsPlayActive,
                VolumePercent = r.VolumePercent,
            };
            item.PropertyChanged += (_, ev) => UpdateDlnaItem(item, ev.PropertyName);
            _dlnaItems.Add(item);

            // Aktuelle Lautstärke beim Renderer abfragen,
            // damit der Slider beim Öffnen den realen Stand zeigt — und ein Klick darauf
            // nicht zu einem Lautstärke-Sprung führt.
            var renderer = BuildRendererFrom(r);
            if (renderer is not null) _ = SyncDlnaVolumeAsync(item, renderer);
        }
    }

    private async Task SyncDlnaVolumeAsync(DlnaRendererItem item, DlnaRenderer renderer)
    {
        try
        {
            var current = await _dlna.GetVolumeAsync(renderer).ConfigureAwait(true);
            if (current is null) return;
            var volume = Math.Clamp(current.Value, 0, 100);
            _suppress = true;
            try { item.VolumePercent = volume; }
            finally { _suppress = false; }
            // Persistierten Wert auch aktualisieren, damit beim Neustart der Slider passt.
            var entry = _settings.Current.DlnaRenderers.FirstOrDefault(d => d.Udn == item.Udn);
            if (entry is not null) entry.VolumePercent = volume;
        }
        catch (Exception ex) { AppLogger.Write(ex); }
    }

    private void UpdateDlnaItem(DlnaRendererItem item, string? prop)
    {
        if (_suppress) return;
        var entry = _settings.Current.DlnaRenderers.FirstOrDefault(d => d.Udn == item.Udn);
        if (entry is null) return;
        var syncGroupChanged = entry.IsPlayActive != item.InSyncGroup;
        entry.IsEnabled = item.IsEnabled;
        entry.IsPlayActive = item.InSyncGroup;
        entry.VolumePercent = item.VolumePercent;
        _settings.NotifyChanged();

        // Lautstärke an den Renderer schicken: Sliderwert ist direkte Renderer-Lautstärke.
        if (prop == nameof(DlnaRendererItem.VolumePercent))
        {
            var renderer = BuildRendererFrom(entry);
            if (renderer is not null)
            {
                _ = _dlna.SetVolumeAsync(renderer, Math.Clamp(item.VolumePercent, 0, 100));
            }
        }

        // Lautsprecher-Toggle koppelt den Renderer an die laufende Pipeline UND syncht zum Mini.
        if (syncGroupChanged)
        {
            _bus.RaisePlayerChanged(new PlayerChangedEventArgs
            {
                PlayerId = "dlna:" + item.Udn,
                Kind = PlayerChangeKind.Power,
                Powered = item.InSyncGroup,
            });
            if (_pipeline.IsStreaming)
            {
                if (item.InSyncGroup) _ = AttachDlnaToLiveStreamAsync(item.Udn);
                else _ = DetachDlnaFromLiveStreamAsync(item.Udn);
            }
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

    private static string BuildDlnaStatus(PersistedDlnaRenderer r)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(r.Manufacturer)) parts.Add(r.Manufacturer!);
        if (!string.IsNullOrEmpty(r.ModelName)) parts.Add(r.ModelName!);
        if (r.LastSeenUtc is { } seen) parts.Add($"zuletzt: {seen.LocalDateTime:dd.MM. HH:mm}");
        return parts.Count == 0 ? "DLNA-Renderer" : string.Join(" · ", parts);
    }

    private void RebuildPlayerList()
    {
        _items.Clear();
        var players = _settings.Current.Players;
        if (MigrateSortOrder(players, p => p.SortOrder, (p, v) => p.SortOrder = v))
            _settings.NotifyChanged();
        foreach (var p in players.OrderBy(x => x.SortOrder).ToList())
        {
            // Eigenen Rechner ausblenden — Wiedergabe auf der Audio-Quelle wäre eine Schleife.
            if (p.IsLocalDevice) continue;
            var item = new PlayerSettingsItem
            {
                Id = p.Id,
                DisplayName = p.CustomName ?? p.LastSeenName ?? p.Id,
                StatusText = p.LastSeenUtc is { } seen ? $"zuletzt gesehen: {seen.LocalDateTime:dd.MM.yyyy HH:mm}" : "noch nie online gesehen",
                AppControlsVolume = p.AppControlsVolume,
                TrimPercent = p.TrimPercent,
                IsEnabled = p.IsEnabled,
                InSyncGroup = p.InActiveSyncGroup,
            };
            item.PropertyChanged += (_, ev) => UpdatePlayer(item, ev.PropertyName);
            _items.Add(item);
        }
    }

    private async Task RefreshFromLmsAsync()
    {
        PlayersHint.Text = "Frage Player vom LMS ab …";
        try
        {
            var snapshots = await _lms.GetPlayersAsync().ConfigureAwait(true);
            MergeSnapshots(snapshots);
            _settings.NotifyChanged();
            _suppress = true;
            RebuildPlayerList();
            _suppress = false;

            PlayersHint.Text = snapshots.Count switch
            {
                0 => "LMS antwortet, meldet aber keine Player. Sind die Player am LMS angemeldet?",
                1 => "1 Player vom LMS gemeldet.",
                _ => $"{snapshots.Count} Player vom LMS gemeldet.",
            };
        }
        catch (Exception ex)
        {
            PlayersHint.Text = $"LMS nicht erreichbar — Liste zeigt nur lokal gespeicherte Player. ({ex.Message})";
        }
    }

    private void MergeSnapshots(IReadOnlyList<PlayerSnapshot> snapshots)
    {
        var settings = _settings.Current;
        foreach (var live in snapshots)
        {
            var isLocal = wStreamAudio.Core.Networking.LocalNetwork.IsLocal(live.Ip);
            var existing = settings.Players.FirstOrDefault(p => p.Id == live.Id);
            if (existing is null)
            {
                // Neue Geräte ans Ende — max(SortOrder) + 1.
                var nextOrder = settings.Players.Count == 0 ? 1 : settings.Players.Max(p => p.SortOrder) + 1;
                settings.Players.Add(new PersistedPlayer
                {
                    Id = live.Id,
                    LastSeenName = live.Name,
                    LastSeenUtc = DateTimeOffset.UtcNow,
                    IsLocalDevice = isLocal,
                    SortOrder = nextOrder,
                    IsEnabled = settings.General.AutoActivateNewDevices,
                });
            }
            else
            {
                existing.LastSeenName = live.Name;
                existing.LastSeenUtc = DateTimeOffset.UtcNow;
                existing.IsLocalDevice = isLocal;
            }
        }
    }

    private async void RefreshPlayers_Click(object sender, RoutedEventArgs e)
    {
        var refreshButton = sender as Button;
        if (refreshButton is not null) refreshButton.IsEnabled = false;
        try { await RefreshFromLmsAsync(); }
        finally { if (refreshButton is not null) refreshButton.IsEnabled = true; }
    }

    private async void PlayPauseOnPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string playerId } button) return;
        var item = _items.FirstOrDefault(x => x.Id == playerId);
        if (item is null) return;

        button.IsEnabled = false;
        try
        {
            if (item.IsPlaying)
            {
                await _lms.StopAsync(playerId).ConfigureAwait(true);
                _suppress = true;
                item.IsPlaying = false;
                item.StatusText = "gestoppt";
                _suppress = false;
            }
            else
            {
                if (!_pipeline.IsStreaming)
                    await _pipeline.StartAsync().ConfigureAwait(true);

                var url = _pipeline.StreamUrl?.ToString();
                if (string.IsNullOrEmpty(url))
                {
                    await ShowMessageAsync("Stream-URL ist (noch) nicht verfügbar. Bitte gleich nochmal versuchen.", "abspielen");
                    return;
                }
                await _lms.PlayUrlAsync(playerId, url).ConfigureAwait(true);
                _suppress = true;
                item.IsPlaying = true;
                item.StatusText = "spielt jetzt";
                _suppress = false;
            }
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Befehl fehlgeschlagen: {ex.Message}", item.IsPlaying ? "stoppen" : "abspielen");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    /// <summary>Hängt einen LMS-Player an den laufenden Stream — sendet PlayUrl mit der
    /// aktuellen Stream-URL. Wird vom „aktiv"-Toggle aufgerufen, wenn die Pipeline läuft.</summary>
    // === Router-Handler für das gemeinsame RendererCardTemplate. ===
    // Routen anhand des DataContext-Typs an die jeweilige Item-spezifische Logik.
    // Die alten Tag-basierten Handler bleiben darunter erhalten und werden hier
    // einfach durchgereicht — Tag bindet bei allen drei Items auf Id (DLNA per
    // Alias auf Udn), sodass die bestehende Implementierung weiter funktioniert.

    private void Card_Speaker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        switch (btn.DataContext)
        {
            case PlayerSettingsItem: PlayerSpeaker_Click(sender, e); break;
            case DlnaRendererItem: DlnaSpeaker_Click(sender, e); break;
            case AirPlayRendererItem: AirPlaySpeaker_Click(sender, e); break;
        }
    }

    private void Card_Rename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        switch (btn.DataContext)
        {
            case PlayerSettingsItem: RenamePlayer_Click(sender, e); break;
            case DlnaRendererItem: DlnaRename_Click(sender, e); break;
            case AirPlayRendererItem: AirPlayRename_Click(sender, e); break;
        }
    }

    private void Card_Forget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        switch (btn.DataContext)
        {
            case PlayerSettingsItem: ForgetPlayer_Click(sender, e); break;
            case DlnaRendererItem: DlnaForget_Click(sender, e); break;
            case AirPlayRendererItem: AirPlayForget_Click(sender, e); break;
        }
    }

    /// <summary>Lautsprecher-Klick auf einer LMS-Player-Karte: toggelt InSyncGroup
    /// (= „Stream auf diesem Player abspielen"). Persistenz und Live-Attach/Detach
    /// erledigt der UpdatePlayer-Handler über die PropertyChanged-Kette.</summary>
    private void PlayerSpeaker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var item = _items.FirstOrDefault(p => p.Id == id);
        if (item is null || !item.IsEnabled) return;
        item.InSyncGroup = !item.InSyncGroup;
    }

    /// <summary>Lautsprecher-Klick auf einer DLNA-Karte: DLNA hat keinen separaten
    /// „spielt auf diesem"-Status — bei DLNA macht der Toggle dasselbe wie die aktiv-CheckBox.</summary>
    private void DlnaSpeaker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string udn }) return;
        var item = _dlnaItems.FirstOrDefault(d => d.Udn == udn);
        if (item is null || !item.IsEnabled) return;
        item.InSyncGroup = !item.InSyncGroup;
    }

    private void AirPlaySpeaker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var item = _airPlayItems.FirstOrDefault(a => a.Id == id);
        if (item is null || !item.CanToggleEnabled || !item.IsEnabled) return;
        item.InSyncGroup = !item.InSyncGroup;
    }

    private async Task AttachPlayerToLiveStreamAsync(string playerId)
    {
        var url = _pipeline.StreamUrl?.ToString();
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            var entry = _settings.Current.Players.FirstOrDefault(p => p.Id == playerId);
            if (entry is not null)
                await _volume.SetTrimAsync(playerId, entry.TrimPercent).ConfigureAwait(true);

            await _lms.PlayUrlAsync(playerId, url).ConfigureAwait(true);
        }
        catch (Exception ex) { AppLogger.Write(ex); }
    }

    private async Task DetachPlayerFromLiveStreamAsync(string playerId)
    {
        try { await _lms.StopAsync(playerId).ConfigureAwait(true); }
        catch (Exception ex) { AppLogger.Write(ex); }
    }

    private async Task AttachDlnaToLiveStreamAsync(string udn)
    {
        var url = _pipeline.StreamUrl?.ToString();
        if (string.IsNullOrEmpty(url)) return;
        var r = _settings.Current.DlnaRenderers.FirstOrDefault(x => x.Udn == udn);
        var renderer = r is null ? null : BuildRendererFrom(r);
        if (renderer is null) return;
        var prebuffer = _settings.Current.Services.DlnaBufferMs;
        try { await _dlna.PlayUrlAsync(renderer, url, prebufferMs: prebuffer).ConfigureAwait(true); }
        catch (Exception ex) { AppLogger.Write(ex); }
    }

    private async Task DetachDlnaFromLiveStreamAsync(string udn)
    {
        var r = _settings.Current.DlnaRenderers.FirstOrDefault(x => x.Udn == udn);
        var renderer = r is null ? null : BuildRendererFrom(r);
        if (renderer is null) return;
        try { await _dlna.StopAsync(renderer).ConfigureAwait(true); }
        catch (Exception ex) { AppLogger.Write(ex); }
    }

    private async Task ShowMessageAsync(string message, string title)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await dlg.ShowAsync();
    }

    private void UpdatePlayer(PlayerSettingsItem item, string? propertyName = null)
    {
        if (_suppress) return;
        var entry = _settings.Current.Players.FirstOrDefault(p => p.Id == item.Id);
        if (entry is null) return;
        var enabledChanged = entry.IsEnabled != item.IsEnabled;
        var syncGroupChanged = entry.InActiveSyncGroup != item.InSyncGroup;

        entry.TrimPercent = item.TrimPercent;
        entry.IsEnabled = item.IsEnabled;
        // Lautsprecher-Icon = „Stream auf diesem Player". Persistiert separat von der
        // aktiv-CheckBox (die nur für die Mini-Fenster-Sichtbarkeit ist).
        entry.InActiveSyncGroup = item.InSyncGroup;
        _settings.NotifyChanged();

        if (enabledChanged)
        {
            _bus.RaisePlayerChanged(new PlayerChangedEventArgs
            {
                PlayerId = item.Id,
                Kind = PlayerChangeKind.Enabled,
                Enabled = item.IsEnabled,
            });
            // Wenn der Player ausgeblendet wird, soll er aus dem laufenden Stream raus.
            if (!item.IsEnabled && _pipeline.IsStreaming)
            {
                _ = DetachPlayerFromLiveStreamAsync(item.Id);
                // Sync-Group-Flag mit-runterziehen, damit nach Reaktivieren nicht überraschend spielt.
                entry.InActiveSyncGroup = false;
                item.InSyncGroup = false;
            }
        }

        // Lautsprecher-Klick → an/aus an der laufenden Pipeline UND Bus-Event,
        // damit das Mini-Fenster den Speaker-Toggle live mitführt.
        if (syncGroupChanged)
        {
            _bus.RaisePlayerChanged(new PlayerChangedEventArgs
            {
                PlayerId = item.Id,
                Kind = PlayerChangeKind.Power,
                Powered = item.InSyncGroup,
            });
            if (_pipeline.IsStreaming)
            {
                if (item.InSyncGroup) _ = AttachPlayerToLiveStreamAsync(item.Id);
                else _ = DetachPlayerFromLiveStreamAsync(item.Id);
            }
        }

        // Slider-Änderung direkt an LMS senden. Windows-Mute wird im Volume-Service separat gespiegelt.
        if (propertyName is null or nameof(PlayerSettingsItem.TrimPercent))
        {
            _ = _volume.SetTrimAsync(item.Id, item.TrimPercent);
            _bus.RaisePlayerChanged(new PlayerChangedEventArgs
            {
                PlayerId = item.Id,
                Kind = PlayerChangeKind.Volume,
                Volume = item.TrimPercent,
            });
        }
    }

    private void ForgetPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var entry = _settings.Current.Players.FirstOrDefault(p => p.Id == id);
        if (entry is not null) _settings.Current.Players.Remove(entry);
        _settings.NotifyChanged();
        Load();
    }

    private async void DlnaDiscover_Click(object sender, RoutedEventArgs e)
    {
        DlnaDiscoverButton.IsEnabled = false;
        DlnaHint.Text = "Suche DLNA-Renderer im LAN …";
        try
        {
            var found = await _dlna.DiscoverRenderersAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(true);
            MergeDlnaSnapshot(found);
            _settings.NotifyChanged();
            _suppress = true;
            RebuildDlnaList();
            _suppress = false;

            DlnaHint.Text = found.Count switch
            {
                0 => "Keine DLNA-Renderer gefunden. Smart-TV oder AVR im selben LAN und eingeschaltet?",
                1 => "1 DLNA-Renderer gefunden.",
                _ => $"{found.Count} DLNA-Renderer gefunden.",
            };
        }
        catch (Exception ex)
        {
            DlnaHint.Text = $"Discovery fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            DlnaDiscoverButton.IsEnabled = true;
        }
    }

    private void MergeDlnaSnapshot(IReadOnlyList<DlnaRenderer> found)
    {
        var settings = _settings.Current;
        foreach (var r in found)
        {
            var existing = settings.DlnaRenderers.FirstOrDefault(p => p.Udn == r.Udn);
            if (existing is null)
            {
                var nextOrder = settings.DlnaRenderers.Count == 0 ? 1 : settings.DlnaRenderers.Max(d => d.SortOrder) + 1;
                settings.DlnaRenderers.Add(new PersistedDlnaRenderer
                {
                    Udn = r.Udn,
                    FriendlyName = r.FriendlyName,
                    AvTransportControlUrl = r.AvTransportControlUrl.AbsoluteUri,
                    RenderingControlUrl = r.RenderingControlUrl?.AbsoluteUri,
                    Manufacturer = r.Manufacturer,
                    ModelName = r.ModelName,
                    LastSeenUtc = DateTimeOffset.UtcNow,
                    SortOrder = nextOrder,
                    IsEnabled = settings.General.AutoActivateNewDevices,
                });
            }
            else
            {
                existing.FriendlyName = r.FriendlyName;
                existing.AvTransportControlUrl = r.AvTransportControlUrl.AbsoluteUri;
                existing.RenderingControlUrl = r.RenderingControlUrl?.AbsoluteUri;
                existing.Manufacturer = r.Manufacturer;
                existing.ModelName = r.ModelName;
                existing.LastSeenUtc = DateTimeOffset.UtcNow;
            }
        }
    }

    private DlnaRenderer? ResolveDlnaRenderer(string udn)
    {
        var p = _settings.Current.DlnaRenderers.FirstOrDefault(r => r.Udn == udn);
        if (p is null) return null;
        if (!Uri.TryCreate(p.AvTransportControlUrl, UriKind.Absolute, out var av)) return null;
        Uri? rc = null;
        if (!string.IsNullOrEmpty(p.RenderingControlUrl))
            Uri.TryCreate(p.RenderingControlUrl, UriKind.Absolute, out rc);
        return new DlnaRenderer
        {
            Udn = p.Udn,
            FriendlyName = p.FriendlyName,
            AvTransportControlUrl = av,
            RenderingControlUrl = rc,
            Manufacturer = p.Manufacturer,
            ModelName = p.ModelName,
        };
    }

    private async void DlnaPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string udn } button) return;
        var item = _dlnaItems.FirstOrDefault(x => x.Udn == udn);
        var renderer = ResolveDlnaRenderer(udn);
        if (item is null || renderer is null) return;

        button.IsEnabled = false;
        try
        {
            if (item.IsPlaying)
            {
                await _dlna.StopAsync(renderer).ConfigureAwait(true);
                item.IsPlaying = false;
            }
            else
            {
                if (!_pipeline.IsStreaming)
                    await _pipeline.StartAsync().ConfigureAwait(true);

                var url = _pipeline.StreamUrl?.ToString();
                if (string.IsNullOrEmpty(url))
                {
                    await ShowMessageAsync("Stream-URL ist (noch) nicht verfügbar.", "abspielen");
                    return;
                }
                var prebuffer = _settings.Current.Services.DlnaBufferMs;
                await _dlna.PlayUrlAsync(renderer, url, prebufferMs: prebuffer).ConfigureAwait(true);
                item.IsPlaying = true;
            }
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"DLNA-Befehl fehlgeschlagen: {ex.Message}", item.IsPlaying ? "stoppen" : "abspielen");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void DlnaForget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string udn }) return;
        var entry = _settings.Current.DlnaRenderers.FirstOrDefault(r => r.Udn == udn);
        if (entry is not null) _settings.Current.DlnaRenderers.Remove(entry);
        _settings.NotifyChanged();
        _suppress = true;
        RebuildDlnaList();
        _suppress = false;
    }

    private async void RenamePlayer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var entry = _settings.Current.Players.FirstOrDefault(p => p.Id == id);
        if (entry is null) return;

        var input = new TextBox
        {
            PlaceholderText = entry.LastSeenName ?? id,
            Text = entry.CustomName ?? string.Empty,
            AcceptsReturn = false,
        };
        var dlg = new ContentDialog
        {
            Title = "Player umbenennen",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"LMS-Name: {entry.LastSeenName ?? id}",
                        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    },
                    input,
                    new TextBlock
                    {
                        Text = "Leer lassen, um wieder den vom LMS gemeldeten Namen anzuzeigen.",
                        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
            PrimaryButtonText = "übernehmen",
            CloseButtonText = "abbrechen",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var newName = input.Text.Trim();
        entry.CustomName = string.IsNullOrEmpty(newName) ? null : newName;
        _settings.NotifyChanged();
        Load();
    }

    /// <summary>Generische Rename-Dialog-Logik für DLNA-Renderer.</summary>
    private async void DlnaRename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string udn }) return;
        var entry = _settings.Current.DlnaRenderers.FirstOrDefault(d => d.Udn == udn);
        if (entry is null) return;
        var newName = await PromptRenameAsync(
            title: "DLNA-Renderer umbenennen",
            originalName: entry.FriendlyName,
            currentCustom: entry.CustomName);
        if (newName is null) return;
        entry.CustomName = string.IsNullOrEmpty(newName) ? null : newName;
        _settings.NotifyChanged();
        RebuildDlnaList();
    }

    private async void AirPlayRename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var item = _airPlayItems.FirstOrDefault(x => x.Id == id);
        if (item is null) return;
        var newName = await PromptRenameAsync(
            title: "AirPlay-Empfänger umbenennen",
            originalName: item.DisplayName,
            currentCustom: null);
        if (newName is null) return;
        var entry = _settings.Current.AirPlayDevices.FirstOrDefault(x => x.Id == id);
        if (entry is not null)
        {
            entry.CustomName = string.IsNullOrEmpty(newName) ? null : newName;
            _settings.NotifyChanged();
        }
        item.DisplayName = string.IsNullOrEmpty(newName) ? item.DisplayName : newName;
    }

    /// <summary>
    /// Gemeinsamer Umbenennen-Dialog: zeigt Original-Namen, lässt User Wunsch-Namen eingeben.
    /// Null = abgebrochen. Leerstring = Custom-Name löschen (zurück auf Original).
    /// </summary>
    private async Task<string?> PromptRenameAsync(string title, string originalName, string? currentCustom)
    {
        var input = new TextBox
        {
            PlaceholderText = originalName,
            Text = currentCustom ?? string.Empty,
            AcceptsReturn = false,
        };
        var dlg = new ContentDialog
        {
            Title = title,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Originalname: {originalName}",
                        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    },
                    input,
                    new TextBlock
                    {
                        Text = "Leer lassen, um den Originalnamen wiederherzustellen.",
                        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
            PrimaryButtonText = "übernehmen",
            CloseButtonText = "abbrechen",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary ? input.Text.Trim() : null;
    }

    /// <summary>
    /// Nach Drag&amp;Drop in der Player-Liste: neue Reihenfolge auf die persistierten
    /// SortOrder-Werte mappen (1, 2, 3, …) und speichern. Das Mini-Fenster zieht
    /// über das Saved-Event automatisch nach.
    /// </summary>
    private void PlayersList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var players = _settings.Current.Players;
        int order = 1;
        foreach (var item in _items)
        {
            var entry = players.FirstOrDefault(p => p.Id == item.Id);
            if (entry is not null) entry.SortOrder = order;
            order++;
        }
        _settings.NotifyChanged();
    }

    private void DlnaList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var renderers = _settings.Current.DlnaRenderers;
        int order = 1;
        foreach (var item in _dlnaItems)
        {
            var entry = renderers.FirstOrDefault(d => d.Udn == item.Udn);
            if (entry is not null) entry.SortOrder = order;
            order++;
        }
        _settings.NotifyChanged();
    }

    private void AirPlayList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var devices = _settings.Current.AirPlayDevices;
        int order = 1;
        foreach (var item in _airPlayItems)
        {
            var entry = devices.FirstOrDefault(a => a.Id == item.Id);
            if (entry is not null) entry.SortOrder = order;
            order++;
        }
        _settings.NotifyChanged();
    }
}

public sealed class DlnaRendererItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isPlaying;
    private bool _isEnabled = true;
    private bool _inSyncGroup;
    private int _volumePercent = 50;
    private string _statusText = string.Empty;

    public required string Udn { get; init; }
    /// <summary>Alias für Udn — damit das gemeinsame RendererCardTemplate
    /// einheitlich auf Id binden kann (LMS hat Id, DLNA hat Udn, AirPlay hat Id).</summary>
    public string Id => Udn;
    public required string DisplayName { get; init; }

    /// <summary>LMS/DLNA kennen kein Protokoll-Badge — Slot leer, Visibility Collapsed.</summary>
    public string ProtocolText => string.Empty;
    public Microsoft.UI.Xaml.Visibility ProtocolBadgeVisible
        => Microsoft.UI.Xaml.Visibility.Collapsed;
    /// <summary>aktiv-CheckBox und Lautsprecher-Toggle sind hier immer bedienbar.</summary>
    public bool CanToggleEnabled => true;

    /// <summary>Lautsprecher-Toggle = „Stream auf diesem Renderer". Persistiert in IsPlayActive.</summary>
    public bool InSyncGroup
    {
        get => _inSyncGroup;
        set
        {
            if (_inSyncGroup == value) return;
            _inSyncGroup = value;
            Raise(nameof(InSyncGroup));
            Raise(nameof(SpeakerGlyph));
        }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; Raise(nameof(StatusText)); }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            Raise(nameof(IsPlaying));
            Raise(nameof(PlayPauseLabel));
            Raise(nameof(PlayPauseSymbol));
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            Raise(nameof(IsEnabled));
            Raise(nameof(CardOpacity));
            Raise(nameof(PowerGlyph));
        }
    }

    public int VolumePercent
    {
        get => _volumePercent;
        set { _volumePercent = value; Raise(nameof(VolumePercent)); }
    }

    public double CardOpacity => _isEnabled ? 1.0 : 0.4;

    /// <summary>Segoe Fluent: E767 (Lautsprecher an), E74F (Lautsprecher durchgestrichen).
    /// Identisch zum Mini-Fenster, damit die Geste optisch konsistent ist.</summary>
    public string PowerGlyph => _isEnabled ? "" : "";

    /// <summary>Lautsprecher-Icon spiegelt InSyncGroup, NICHT IsEnabled.</summary>
    public string SpeakerGlyph => _inSyncGroup ? "" : "";

    public string PlayPauseLabel => _isPlaying ? "stoppen" : "abspielen";
    public Microsoft.UI.Xaml.Controls.Symbol PlayPauseSymbol
        => _isPlaying ? Microsoft.UI.Xaml.Controls.Symbol.Stop : Microsoft.UI.Xaml.Controls.Symbol.Play;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

/// <summary>UI-Item für die AirPlay-Empfänger-Liste in den Streaming-Einstellungen.</summary>
public sealed class AirPlayRendererItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isPlaying;
    private bool _isEnabled = true;
    private bool _inSyncGroup;
    private int _volumePercent = 50;
    private string _statusText = string.Empty;
    private string _displayName = string.Empty;

    /// <summary>Lautsprecher-Toggle = „Stream auf diesem Empfänger". Persistiert in IsPlayActive.</summary>
    public bool InSyncGroup
    {
        get => _inSyncGroup;
        set
        {
            if (_inSyncGroup == value) return;
            _inSyncGroup = value;
            Raise(nameof(InSyncGroup));
            Raise(nameof(SpeakerGlyph));
        }
    }

    public required string Id { get; init; }

    public required string DisplayName
    {
        get => _displayName;
        set { _displayName = value; Raise(nameof(DisplayName)); }
    }

    public required string ProtocolText { get; init; }

    /// <summary>Steuert das Protokoll-Badge im gemeinsamen Card-Template.</summary>
    public Microsoft.UI.Xaml.Visibility ProtocolBadgeVisible
        => string.IsNullOrEmpty(ProtocolText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

    /// <summary>True wenn das Gerät klassisches AirPlay 1 unterstützt — nur dann können
    /// wir aktuell streamen. AirPlay-2-only-Geräte sind nur zur Ansicht.</summary>
    public bool CanStream { get; init; }

    /// <summary>Convenience für die XAML-Bindings: ob die aktiv-CheckBox überhaupt bedienbar
    /// sein soll. Identisch zu CanStream, aber als eigener Bind-Pfad lesbar.</summary>
    public bool CanToggleEnabled => CanStream;

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; Raise(nameof(StatusText)); }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            Raise(nameof(IsPlaying));
            Raise(nameof(PlayPauseLabel));
            Raise(nameof(PlayPauseSymbol));
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled && CanStream;
        set
        {
            var effective = value && CanStream;
            if (_isEnabled == effective) return;
            _isEnabled = effective;
            Raise(nameof(IsEnabled));
            Raise(nameof(CardOpacity));
            Raise(nameof(PowerGlyph));
        }
    }

    public int VolumePercent
    {
        get => _volumePercent;
        set { _volumePercent = value; Raise(nameof(VolumePercent)); }
    }

    public double CardOpacity => IsEnabled ? 1.0 : 0.4;

    /// <summary>Lautsprecher-Icon (an/durchgestrichen) — gleich zu Squeeze und DLNA.</summary>
    public string PowerGlyph => IsEnabled ? "" : "";

    /// <summary>Lautsprecher-Icon spiegelt InSyncGroup (= IsPlayActive), nicht IsEnabled.</summary>
    public string SpeakerGlyph => _inSyncGroup ? "" : "";

    public string PlayPauseLabel => _isPlaying ? "stoppen" : "abspielen";
    public Microsoft.UI.Xaml.Controls.Symbol PlayPauseSymbol
        => _isPlaying ? Microsoft.UI.Xaml.Controls.Symbol.Stop : Microsoft.UI.Xaml.Controls.Symbol.Play;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public sealed class PlayerSettingsItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _appControlsVolume;
    private int _trimPercent;
    private bool _isPlaying;
    private bool _isEnabled = true;
    private bool _inSyncGroup;
    private string _statusText = string.Empty;

    public required string Id { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>LMS-Karten haben kein Protokoll-Badge — Slot bleibt leer/unsichtbar.</summary>
    public string ProtocolText => string.Empty;
    public Microsoft.UI.Xaml.Visibility ProtocolBadgeVisible
        => Microsoft.UI.Xaml.Visibility.Collapsed;
    /// <summary>aktiv-CheckBox und Lautsprecher-Toggle sind bei LMS immer bedienbar.</summary>
    public bool CanToggleEnabled => true;

    /// <summary>Alias für TrimPercent — das gemeinsame RendererCardTemplate
    /// bindet den Pegel-Slider einheitlich auf VolumePercent (LMS, DLNA, AirPlay).</summary>
    public int VolumePercent
    {
        get => _trimPercent;
        set { TrimPercent = value; }
    }

    /// <summary>„Stream auf diesem Gerät" — gespiegelt zum Lautsprecher-Icon im Mini-Fenster.
    /// Persistiert sich in <see cref="PersistedPlayer.InActiveSyncGroup"/>.</summary>
    public bool InSyncGroup
    {
        get => _inSyncGroup;
        set
        {
            if (_inSyncGroup == value) return;
            _inSyncGroup = value;
            Raise(nameof(InSyncGroup));
            Raise(nameof(SpeakerGlyph));
        }
    }

    /// <summary>Lautsprecher-an (U+E767) / -aus (U+E74F). Identisch zum Mini-Fenster.</summary>
    public string SpeakerGlyph => _inSyncGroup ? "" : "";

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; Raise(nameof(StatusText)); }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            Raise(nameof(IsPlaying));
            Raise(nameof(PlayPauseLabel));
            Raise(nameof(PlayPauseSymbol));
        }
    }

    public string PlayPauseLabel => _isPlaying ? "stoppen" : "abspielen";
    public Microsoft.UI.Xaml.Controls.Symbol PlayPauseSymbol
        => _isPlaying ? Microsoft.UI.Xaml.Controls.Symbol.Stop : Microsoft.UI.Xaml.Controls.Symbol.Play;

    public bool AppControlsVolume
    {
        get => _appControlsVolume;
        set { _appControlsVolume = value; Raise(nameof(AppControlsVolume)); }
    }
    public int TrimPercent
    {
        get => _trimPercent;
        set
        {
            if (_trimPercent == value) return;
            _trimPercent = value;
            Raise(nameof(TrimPercent));
            // Spiegel-Property für das gemeinsame Card-Template.
            Raise(nameof(VolumePercent));
        }
    }

    /// <summary>
    /// Wenn false → Player ist vom User abgeschaltet, Karte erscheint ausgegraut in den Einstellungen
    /// und wird im Mini-Fenster gar nicht angezeigt.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            Raise(nameof(IsEnabled));
            Raise(nameof(CardOpacity));
            Raise(nameof(PowerGlyph));
        }
    }

    /// <summary>0.4 wenn deaktiviert, 1.0 sonst — für visuelles Grayout der Karte.</summary>
    public double CardOpacity => _isEnabled ? 1.0 : 0.4;

    /// <summary>Segoe Fluent: Lautsprecher an (U+E767) bzw. durchgestrichen (U+E74F).
    /// Identisch zum Mini-Fenster — eine Geste, ein Icon.</summary>
    public string PowerGlyph => _isEnabled ? "" : "";

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
