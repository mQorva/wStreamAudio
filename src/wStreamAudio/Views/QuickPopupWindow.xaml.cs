using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Core.Models;
using wStreamAudio.Core.Networking;
using wStreamAudio.Infrastructure.Logging;
using wStreamAudio.Services;
using wStreamAudio.ViewModels;

namespace wStreamAudio.Views;

public sealed partial class QuickPopupWindow : Window
{
    private const int PopupWidth = 380;
    private const int PopupHeight = 480;

    private readonly ISettingsService _settings;
    private readonly ILmsClient _lms;
    private readonly StreamPipelineCoordinator _pipeline;
    private readonly IVolumeService _volume;
    private readonly IPlayerStateBus _bus;
    private readonly ObservableCollection<PlayerListItemViewModel> _players = new();
    private bool _suppressEvents;
    private string _visiblePlayerSignature = string.Empty;
    private bool _pinned; // wenn true: Always-on-top aktiviert (kein Auto-Hide mehr)
    private OverlappedPresenter? _presenter;

    public QuickPopupWindow(
        ISettingsService settings,
        ILmsClient lms,
        StreamPipelineCoordinator pipeline,
        IVolumeService volume,
        IPlayerStateBus bus)
    {
        _settings = settings;
        _lms = lms;
        _pipeline = pipeline;
        _volume = volume;
        _bus = bus;

        InitializeComponent();
        Title = "wStreamAudio";

        // Borderless Presenter ohne System-Titelleiste. Resizable, damit der User bei vielen
        // Playern das Fenster ziehen kann. Drag-Region kommt aus dem XAML (siehe SetTitleBar).
        var presenter = OverlappedPresenter.Create();
        // Non-resizable → keine Resize-Border, kein „Selektions-Rahmen", wenn das Fenster aktiv ist.
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        // Always-on-top kommt aus dem Pin-Status (QuickPopupSticky). Nicht mehr hartcodiert.
        presenter.IsAlwaysOnTop = _settings.Current.QuickPopupSticky;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        _presenter = presenter;
        AppWindow.IsShownInSwitchers = false;

        // Eigene Drag-Region: oberer Bereich (Titelzeile mit „wStreamAudio" + X) wird
        // bewegbar, ohne dass eine System-Titelleiste sichtbar ist.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);

        // Gespeicherte Größe übernehmen, sonst Default.
        var savedSize = _settings.Current.QuickPopupPlacement;
        var w = savedSize?.Width is int sw && sw >= 300 ? sw : PopupWidth;
        var h = savedSize?.Height is int sh && sh >= 200 ? sh : PopupHeight;
        AppWindow.Resize(new SizeInt32(w, h));

        // Mica statt Acrylic: Acrylic schaltet beim Aktivieren/Deaktivieren des Fensters
        // zwischen voller Transparenz und Fallback-Farbe um — das Mini-Fenster wirkt dann
        // "blasser", sobald man hineinklickt. Mica bleibt visuell konstant.
        SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
        ThemeService.ApplyTo(this, _settings.Current.General.Theme);

        // Sticky-Zustand aus Settings wiederherstellen, damit der Pin-Status zwischen Sessions hält.
        _pinned = _settings.Current.QuickPopupSticky;
        UpdatePinIcon();

        PlayerList.ItemsSource = _players;
        _pipeline.StreamingChanged += OnStreamingChanged;
        _bus.PlayerChanged += OnBusPlayerChanged;
        // Wenn die Streaming-Seite (oder irgendein anderer Pfad) Persistenz schreibt —
        // z.B. „aktiv"-Toggle bei DLNA oder AirPlay — die Liste neu mergen. Sonst tauchen
        // frisch aktivierte Geräte nicht im Mini-Fenster auf, solange es offen ist.
        _settings.Saved += OnSettingsSaved;
        Closed += OnClosed;
        Activated += OnActivated;
        AppWindow.Changed += OnAppWindowChanged;
    }

    /// <summary>
    /// Wenn anderswo (Settings, Tray) sich eine Player-Eigenschaft ändert, das passende
    /// ViewModel in der Mini-Fenster-Liste direkt aktualisieren — ohne LMS-Roundtrip.
    /// </summary>
    private void OnSettingsSaved(object? sender, EventArgs e)
    {
        // Nur bei strukturellen Änderungen neu laden. Reine Lautstärke- oder Fensterpositions-
        // Saves dürfen die ItemsControl-Liste nicht ersetzen, sonst verliert der Slider während
        // des Ziehens seinen Thumb.
        var signature = BuildVisiblePlayerSignature();
        if (string.Equals(signature, _visiblePlayerSignature, StringComparison.Ordinal))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() => _ = RefreshPlayersAsync());
    }

    private void OnBusPlayerChanged(object? sender, PlayerChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // Wenn ein Player deaktiviert/aktiviert wurde, komplette Liste neu mergen —
            // dann verschwindet bzw. erscheint er sofort im Mini-Fenster.
            if (e.Kind == PlayerChangeKind.Enabled)
            {
                _ = RefreshPlayersAsync();
                return;
            }

            var vm = _players.FirstOrDefault(p => p.PlayerId == e.PlayerId);
            if (vm is null) return;
            _suppressEvents = true;
            try
            {
                if (e.Volume is int v) vm.EffectiveVolume = v;
                if (e.Powered is bool p) vm.IsPowered = p;
            }
            finally { _suppressEvents = false; }
        });
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange && !args.DidSizeChange) return;
        var p = _settings.Current.QuickPopupPlacement ??= new Core.Settings.WindowPlacement();
        p.X = sender.Position.X;
        p.Y = sender.Position.Y;
        p.Width = sender.Size.Width;
        p.Height = sender.Size.Height;
        _settings.NotifyChanged();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // Beim Schließen merken: Mini ist aus → beim nächsten App-Start nicht wieder hochfahren.
        _settings.Current.QuickPopupOpen = false;
        _settings.NotifyChanged();
        AppWindow.Hide();
        App.Instance?.NotifyQuickPopupVisibilityChanged();
    }

    /// <summary>Klick auf den Status-Text („abspielen"/„anhalten") tut dasselbe wie der
    /// Klick auf den Play-Button — der Text ist als Klickfläche oft natürlicher zu treffen.</summary>
    private void PlayPauseText_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        PlayPauseButton_Click(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    /// <summary>Doppelklick auf die Titelzeile ist bei Standard-Fenstern „maximieren". Hier ohne
    /// System-Titelleiste haben wir das selbst in der Hand — der Doppelklick öffnet stattdessen
    /// die Einstellungen, weil das die häufigste Folge-Aktion vom Mini-Fenster aus ist.</summary>
    private void DragRegion_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        App.Instance?.ShowSettingsWindow();
        e.Handled = true;
    }

    public void ShowAtTray()
    {
        if (AppWindow is null) return;

        var saved = _settings.Current.QuickPopupPlacement;
        PointInt32 target;
        if (saved?.X is int sx && saved?.Y is int sy && IsOnScreen(sx, sy))
        {
            target = new PointInt32(sx, sy);
        }
        else
        {
            var area = DisplayArea.Primary.WorkArea;
            target = new PointInt32(
                area.X + area.Width - PopupWidth - 12,
                area.Y + area.Height - PopupHeight - 12);
        }
        AppWindow.Move(target);
        AppWindow.Show();

        // Sichtbarkeit persistieren — beim nächsten App-Start automatisch wieder hochfahren.
        _settings.Current.QuickPopupOpen = true;
        _settings.NotifyChanged();

        UpdatePlayPauseButton();
        _ = RefreshPlayersAsync();
    }

    /// <summary>
    /// Fensterhöhe an den tatsächlichen Inhalt anpassen — damit alle Player-Zeilen sichtbar
    /// sind, egal wie viele es gibt. Wird nach jedem Merge der Player-Liste aufgerufen.
    /// </summary>
    private void ResizeToContent()
    {
        if (RootGrid is null || AppWindow is null) return;
        try
        {
            // Wichtig: Measure mit Infinity gibt die ECHTE Wunsch-Höhe des Inhalts zurück.
            // ActualHeight wäre nur die aktuelle Container-Höhe (also was wir vorher gesetzt
            // haben) — damit würde das Fenster nie kleiner werden, nur größer.
            RootGrid.Measure(new Windows.Foundation.Size(RootGrid.ActualWidth, double.PositiveInfinity));
            var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            var desired = RootGrid.DesiredSize.Height;
            if (desired <= 0) return;
            var pxHeight = (int)Math.Ceiling(desired * scale);
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            pxHeight = Math.Min(pxHeight, area.Height - 24);
            AppWindow.Resize(new SizeInt32(AppWindow.Size.Width, pxHeight));
        }
        catch (Exception ex) { AppLogger.Write(ex); }
    }

    private static bool IsOnScreen(int x, int y)
    {
        var rect = new RectInt32(x, y, PopupWidth, PopupHeight);
        var display = DisplayArea.GetFromRect(rect, DisplayAreaFallback.None);
        return display is not null;
    }

    private void UpdatePlayPauseButton()
    {
        // Segoe Fluent: E768 = Play, E769 = Pause
        if (_pipeline.IsStreaming)
        {
            PlayPauseIcon.Glyph = "";
            PlayPauseText.Text = "anhalten";
        }
        else
        {
            PlayPauseIcon.Glyph = "";
            PlayPauseText.Text = "abspielen";
        }
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        // Sofortiges visuelles Feedback — sonst sieht der User 1-2 Sekunden lang nichts,
        // während Capture/Server/LMS/DLNA/AirPlay nacheinander hochlaufen.
        PlayPauseButton.IsEnabled = false;
        var willStart = !_pipeline.IsStreaming;
        PlayPauseIcon.Glyph = willStart ? "" : ""; // E769 Pause / E768 Play
        PlayPauseText.Text = willStart ? "starte…" : "stoppe…";
        try
        {
            if (_pipeline.IsStreaming) await _pipeline.StopAsync().ConfigureAwait(true);
            else await _pipeline.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { AppLogger.Write(ex); }
        finally
        {
            PlayPauseButton.IsEnabled = true;
            UpdatePlayPauseButton();
        }
    }

    private void OnStreamingChanged(object? sender, bool isStreaming)
    {
        DispatcherQueue.TryEnqueue(UpdatePlayPauseButton);
    }

    private async Task RefreshPlayersAsync()
    {
        try
        {
            var snapshots = await _lms.GetPlayersAsync().ConfigureAwait(true);
            DispatcherQueue.TryEnqueue(() => MergePlayers(snapshots));
        }
        catch
        {
            DispatcherQueue.TryEnqueue(() => MergePlayers(Array.Empty<PlayerSnapshot>()));
        }
    }

    private void MergePlayers(IReadOnlyList<PlayerSnapshot> snapshots)
    {
        _suppressEvents = true;

        // Reihenfolge: LMS → DLNA → AirPlay, in Persistenz-Reihenfolge — identisch zum
        // Hauptfenster. Statt In-Place-Sortieren wird die Liste komplett neu aufgebaut.
        foreach (var v in _players) v.PropertyChanged -= OnPlayerItemChanged;
        _players.Clear();

        // Erst die IsLocalDevice-Flags der persistierten Player aus aktuellen Live-Snapshots
        // aktualisieren — danach blendet die UI sie sauber aus.
        var settings = _settings.Current;
        foreach (var live in snapshots)
        {
            var entry = settings.Players.FirstOrDefault(p => p.Id == live.Id);
            if (entry is not null) entry.IsLocalDevice = LocalNetwork.IsLocal(live.Ip);
        }

        // Lokale Snapshots aus der UI-Schleife rauswerfen.
        snapshots = snapshots.Where(s => !LocalNetwork.IsLocal(s.Ip)).ToList();

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var persisted in settings.Players.OrderBy(p => p.SortOrder).ToList())
        {
            // Lokale Geräte (Audio-Schleife) und vom User per CheckBox ausgeblendete
            // Player nicht zeigen. Die aktiv-CheckBox (= IsEnabled) ist die Sichtbarkeits-
            // Schaltung. Das Lautsprecher-Icon im Mini = InActiveSyncGroup („spielt auf
            // diesem Gerät") — wird unten gemappt.
            if (persisted.IsLocalDevice) continue;
            if (!persisted.IsEnabled) continue;
            seenIds.Add(persisted.Id);
            var live = snapshots.FirstOrDefault(s => s.Id == persisted.Id);
            var name = !string.IsNullOrEmpty(persisted.CustomName)
                ? persisted.CustomName
                : (live?.Name ?? persisted.LastSeenName ?? persisted.Id);
            // Slider zeigt die persistierte LMS-Lautstärke — identisch zur Settings-Seite.
            var effVol = persisted.TrimPercent;
            var status = live is null
                ? "offline"
                : (live.IsPlaying ? "spielt" : (live.IsConnected ? "online" : "offline"));

            var vm = _players.FirstOrDefault(p => p.PlayerId == persisted.Id);
            if (vm is null)
            {
                vm = new PlayerListItemViewModel
                {
                    PlayerId = persisted.Id,
                    Kind = live?.Kind ?? PlayerKind.Unknown,
                    DisplayName = name,
                    IsConnected = live?.IsConnected ?? false,
                    // Lautsprecher-Icon spiegelt InActiveSyncGroup (= „spielt auf diesem
                    // Gerät"), NICHT die LMS-Live-Power und auch nicht IsEnabled.
                    IsPowered = persisted.InActiveSyncGroup,
                    InSyncGroup = persisted.InActiveSyncGroup,
                    EffectiveVolume = effVol,
                    StatusText = status,
                };
                vm.PropertyChanged += OnPlayerItemChanged;
                _players.Add(vm);
            }
            else
            {
                vm.Kind = live?.Kind ?? PlayerKind.Unknown;
                vm.DisplayName = name;
                vm.IsConnected = live?.IsConnected ?? false;
                vm.IsPowered = persisted.InActiveSyncGroup;
                vm.InSyncGroup = persisted.InActiveSyncGroup;
                vm.EffectiveVolume = effVol;
                vm.StatusText = status;
            }
        }

        foreach (var live in snapshots)
        {
            if (settings.Players.Any(p => p.Id == live.Id)) continue;
            var nextOrder = settings.Players.Count == 0 ? 1 : settings.Players.Max(p => p.SortOrder) + 1;
            settings.Players.Add(new PersistedPlayer
            {
                Id = live.Id,
                LastSeenName = live.Name,
                LastSeenUtc = DateTimeOffset.UtcNow,
                SortOrder = nextOrder,
                IsEnabled = settings.General.AutoActivateNewDevices,
            });
            var vm = new PlayerListItemViewModel
            {
                PlayerId = live.Id,
                Kind = live.Kind,
                DisplayName = live.Name,
                IsConnected = live.IsConnected,
                IsPowered = live.IsPowered,
                EffectiveVolume = Defaults.PlayerTrimDefault,
                StatusText = live.IsPlaying ? "spielt" : "online",
            };
            vm.PropertyChanged += OnPlayerItemChanged;
            _players.Add(vm);
            seenIds.Add(live.Id);
            _settings.NotifyChanged();
        }

        // === DLNA-Renderer (direkt angesteuert) ===
        // Nur „aktiv" markierte zeigen — Sichtbarkeit folgt der CheckBox auf der Streaming-Seite.
        foreach (var dlna in settings.DlnaRenderers.OrderBy(d => d.SortOrder).ToList())
        {
            if (!dlna.IsEnabled) continue;
            var id = "dlna:" + dlna.Udn;
            seenIds.Add(id);
            var name = !string.IsNullOrEmpty(dlna.CustomName) ? dlna.CustomName! : dlna.FriendlyName;
            var vm = _players.FirstOrDefault(p => p.PlayerId == id);
            if (vm is null)
            {
                vm = new PlayerListItemViewModel
                {
                    PlayerId = id,
                    Kind = PlayerKind.Dlna,
                    DisplayName = name,
                    IsConnected = true,
                    IsPowered = dlna.IsPlayActive,
                    EffectiveVolume = dlna.VolumePercent,
                    StatusText = "DLNA",
                };
                vm.PropertyChanged += OnPlayerItemChanged;
                _players.Add(vm);
            }
            else
            {
                vm.DisplayName = name;
                vm.IsPowered = dlna.IsPlayActive;
                vm.EffectiveVolume = dlna.VolumePercent;
            }
        }

        // === AirPlay-Empfänger (direkt angesteuert) ===
        foreach (var ap in settings.AirPlayDevices.OrderBy(a => a.SortOrder).ToList())
        {
            if (!ap.IsEnabled) continue;
            var id = "airplay:" + ap.Id;
            seenIds.Add(id);
            var name = !string.IsNullOrEmpty(ap.CustomName) ? ap.CustomName! : ap.FriendlyName;
            var vm = _players.FirstOrDefault(p => p.PlayerId == id);
            if (vm is null)
            {
                vm = new PlayerListItemViewModel
                {
                    PlayerId = id,
                    Kind = PlayerKind.AirPlay,
                    DisplayName = name,
                    IsConnected = true,
                    IsPowered = ap.IsPlayActive,
                    EffectiveVolume = ap.VolumePercent,
                    StatusText = ap.SupportsAirPlay2 ? "AirPlay 1+2" : "AirPlay 1",
                };
                vm.PropertyChanged += OnPlayerItemChanged;
                _players.Add(vm);
            }
            else
            {
                vm.DisplayName = name;
                vm.IsPowered = ap.IsPlayActive;
                vm.EffectiveVolume = ap.VolumePercent;
            }
        }

        for (int i = _players.Count - 1; i >= 0; i--)
        {
            if (!seenIds.Contains(_players[i].PlayerId))
            {
                _players[i].PropertyChanged -= OnPlayerItemChanged;
                _players.RemoveAt(i);
            }
        }

        _suppressEvents = false;
        _visiblePlayerSignature = BuildVisiblePlayerSignature();
        // Höhe an die Anzahl der jetzt sichtbaren Player anpassen (im nächsten Layout-Pass).
        DispatcherQueue.TryEnqueue(ResizeToContent);
    }

    private string BuildVisiblePlayerSignature()
    {
        var settings = _settings.Current;
        var parts = new List<string>();

        foreach (var p in settings.Players.OrderBy(p => p.SortOrder))
        {
            if (p.IsLocalDevice || !p.IsEnabled) continue;
            var name = p.CustomName ?? p.LastSeenName ?? p.Id;
            parts.Add($"lms:{p.Id}:{p.SortOrder}:{name}");
        }

        foreach (var d in settings.DlnaRenderers.OrderBy(d => d.SortOrder))
        {
            if (!d.IsEnabled) continue;
            var name = d.CustomName ?? d.FriendlyName;
            parts.Add($"dlna:{d.Udn}:{d.SortOrder}:{name}");
        }

        foreach (var a in settings.AirPlayDevices.OrderBy(a => a.SortOrder))
        {
            if (!a.IsEnabled) continue;
            var name = a.CustomName ?? a.FriendlyName;
            parts.Add($"airplay:{a.Id}:{a.SortOrder}:{name}");
        }

        return string.Join("|", parts);
    }

    private void OnPlayerItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressEvents || sender is not PlayerListItemViewModel vm) return;

        // DLNA-/AirPlay-Einträge tragen ein Prefix in der PlayerId, weil sie nicht im
        // LMS leben — die Routing-Logik unterscheidet sich (kein Sync, kein Power).
        if (vm.Kind == PlayerKind.Dlna)
        {
            HandleDlnaItemChanged(vm, e.PropertyName);
            return;
        }
        if (vm.Kind == PlayerKind.AirPlay)
        {
            HandleAirPlayItemChanged(vm, e.PropertyName);
            return;
        }

        var settings = _settings.Current;
        var entry = settings.Players.FirstOrDefault(p => p.Id == vm.PlayerId);

        switch (e.PropertyName)
        {
            case nameof(PlayerListItemViewModel.InSyncGroup):
                if (entry is not null) entry.InActiveSyncGroup = vm.InSyncGroup;
                _settings.NotifyChanged();
                _ = ApplySyncGroupToLmsAsync(vm);
                break;
            case nameof(PlayerListItemViewModel.IsPowered):
                // Lautsprecher-Geste im Mini = „Stream auf diesem Player". Schreibt
                // InActiveSyncGroup (gleiche Quelle wie der Lautsprecher-Toggle auf der
                // Streaming-Seite). Die aktiv-CheckBox dort (IsEnabled = Sichtbarkeit)
                // bleibt unberührt.
                if (entry is not null) entry.InActiveSyncGroup = vm.IsPowered;
                vm.InSyncGroup = vm.IsPowered;
                _settings.NotifyChanged();
                // LMS-Power wecken/abdrehen erleichtert das Verhalten.
                _ = _lms.SetPowerAsync(vm.PlayerId, vm.IsPowered);
                if (_pipeline.IsStreaming)
                {
                    if (vm.IsPowered)
                    {
                        var url = _pipeline.StreamUrl?.ToString();
                        if (!string.IsNullOrEmpty(url))
                            _ = PlayPlayerWithCurrentVolumeAsync(vm.PlayerId, url);
                    }
                    else
                    {
                        _ = _lms.StopAsync(vm.PlayerId);
                    }
                }
                _bus.RaisePlayerChanged(new PlayerChangedEventArgs
                {
                    PlayerId = vm.PlayerId,
                    Kind = PlayerChangeKind.Power,
                    Powered = vm.IsPowered,
                });
                break;
            case nameof(PlayerListItemViewModel.EffectiveVolume):
                // Slider repräsentiert die direkte LMS-Lautstärke (identisch zur Settings-Seite).
                _ = _volume.SetTrimAsync(vm.PlayerId, vm.EffectiveVolume);
                _bus.RaisePlayerChanged(new PlayerChangedEventArgs
                {
                    PlayerId = vm.PlayerId,
                    Kind = PlayerChangeKind.Volume,
                    Volume = vm.EffectiveVolume,
                });
                break;
        }
    }

    private void HandleDlnaItemChanged(PlayerListItemViewModel vm, string? propertyName)
    {
        const string prefix = "dlna:";
        if (!vm.PlayerId.StartsWith(prefix, StringComparison.Ordinal)) return;
        var udn = vm.PlayerId[prefix.Length..];
        var dlna = _settings.Current.DlnaRenderers.FirstOrDefault(d => d.Udn == udn);
        if (dlna is null) return;

        if (propertyName == nameof(PlayerListItemViewModel.EffectiveVolume))
        {
            dlna.VolumePercent = vm.EffectiveVolume;
            _settings.NotifyChanged();
        }
        else if (propertyName == nameof(PlayerListItemViewModel.IsPowered))
        {
            // Lautsprecher-Geste = „spielt auf diesem Renderer". IsEnabled (Sichtbarkeit)
            // bleibt unberührt — sonst verschwindet die Karte beim Aus-Klick aus dem Mini.
            dlna.IsPlayActive = vm.IsPowered;
            _settings.NotifyChanged();
            _bus.RaisePlayerChanged(new PlayerChangedEventArgs
            {
                PlayerId = vm.PlayerId,
                Kind = PlayerChangeKind.Power,
                Powered = vm.IsPowered,
            });
        }
    }

    private void HandleAirPlayItemChanged(PlayerListItemViewModel vm, string? propertyName)
    {
        const string prefix = "airplay:";
        if (!vm.PlayerId.StartsWith(prefix, StringComparison.Ordinal)) return;
        var apId = vm.PlayerId[prefix.Length..];
        var ap = _settings.Current.AirPlayDevices.FirstOrDefault(a => a.Id == apId);
        if (ap is null) return;

        if (propertyName == nameof(PlayerListItemViewModel.EffectiveVolume))
        {
            ap.VolumePercent = vm.EffectiveVolume;
            _settings.NotifyChanged();
        }
        else if (propertyName == nameof(PlayerListItemViewModel.IsPowered))
        {
            ap.IsPlayActive = vm.IsPowered;
            _settings.NotifyChanged();
            _bus.RaisePlayerChanged(new PlayerChangedEventArgs
            {
                PlayerId = vm.PlayerId,
                Kind = PlayerChangeKind.Power,
                Powered = vm.IsPowered,
            });
        }
    }

    private async Task ApplySyncGroupToLmsAsync(PlayerListItemViewModel changed)
    {
        var settings = _settings.Current;
        var members = settings.Players.Where(p => p.InActiveSyncGroup).ToList();
        try
        {
            if (changed.InSyncGroup)
            {
                if (members.Count <= 1) return;
                var master = members[0];
                if (changed.PlayerId == master.Id) return;
                await _lms.SyncAsync(master.Id, changed.PlayerId).ConfigureAwait(false);
            }
            else
            {
                await _lms.UnsyncAsync(changed.PlayerId).ConfigureAwait(false);
            }
        }
        catch (Exception ex) { AppLogger.Write(ex); }
    }

    private async Task PlayPlayerWithCurrentVolumeAsync(string playerId, string url)
    {
        try
        {
            var settings = _settings.Current;
            var entry = settings.Players.FirstOrDefault(p => p.Id == playerId);
            if (entry is not null)
                await _volume.SetTrimAsync(playerId, entry.TrimPercent).ConfigureAwait(false);

            await _lms.PlayUrlAsync(playerId, url).ConfigureAwait(false);
        }
        catch (Exception ex) { AppLogger.Write(ex); }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        App.Instance?.ShowSettingsWindow();
    }

    // Auto-Hide bei Fokus-Verlust ist bewusst RAUS. Das Mini-Fenster bleibt grundsätzlich
    // offen, bis der User auf X klickt oder es in den Allgemein-Einstellungen aus macht.
    // OnActivated bleibt nur als no-op-Hook, falls in Zukunft was anderes daran soll.
    private void OnActivated(object sender, WindowActivatedEventArgs e) { }

    private void PinToggle_Click(object sender, RoutedEventArgs e)
    {
        _pinned = !_pinned;
        // Pin = Always-on-top. Sofort am Presenter durchstellen.
        if (_presenter is not null) _presenter.IsAlwaysOnTop = _pinned;
        UpdatePinIcon();
        PersistSticky(_pinned);
    }

    /// <summary>Pin-/Unpin-Glyph je nach Zustand. E718 = gefüllter Pin (fixiert),
    /// E77A = Pin mit Strich (nicht fixiert). Beide dezent mit reduzierter Opazität,
    /// damit der Button nicht wie ein Accent-Element heraussticht.</summary>
    private void UpdatePinIcon()
    {
        if (PinIcon is null) return;
        if (_pinned)
        {
            PinIcon.Glyph = ""; // Pinned (gefuellt, leicht angewinkelt)
            PinIcon.Opacity = 1.0;
            PinToggle.SetValue(Microsoft.UI.Xaml.Controls.ToolTipService.ToolTipProperty, "Vordergrund-Fixierung lösen");
        }
        else
        {
            PinIcon.Glyph = ""; // Unpin (durchgestrichen)
            PinIcon.Opacity = 0.7;
            PinToggle.SetValue(Microsoft.UI.Xaml.Controls.ToolTipService.ToolTipProperty, "immer im Vordergrund halten");
        }
    }

    /// <summary>Wird von außen (App.ShowQuickPopupAsync) aufgerufen, wenn der Sticky-Status
    /// extern (z.B. via Allgemein-Einstellungen) gewechselt wurde. Holt den aktuellen Wert
    /// aus den Settings und spiegelt ihn auf UI + internes Flag.</summary>
    public void SyncStickyFromSettings()
    {
        _pinned = _settings.Current.QuickPopupSticky;
        if (_presenter is not null) _presenter.IsAlwaysOnTop = _pinned;
        UpdatePinIcon();
    }

    private void PersistSticky(bool value)
    {
        try
        {
            if (_settings.Current.QuickPopupSticky == value) return;
            _settings.Current.QuickPopupSticky = value;
            _settings.NotifyChanged();
            App.Instance?.NotifyQuickPopupVisibilityChanged();
        }
        catch { /* nicht-kritisch */ }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _pipeline.StreamingChanged -= OnStreamingChanged;
        _bus.PlayerChanged -= OnBusPlayerChanged;
        _settings.Saved -= OnSettingsSaved;
        Activated -= OnActivated;
        Closed -= OnClosed;
        foreach (var item in _players) item.PropertyChanged -= OnPlayerItemChanged;
    }
}
