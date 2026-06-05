using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Services;

namespace wStreamAudio.Views.Settings;

public sealed partial class LmsServerPage : Page
{
    private readonly ISettingsService _settings;
    private readonly ILmsClient _lms;
    private readonly StreamPipelineCoordinator _pipeline;
    private bool _suppress;

    public LmsServerPage(ISettingsService settings, ILmsClient lms, StreamPipelineCoordinator pipeline)
    {
        _settings = settings;
        _lms = lms;
        _pipeline = pipeline;
        InitializeComponent();
        Load();
        _pipeline.StreamingChanged += OnPipelineChanged;
        Unloaded += (_, _) => _pipeline.StreamingChanged -= OnPipelineChanged;
    }

    private void OnPipelineChanged(object? sender, bool isStreaming)
        => DispatcherQueue.TryEnqueue(UpdateStreamUrlBox);

    private void Load()
    {
        _suppress = true;
        var s = _settings.Current.Lms;
        var st = _settings.Current.Streaming;
        var svc = _settings.Current.Services;
        SqueezeBoxToggle.IsOn = svc.SqueezeBox;
        DlnaToggle.IsOn = svc.Dlna;
        AirPlayToggle.IsOn = svc.AirPlay;
        DlnaBufferBox.Value = svc.DlnaBufferMs;
        DlnaAutoDiscoverToggle.IsOn = svc.DlnaAutoDiscover;
        AirPlayAutoDiscoverToggle.IsOn = svc.AirPlayAutoDiscover;
        AutoDiscoverToggle.IsOn = s.AutoDiscover;
        StreamPortBox.Value = st.HttpPort;
        FirewallToggle.IsOn = st.SetFirewallRule;
        UpdateStreamUrlBox();
        HostBox.Text = s.Host;
        PortBox.Value = s.Port;
        _suppress = false;
    }

    private void DlnaBufferBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppress || double.IsNaN(args.NewValue)) return;
        _settings.Current.Services.DlnaBufferMs = (int)args.NewValue;
        _settings.NotifyChanged();
    }

    private void DlnaAutoDiscoverToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _settings.Current.Services.DlnaAutoDiscover = DlnaAutoDiscoverToggle.IsOn;
        _settings.NotifyChanged();
    }

    private void AirPlayAutoDiscoverToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _settings.Current.Services.AirPlayAutoDiscover = AirPlayAutoDiscoverToggle.IsOn;
        _settings.NotifyChanged();
    }

    private void SqueezeBoxToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _settings.Current.Services.SqueezeBox = SqueezeBoxToggle.IsOn;
        _settings.NotifyChanged();
    }

    private void DlnaToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _settings.Current.Services.Dlna = DlnaToggle.IsOn;
        _settings.NotifyChanged();
    }

    private void AirPlayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _settings.Current.Services.AirPlay = AirPlayToggle.IsOn;
        _settings.NotifyChanged();
    }

    private void AutoDiscoverToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _settings.Current.Lms.AutoDiscover = AutoDiscoverToggle.IsOn;
        _settings.NotifyChanged();
    }

    private void HostBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        _settings.Current.Lms.Host = HostBox.Text.Trim();
        _settings.NotifyChanged();
    }

    private void PortBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppress) return;
        if (double.IsNaN(args.NewValue)) return;
        _settings.Current.Lms.Port = (int)args.NewValue;
        _settings.NotifyChanged();
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        ShowResult(ResultKind.Info, "Test läuft …", "Verbindung wird geprüft.");

        try
        {
            var host = _settings.Current.Lms.Host;
            var port = _settings.Current.Lms.Port;
            var result = await _lms.TestConnectionAsync(host, port);
            if (result.Ok)
            {
                var detail = result.StatusCode is int code && code != 200
                    ? $"LMS antwortet (HTTP {code})."
                    : "LMS antwortet wie erwartet.";
                ShowResult(ResultKind.Success, "Verbunden", detail);
                try { _lms.Configure(host, port); }
                catch (Exception ex)
                {
                    ShowResult(ResultKind.Warning, "Konfiguration fehlgeschlagen", ex.Message);
                }
            }
            else
            {
                ShowResult(ResultKind.Error, "Nicht erreichbar", result.Error ?? string.Empty);
            }
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private enum ResultKind { Info, Success, Warning, Error }

    private void ShowResult(ResultKind kind, string title, string message)
    {
        TestResultPanel.Visibility = Visibility.Visible;
        TestResultTitle.Text = title;
        TestResultMessage.Text = message;

        // Dezent gefärbter Rand + leicht eingefärbter Hintergrund. Theme-konform,
        // nicht so knallig wie eine vollflächige InfoBar.
        var (bgKey, borderKey) = kind switch
        {
            ResultKind.Success => ("SystemFillColorSuccessBackgroundBrush", "SystemFillColorSuccessBrush"),
            ResultKind.Warning => ("SystemFillColorCautionBackgroundBrush", "SystemFillColorCautionBrush"),
            ResultKind.Error   => ("SystemFillColorCriticalBackgroundBrush", "SystemFillColorCriticalBrush"),
            _                  => ("CardBackgroundFillColorDefaultBrush", "CardStrokeColorDefaultBrush"),
        };
        TestResultPanel.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[bgKey];
        TestResultPanel.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[borderKey];
    }

    // ===== Stream-Server-Settings (Stream-URL, HTTP-Port, Firewall) =====

    private void UpdateStreamUrlBox()
    {
        var url = _pipeline.StreamUrl?.ToString();
        if (string.IsNullOrEmpty(url))
        {
            var port = _settings.Current.Streaming.HttpPort;
            StreamUrlBox.Text = $"(noch nicht aktiv — wird beim ersten Abspielen zu http://<dein-PC>:{port}/stream.mp3)";
        }
        else
        {
            StreamUrlBox.Text = url;
        }
    }

    private void StreamPortBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppress || double.IsNaN(args.NewValue)) return;
        _settings.Current.Streaming.HttpPort = (int)args.NewValue;
        _settings.NotifyChanged();
        UpdateStreamUrlBox();
    }

    private void FirewallToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _settings.Current.Streaming.SetFirewallRule = FirewallToggle.IsOn;
        _settings.NotifyChanged();
    }

    private void OpenStreamUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = _pipeline.StreamUrl?.ToString();
        if (string.IsNullOrEmpty(url))
        {
            _ = ShowMessageAsync("Stream-URL existiert erst, wenn der Stream einmal gestartet wurde. Klick erst auf abspielen bei einem Player, dann nochmal hier.", "Stream-URL");
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync($"Browser konnte nicht geöffnet werden: {ex.Message}", "Stream-URL");
        }
    }

    private async void TestStreamUrl_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var url = _pipeline.StreamUrl?.ToString();
        if (string.IsNullOrEmpty(url))
        {
            StreamSelfTestText.Text = "Stream noch nicht aktiv — erst auf einem Player abspielen klicken.";
            return;
        }

        btn.IsEnabled = false;
        StreamSelfTestText.Text = "teste …";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(true);
            if (resp.IsSuccessStatusCode)
            {
                using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(true);
                var buf = new byte[256];
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                int read = 0;
                try { read = await stream.ReadAsync(buf, cts.Token).ConfigureAwait(true); }
                catch (OperationCanceledException) { }
                StreamSelfTestText.Text = read > 0
                    ? $"OK — HTTP {(int)resp.StatusCode}, erste {read} Bytes empfangen."
                    : $"Antwortet mit HTTP {(int)resp.StatusCode}, aber keine Daten — Capture-Pipeline läuft nicht.";
            }
            else
            {
                StreamSelfTestText.Text = $"HTTP {(int)resp.StatusCode} — eigener Server lehnt ab.";
            }
        }
        catch (Exception ex)
        {
            StreamSelfTestText.Text = $"Eigener Server nicht erreichbar: {ex.Message}";
        }
        finally
        {
            btn.IsEnabled = true;
        }
    }

    private async Task ShowMessageAsync(string message, string title)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        };
        await dlg.ShowAsync();
    }
}
