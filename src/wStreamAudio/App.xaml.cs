using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Core.Settings;
using wStreamAudio.Infrastructure.Logging;
using wStreamAudio.Services;
using wStreamAudio.Tray;
using wStreamAudio.Views;

namespace wStreamAudio;

public sealed partial class App : Application
{
    public static App? Instance { get; private set; }
    private ServiceProvider? _services;
    private TrayIconController? _tray;
    private QuickPopupWindow? _popup;
    private SettingsWindow? _settingsWindow;

    /// <summary>Wird von Picker-/Dialog-Code in den Settings-Pages benötigt, um an das Owner-HWND zu kommen.</summary>
    public Window? CurrentSettingsWindow => _settingsWindow;

    /// <summary>
    /// Wendet das aktuelle Theme aus den Settings auf alle bekannten geöffneten Fenster an.
    /// Wird vom Theme-Setting-Handler aufgerufen, wenn der User Hell/Dunkel/System wechselt.
    /// </summary>
    public void ApplyThemeToAllWindows()
    {
        if (_services is null) return;
        var theme = _services.GetService<ISettingsService>()?.Current.General.Theme ?? AppTheme.System;
        ThemeService.ApplyTo(_settingsWindow, theme);
        ThemeService.ApplyTo(_popup, theme);
    }
    private DispatcherQueue? _uiQueue;

    public IServiceProvider Services => _services ?? throw new InvalidOperationException("DI noch nicht initialisiert.");
    public DispatcherQueue UiQueue => _uiQueue ?? throw new InvalidOperationException("UI-Dispatcher noch nicht verfügbar.");

    public App()
    {
        Instance = this;
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            AppLogger.Write(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                AppLogger.Write(ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Write(args.Exception);
            args.SetObserved();
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            AppLogger.WriteMessage("OnLaunched: start");
            _uiQueue = DispatcherQueue.GetForCurrentThread();
            _services = ServiceConfigurator.Build();

            var profile = _services.GetRequiredService<IAppProfile>();
            AppLogger.Configure(profile.AppName, profile.DataFolderName);

            var single = _services.GetRequiredService<ISingleInstance>();
            if (!single.IsFirstInstance)
            {
                AppLogger.WriteMessage("OnLaunched: zweite Instanz — versuche laufende zu signalisieren");
                var delivered = await single.SignalRunningInstanceAsync("show-settings").ConfigureAwait(false);
                if (delivered)
                {
                    // Sichtbares Feedback liefert die laufende Instanz selbst — sie hat auf das
                    // Signal hin das Einstellungsfenster geöffnet. Kein Bedarf für eine zusätzliche Box.
                    AppLogger.WriteMessage("OnLaunched: Signal angekommen — Einstellungen hochgefahren, diese Instanz beendet sich");
                    await DisposeServicesAsync().ConfigureAwait(false);
                    RequestExitOnUiThread();
                    return;
                }
                // Laufende Instanz reagiert nicht (Zombie/hängt) — wir übernehmen statt zu verschwinden.
                AppLogger.WriteMessage("OnLaunched: laufende Instanz nicht erreichbar — übernehme als neue Instanz");
            }

            await single.StartListeningAsync().ConfigureAwait(false);
            single.CommandReceived += OnSecondInstanceCommand;

            var settings = _services.GetRequiredService<ISettingsService>();
            var model = await settings.LoadAsync().ConfigureAwait(false);

            // UI-Sprache global aktivieren (Strings reagiert auf Wechsel via Event).
            Localization.Strings.SetLanguage(model.General.LanguageCode);

            // LMS-Client mit gespeicherter Konfiguration vorbereiten.
            var lms = _services.GetRequiredService<ILmsClient>();
            try { lms.Configure(model.Lms.Host, model.Lms.Port); }
            catch (Exception ex) { AppLogger.Write(ex); }

            // TaskbarIcon ist ein FrameworkElement und MUSS auf dem UI-Thread konstruiert werden.
            // Durch die vorherigen ConfigureAwait(false) sind wir aktuell auf einem Threadpool-Thread —
            // also zurück auf den UI-Dispatcher.
            var tcs = new TaskCompletionSource();
            _uiQueue!.TryEnqueue(() =>
            {
                try
                {
                    _tray = ActivatorUtilities.CreateInstance<TrayIconController>(_services!);
                    AppLogger.WriteMessage("OnLaunched: tray erstellt");

                    if (!model.General.LaunchMinimizedToTray)
                    {
                        ShowSettingsWindow();
                    }

                    // Mini-Fenster automatisch wieder hochfahren, wenn es zuletzt offen war.
                    // QuickPopupSticky (= immer im Vordergrund) ist davon getrennt.
                    if (model.QuickPopupOpen)
                    {
                        _ = ShowQuickPopupAsync();
                    }
                }
                catch (Exception ex) { AppLogger.Write(ex); }
                finally { tcs.TrySetResult(); }
            });
            await tcs.Task.ConfigureAwait(false);

            // Beim letzten Beenden lief der Stream und der User wünscht Resume → wieder anwerfen.
            // WICHTIG: Auf dem UI-Thread starten, nicht via Task.Run. Sonst läuft die
            // WASAPI/COM-Initialisierung im MTA, später eingehende Stop/Start-Aufrufe vom
            // UI-Thread (STA) kollidieren am IMMDevice (E_NOINTERFACE auf IPropertyStore).
            if (model.General.ResumePlaybackOnStart && model.WasStreamingAtExit)
            {
                AppLogger.WriteMessage("OnLaunched: resume — Stream war beim letzten Beenden aktiv");
                _uiQueue!.TryEnqueue(() =>
                {
                    var pipeline = _services.GetService<Services.StreamPipelineCoordinator>();
                    if (pipeline is null) return;
                    _ = pipeline.StartAsync().ContinueWith(
                        t => { if (t.Exception is not null) AppLogger.Write(t.Exception); },
                        TaskScheduler.Default);
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Write(ex);
            await DisposeServicesAsync().ConfigureAwait(false);
            RequestExitOnUiThread();
        }
    }

    public void RequestExitOnUiThread()
    {
        var queue = _uiQueue;
        if (queue is not null)
        {
            queue.TryEnqueue(() =>
            {
                try { Exit(); } catch { /* Exit kann beim App-Abbau bereits laufen. */ }
            });
            return;
        }

        try { Exit(); } catch { /* Exit kann beim App-Abbau bereits laufen. */ }
    }

    private async Task DisposeServicesAsync()
    {
        var services = _services;
        _services = null;
        if (services is not null)
        {
            try { await services.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { AppLogger.Write(ex); }
        }
    }

    private void OnSecondInstanceCommand(object? sender, string command)
    {
        if (string.Equals(command, "show-settings", StringComparison.OrdinalIgnoreCase))
        {
            ShowSettingsWindow();
            return;
        }

        if (string.Equals(command, "show-popup", StringComparison.OrdinalIgnoreCase))
        {
            _ = ShowQuickPopupAsync();
        }
    }

    /// <summary>True wenn das Mini-Fenster aktuell sichtbar ist.</summary>
    public bool IsQuickPopupVisible => _popup?.AppWindow?.IsVisible == true;

    /// <summary>
    /// Wird gefeuert, wann immer sich der Sichtbarkeits-/Sticky-Status des Mini-Fensters
    /// ändert. Das Tray-Menü hängt sich daran, um den Haken am „Mini-Fenster"-Eintrag
    /// sofort zu aktualisieren — kein Polling, kein Wartezeit.
    /// </summary>
    public event EventHandler? QuickPopupVisibilityChanged;
    public void NotifyQuickPopupVisibilityChanged()
        => QuickPopupVisibilityChanged?.Invoke(this, EventArgs.Empty);

    public void HideQuickPopup()
    {
        if (_uiQueue is null) return;
        _uiQueue.TryEnqueue(() =>
        {
            try { _popup?.AppWindow?.Hide(); } catch { }
            try
            {
                var settings = _services?.GetService<ISettingsService>();
                if (settings is not null)
                {
                    settings.Current.QuickPopupOpen = false;
                    settings.NotifyChanged();
                }
            }
            catch { /* nicht-kritisch */ }
            NotifyQuickPopupVisibilityChanged();
        });
    }

    private AboutWindow? _aboutWindow;
    public void ShowAboutWindow()
    {
        if (_uiQueue is null) return;
        _uiQueue.TryEnqueue(() =>
        {
            try
            {
                if (_aboutWindow is null)
                {
                    _aboutWindow = new AboutWindow();
                    _aboutWindow.Closed += (_, _) => _aboutWindow = null;
                }
                _aboutWindow.Activate();
            }
            catch (Exception ex) { AppLogger.Write(ex); }
        });
    }

    public Task ShowQuickPopupAsync()
    {
        if (_services is null || _uiQueue is null) return Task.CompletedTask;
        _uiQueue.TryEnqueue(() =>
        {
            try
            {
                _popup ??= ActivatorUtilities.CreateInstance<QuickPopupWindow>(_services);
                ApplyThemeToAllWindows();
                // Sticky-Status aus den Settings auf das (evtl. bereits existierende) Popup ziehen,
                // damit eine externe Änderung — z.B. Toggle in Allgemein — auch ankommt.
                _popup.SyncStickyFromSettings();
                _popup.ShowAtTray();
            }
            catch (Exception ex) { AppLogger.Write(ex); }
        });
        return Task.CompletedTask;
    }

    public void ShowSettingsWindow()
    {
        if (_services is null || _uiQueue is null) return;
        _uiQueue.TryEnqueue(() =>
        {
            try
            {
                AppLogger.WriteMessage("ShowSettingsWindow: action start");
                if (_settingsWindow is null)
                {
                    _settingsWindow = ActivatorUtilities.CreateInstance<SettingsWindow>(_services);
                    AppLogger.WriteMessage("ShowSettingsWindow: SettingsWindow erzeugt");
                    _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                }
                ApplyThemeToAllWindows();
                AppLogger.WriteMessage("ShowSettingsWindow: theme angewendet");
                _settingsWindow.Activate();
                AppLogger.WriteMessage("ShowSettingsWindow: aktiviert");
            }
            catch (Exception ex)
            {
                AppLogger.Write(ex);
            }
        });
    }

    public async Task ShutdownAsync()
    {
        try
        {
            // Fenster MÜSSEN auf dem UI-Thread geschlossen werden — sonst wirft Close()
            // und der Mini-Player bleibt als Geist stehen. Wenn wir bereits auf dem
            // UI-Thread sind, direkt ausführen; sonst per Dispatcher marshallen.
            await CloseOwnedWindowsOnUiAsync().ConfigureAwait(false);

            if (_services is not null)
            {
                var pipeline = _services.GetService<Services.StreamPipelineCoordinator>();
                var settings = _services.GetService<ISettingsService>();
                // Streaming-Zustand merken, BEVOR die Pipeline disposed wird (IsStreaming wird beim
                // Stop auf false geflippt). Wird beim nächsten Start für "Wiedergabe fortsetzen" gelesen.
                if (settings is not null)
                {
                    try { settings.Current.WasStreamingAtExit = pipeline?.IsStreaming == true; }
                    catch { /* nicht-kritisch */ }
                }
                if (pipeline is not null) await pipeline.DisposeAsync().ConfigureAwait(false);

                if (settings is not null) await settings.SaveAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex) { AppLogger.Write(ex); }

        _tray?.Dispose();
        await DisposeServicesAsync().ConfigureAwait(false);
    }

    /// <summary>Schließt Mini, About und Settings auf dem UI-Thread. Awaitet, bis fertig.</summary>
    private Task CloseOwnedWindowsOnUiAsync()
    {
        if (_uiQueue is null) return Task.CompletedTask;
        var tcs = new TaskCompletionSource();
        bool enqueued = _uiQueue.TryEnqueue(() =>
        {
            try { _settingsWindow?.CapturePlacementForShutdown(); } catch { /* ignore */ }
            try { _popup?.Close(); } catch { /* ignore */ }
            try { _aboutWindow?.Close(); } catch { /* ignore */ }
            try { _settingsWindow?.Close(); } catch { /* ignore */ }
            tcs.TrySetResult();
        });
        if (!enqueued) tcs.TrySetResult();
        return tcs.Task;
    }
}
