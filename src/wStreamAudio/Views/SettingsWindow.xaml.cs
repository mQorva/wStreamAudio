using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Core.Settings;
using wStreamAudio.Infrastructure.Logging;
using wStreamAudio.Localization;
using wStreamAudio.Services;
using wStreamAudio.Views.Settings;

namespace wStreamAudio.Views;

public sealed partial class SettingsWindow : Window
{
    // Defaults: angenehm groß, aber passt auch noch auf 1366x768.
    private const int DefaultWidth = 1200;
    private const int DefaultHeight = 820;
    private const int MinWidth = 720;
    private const int MinHeight = 520;

    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private bool _placementLoaded;

    public SettingsWindow(IServiceProvider services, ISettingsService settings)
    {
        _services = services;
        _settings = settings;
        InitializeComponent();
        // Mica Base — der hellere, normale Mica-Look für App-Fenster.
        // BaseAlt wirkte deutlich dunkler als ein typisches Windows-App-Fenster.
        SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
        ApplyTexts();
        Strings.LanguageChanged += OnLanguageChanged;

        AppWindow.IsShownInSwitchers = true;

        // Custom Title-Bar im Win11-Stil: Content reicht hoch bis unter die System-Buttons,
        // unser TitleBar-Grid liefert Icon + Titel. Die Min/Max/Close-Buttons malt Windows
        // rechts auf den freigehaltenen Bereich.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        // Reservierten Bereich für die System-Buttons als Padding der rechten Spalte spiegeln,
        // damit der Titeltext nicht unter die Buttons rutscht. RightInset kommt in Pixeln —
        // wir konvertieren über die DPI-Skalierung in DIPs.
        try
        {
            UpdateTitleBarRightInset();
            TitleBar.SizeChanged += (_, _) => UpdateTitleBarRightInset();
        }
        catch (Exception ex) { AppLogger.Write(ex); }

        // Title-Bar-Icon setzen (analog Magic-Voice).
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico");
            if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        }
        catch (Exception ex) { AppLogger.Write(ex); }

        if (AppWindow.Presenter is OverlappedPresenter pres)
        {
            pres.IsMinimizable = true;
            pres.IsMaximizable = true;
            pres.PreferredMinimumWidth = MinWidth;
            pres.PreferredMinimumHeight = MinHeight;
        }

        ApplyPlacementFromSettings();
        AppWindow.Changed += OnAppWindowChanged;

        // Persistenz: OnAppWindowChanged schreibt bereits bei jeder Bewegung/Resize die
        // Lage in die Settings (debounced). Beim Schließen reicht ein letztes Capture
        // im Window.Closed — AppWindow.Closing verhakt sich in WinUI 3, wenn nicht
        // die ganze App beendet werden soll.
        Closed += OnWindowClosed;

        ThemeService.ApplyTo(this, _settings.Current.General.Theme);

        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void UpdateTitleBarRightInset()
    {
        try
        {
            var scale = TitleBar.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale <= 0) scale = 1.0;
            var insetDip = AppWindow.TitleBar.RightInset / scale;
            TitleBarRightInsetCol.Width = new GridLength(insetDip);
        }
        catch { /* TitleBar evtl. noch nicht geladen — beim nächsten SizeChanged kommen wir wieder rein. */ }
    }

    /// <summary>Titel des Hauptfensters: "wStreamAudio {Version}" — kein Sub-Suffix wie „— Einstellungen",
    /// die aktive Sektion zeigt schon der große Header im Inhalt.</summary>
    private static string BuildWindowTitle()
    {
        var asm = Assembly.GetExecutingAssembly();
        var ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? asm.GetName().Version?.ToString()
                  ?? "";
        // Plus-Suffix einer SemVer-Build-Metadata abschneiden — im Titel reicht „1.2.3".
        var plus = ver.IndexOf('+');
        if (plus > 0) ver = ver[..plus];
        return string.IsNullOrEmpty(ver) ? "wStreamAudio" : $"wStreamAudio {ver}";
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => { ApplyTexts(); RefreshActivePageHeader(); });

    private void ApplyTexts()
    {
        var title = BuildWindowTitle();
        Title = title;
        TitleBarText.Text = title;
        NavGeneral.Content = Strings.NavGeneral;
        NavAudio.Content = Strings.NavAudio;
        NavLms.Content = Strings.NavLms;
        NavStreaming.Content = Strings.NavStreaming;
        NavAbout.Content = Strings.NavAbout;
    }

    private void RefreshActivePageHeader()
    {
        if (Nav.SelectedItem is NavigationViewItem item)
            UpdateHeaderFor(item.Tag as string ?? "general");
    }

    private void UpdateHeaderFor(string tag)
    {
        HeaderText.Text = tag switch
        {
            "general" => Strings.NavGeneral,
            "audio" => Strings.NavAudio,
            "lms" => Strings.NavLms,
            "streaming" => Strings.NavStreaming,
            "about" => Strings.NavAbout,
            _ => HeaderText.Text,
        };
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        try
        {
            Strings.LanguageChanged -= OnLanguageChanged;
            AppWindow.Changed -= OnAppWindowChanged;
            Closed -= OnWindowClosed;

            // Lage wurde schon laufend in den Settings aktualisiert (siehe OnAppWindowChanged).
            // Hier nur noch fire-and-forget Save anstoßen — kein GetResult, kein Block.
            var s = _settings;
            _ = Task.Run(async () =>
            {
                try { await s.SaveAsync().ConfigureAwait(false); }
                catch (Exception ex) { AppLogger.Write(ex); }
            });

            var snapshot = _settings.Current.SettingsWindow;
            AppLogger.WriteMessage(
                $"SettingsWindow Closed — Lage zuletzt: " +
                $"{snapshot?.X},{snapshot?.Y} {snapshot?.Width}x{snapshot?.Height}");

            // Schließen des Hauptfensters = App beenden. Mini-/About-Fenster, Tray und
            // alle laufenden Streams werden über ShutdownAsync sauber abgeräumt.
            var uiQueue = App.Instance?.UiQueue;
            _ = Task.Run(async () =>
            {
                try { if (App.Instance is not null) await App.Instance.ShutdownAsync().ConfigureAwait(false); }
                catch (Exception ex) { AppLogger.Write(ex); }
                finally
                {
                    // Exit() muss auf dem UI-Thread laufen, sonst hängen die noch
                    // existierenden Fenster-Threads weiter.
                    uiQueue?.TryEnqueue(() =>
                    {
                        try { Microsoft.UI.Xaml.Application.Current.Exit(); } catch { /* schon beim Beenden */ }
                    });
                }
            });
        }
        catch (Exception ex) { AppLogger.Write(ex); }
    }

    private void ApplyPlacementFromSettings()
    {
        try
        {
            var placement = _settings.Current.SettingsWindow ?? new WindowPlacement();

            var width = placement.Width is int pw && pw >= MinWidth ? pw : DefaultWidth;
            var height = placement.Height is int ph && ph >= MinHeight ? ph : DefaultHeight;

            AppWindow.Resize(new SizeInt32(width, height));
            AppLogger.WriteMessage($"SettingsWindow: resize -> {width}x{height}");

            if (placement.X is int x && placement.Y is int y)
            {
                var desiredRect = new RectInt32(x, y, width, height);
                var workArea = MonitorWorkAreaFor(desiredRect);
                if (!IsRectSufficientlyVisible(desiredRect, workArea))
                {
                    var centered = CenteredRectWithin(workArea, width, height);
                    AppWindow.Move(new PointInt32(centered.X, centered.Y));
                    AppLogger.WriteMessage($"SettingsWindow: off-screen → zentriert auf {centered.X},{centered.Y}");
                }
                else
                {
                    var clamped = ClampRectIntoWorkArea(desiredRect, workArea);
                    AppWindow.Move(new PointInt32(clamped.X, clamped.Y));
                    AppLogger.WriteMessage($"SettingsWindow: move -> {clamped.X},{clamped.Y}");
                }
            }
            else
            {
                // Erstes Hochfahren: Fenster auf dem Primärmonitor zentrieren.
                var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
                var cx = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
                var cy = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
                AppWindow.Move(new PointInt32(cx, cy));
                AppLogger.WriteMessage($"SettingsWindow: erste Lage zentriert auf {cx},{cy}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Write(ex);
            try { AppWindow.Resize(new SizeInt32(DefaultWidth, DefaultHeight)); } catch { /* ignore */ }
        }

        _placementLoaded = true;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_placementLoaded) return;

        // Minimieren → in den Tray verstecken.
        if (sender.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized)
        {
            p.Restore();
            sender.Hide();
            AppLogger.WriteMessage("SettingsWindow: minimized → ins Tray versteckt");
            return;
        }

        if (!args.DidPositionChange && !args.DidSizeChange) return;

        CapturePlacementToSettings();
        _settings.NotifyChanged();
    }

    /// <summary>
    /// Wird beim App-Shutdown gerufen, falls das Fenster noch offen ist und das
    /// Closing-Event nicht mehr feuert.
    /// </summary>
    public void CapturePlacementForShutdown() => CapturePlacementToSettings();

    private void CapturePlacementToSettings()
    {
        // Minimierte oder unsinnige Zustände nicht persistieren.
        if (AppWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized) return;

        var size = AppWindow.Size;
        var pos = AppWindow.Position;
        if (size.Width < MinWidth || size.Height < MinHeight) return;
        if (pos.X < -10000 || pos.Y < -10000) return;

        var placement = _settings.Current.SettingsWindow ??= new WindowPlacement();
        placement.X = pos.X;
        placement.Y = pos.Y;
        placement.Width = size.Width;
        placement.Height = size.Height;
    }

    // ===== Monitor-Clamping (Magic-Voice-Pattern) =====

    private static RectInt32 CenteredRectWithin(RectInt32 workArea, int width, int height)
    {
        var w = Math.Min(width, workArea.Width);
        var h = Math.Min(height, workArea.Height);
        var x = workArea.X + Math.Max(0, (workArea.Width - w) / 2);
        var y = workArea.Y + Math.Max(0, (workArea.Height - h) / 2);
        return new RectInt32(x, y, w, h);
    }

    private static RectInt32 ClampRectIntoWorkArea(RectInt32 rect, RectInt32 workArea)
    {
        const int minVisibleX = 80;
        const int minVisibleY = 40;
        var maxX = workArea.X + Math.Max(0, workArea.Width - minVisibleX);
        var maxY = workArea.Y + Math.Max(0, workArea.Height - minVisibleY);
        var minX = workArea.X - Math.Max(0, rect.Width - minVisibleX);
        var minY = workArea.Y;
        var x = Math.Clamp(rect.X, minX, maxX);
        var y = Math.Clamp(rect.Y, minY, maxY);
        return new RectInt32(x, y, rect.Width, rect.Height);
    }

    private static bool IsRectSufficientlyVisible(RectInt32 rect, RectInt32 workArea)
    {
        var left = Math.Max(rect.X, workArea.X);
        var top = Math.Max(rect.Y, workArea.Y);
        var right = Math.Min(rect.X + rect.Width, workArea.X + workArea.Width);
        var bottom = Math.Min(rect.Y + rect.Height, workArea.Y + workArea.Height);
        var w = Math.Max(0, right - left);
        var h = Math.Max(0, bottom - top);
        return w >= 120 && h >= 80;
    }

    private static RectInt32 MonitorWorkAreaFor(RectInt32 rect)
    {
        // WinAppSDK-Variante des klassischen MonitorFromRect/GetMonitorInfo — fällt automatisch
        // auf den Primärmonitor zurück, wenn das Rect nirgendwo sichtbar ist.
        var display = DisplayArea.GetFromRect(rect, DisplayAreaFallback.Nearest)
                      ?? DisplayArea.Primary;
        return display.WorkArea;
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        var tag = item.Tag as string ?? "general";

        switch (tag)
        {
            case "general":
                ContentFrame.Content = ActivatorUtilities.CreateInstance<GeneralPage>(_services);
                break;
            case "audio":
                ContentFrame.Content = ActivatorUtilities.CreateInstance<AudioSourcePage>(_services);
                break;
            case "lms":
                ContentFrame.Content = ActivatorUtilities.CreateInstance<LmsServerPage>(_services);
                break;
            case "streaming":
                ContentFrame.Content = ActivatorUtilities.CreateInstance<StreamingPage>(_services);
                break;
            case "about":
                ContentFrame.Content = ActivatorUtilities.CreateInstance<AboutPage>(_services);
                break;
        }
        UpdateHeaderFor(tag);
    }
}
