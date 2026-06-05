using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace wStreamAudio.Views;

public sealed partial class AboutWindow : Window
{
    private const int LogicalWidth = 420;
    private const int LogicalHeight = 230;
    private const int MarginFromEdge = 12;

    public AboutWindow()
    {
        InitializeComponent();
        // Mica Base — passend zum Settings-Window.
        SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
        // Theme an die App-Einstellung koppeln.
        wStreamAudio.Services.ThemeService.ApplyTo(
            this,
            App.Instance?.Services.GetService<wStreamAudio.Core.Abstractions.ISettingsService>()
                ?.Current.General.Theme ?? wStreamAudio.Core.Settings.AppTheme.System);

        var asm = Assembly.GetExecutingAssembly();
        var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? asm.GetName().Version?.ToString()
                      ?? "0.0.0";
        var plus = version.IndexOf('+');
        if (plus > 0) version = version[..plus];
        var copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

        VersionText.Text = $"Version {version}";
        CopyrightText.Text = copyright;
        DescriptionText.Text =
            "System-Audio über WASAPI Loopback einfangen, als MP3 streamen und in den " +
            "Logitech Media Server (LMS) und an DLNA-Renderer schicken. Optional mit " +
            "AirPlay-Brücke. Multiroom-Sync über Squeeze-Player.";

        var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "App256.png");
        if (File.Exists(logoPath))
        {
            LogoImage.Source = new BitmapImage
            {
                UriSource = new Uri(logoPath),
                DecodePixelWidth = 112,
            };
        }

        ConfigureWindow();
    }

    private void ConfigureWindow()
    {
        // Title-Bar-Icon und Presenter direkt über AppWindow — kein P/Invoke nötig.
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        // DPI-Skalierung über XamlRoot abfragen, sobald die Content-Schicht steht.
        // RasterizationScale ist „display scale", 1.0 = 100 %.
        if (Content is FrameworkElement fe)
        {
            fe.Loaded += (_, _) => ApplyScaledLayout();
        }
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e) => Close();

    private void ApplyScaledLayout()
    {
        var scale = Content?.XamlRoot?.RasterizationScale ?? 1.0;
        var width = (int)(LogicalWidth * scale);
        var height = (int)(LogicalHeight * scale);
        var margin = (int)(MarginFromEdge * scale);

        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var work = area.WorkArea;
        var x = work.X + work.Width - width - margin;
        var y = work.Y + work.Height - height - margin;
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }
}
