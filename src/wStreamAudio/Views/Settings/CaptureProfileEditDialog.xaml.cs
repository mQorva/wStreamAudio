using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Core.Models;
using wStreamAudio.Infrastructure.Audio;

namespace wStreamAudio.Views.Settings;

public sealed partial class CaptureProfileEditDialog : ContentDialog
{
    private readonly CaptureProfile _profile;
    private readonly IReadOnlyList<AudioEndpointInfo> _endpoints;

    public CaptureProfileEditDialog(CaptureProfile profile, IAudioEndpointCatalog catalog)
    {
        _profile = profile;
        _endpoints = catalog.EnumerateRenderEndpoints();
        InitializeComponent();
        Load();
        PrimaryButtonClick += OnPrimary;
    }

    private void Load()
    {
        NameBox.Text = _profile.Name;

        EndpointBox.ItemsSource = _endpoints;
        if (_profile.EndpointId is { Length: > 0 } id)
        {
            var match = _endpoints.FirstOrDefault(e => e.Id == id);
            if (match is not null) EndpointBox.SelectedItem = match;
        }
        if (EndpointBox.SelectedItem is null && _endpoints.Count > 0)
        {
            EndpointBox.SelectedItem = _endpoints.FirstOrDefault(e => e.IsDefault) ?? _endpoints[0];
        }
        FollowDefaultBox.IsChecked = _profile.FollowDefaultEndpoint;

        ProcessNameBox.Text = _profile.ProcessName ?? string.Empty;
        ProcessIncludeRadio.IsChecked = _profile.ProcessMode == ProcessLoopbackMode.Include;
        ProcessExcludeRadio.IsChecked = _profile.ProcessMode == ProcessLoopbackMode.Exclude;

        ModeEndpoint.IsChecked = _profile.Mode == CaptureMode.EndpointLoopback;
        ModeProcess.IsChecked = _profile.Mode == CaptureMode.ProcessLoopback;
        ApplyModeVisibility();
        ApplyFollowDefaultEnabled();

        SampleRateBox.SelectedIndex = _profile.SampleRate switch
        {
            44100 => 1,
            48000 => 2,
            _ => 0
        };
    }

    private async void PickProcess_Click(object sender, RoutedEventArgs e)
    {
        var apps = AudioSessionEnumerator.EnumerateActive();
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            Height = 320,
            ItemTemplate = (DataTemplate)XamlReader.Load(
                "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<StackPanel Spacing=\"2\" Padding=\"4,2\">" +
                "<TextBlock Text=\"{Binding DisplayName}\" Style=\"{ThemeResource BodyStrongTextBlockStyle}\" TextTrimming=\"CharacterEllipsis\" />" +
                "<TextBlock Text=\"{Binding SubLabel}\" Style=\"{ThemeResource CaptionTextBlockStyle}\" Foreground=\"{ThemeResource TextFillColorSecondaryBrush}\" />" +
                "</StackPanel></DataTemplate>"),
            ItemsSource = apps,
        };

        var content = new StackPanel { Spacing = 8, Width = 420 };
        if (apps.Count == 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = "Keine laufenden Audio-Apps gefunden. Bitte den Prozessnamen manuell eintragen.",
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = "Eine App wählen — der Prozessname wird übernommen.",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(list);
        }

        var dlg = new ContentDialog
        {
            Title = "App auswählen",
            Content = content,
            PrimaryButtonText = "übernehmen",
            CloseButtonText = "abbrechen",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            IsPrimaryButtonEnabled = apps.Count > 0,
        };
        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary) return;
        if (list.SelectedItem is RunningAudioApp picked)
        {
            ProcessNameBox.Text = picked.ProcessName;
        }
    }

    private void Mode_Checked(object sender, RoutedEventArgs e) => ApplyModeVisibility();

    private void ApplyModeVisibility()
    {
        if (EndpointPanel is null || ProcessPanel is null) return;
        var endpoint = ModeEndpoint?.IsChecked == true;
        EndpointPanel.Visibility = endpoint ? Visibility.Visible : Visibility.Collapsed;
        ProcessPanel.Visibility = endpoint ? Visibility.Collapsed : Visibility.Visible;
    }

    private void FollowDefault_Changed(object sender, RoutedEventArgs e) => ApplyFollowDefaultEnabled();

    private void ApplyFollowDefaultEnabled()
    {
        if (EndpointBox is null || FollowDefaultBox is null) return;
        EndpointBox.IsEnabled = FollowDefaultBox.IsChecked != true;
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var error = Validate();
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            args.Cancel = true;
            return;
        }

        var endpointMode = ModeEndpoint.IsChecked == true;
        _profile.Name = NameBox.Text.Trim();
        _profile.Mode = endpointMode ? CaptureMode.EndpointLoopback : CaptureMode.ProcessLoopback;

        if (endpointMode)
        {
            _profile.FollowDefaultEndpoint = FollowDefaultBox.IsChecked == true;
            if (_profile.FollowDefaultEndpoint)
            {
                _profile.EndpointId = null;
                _profile.EndpointDisplayName = null;
            }
            else if (EndpointBox.SelectedItem is AudioEndpointInfo ep)
            {
                _profile.EndpointId = ep.Id;
                _profile.EndpointDisplayName = ep.DisplayName;
            }
            _profile.ProcessName = null;
        }
        else
        {
            _profile.ProcessName = ProcessNameBox.Text.Trim();
            _profile.ProcessMode = ProcessExcludeRadio.IsChecked == true
                ? ProcessLoopbackMode.Exclude
                : ProcessLoopbackMode.Include;
        }

        _profile.SampleRate = (SampleRateBox.SelectedItem as ComboBoxItem)?.Tag is string tag
            && int.TryParse(tag, out var sr) ? sr : 0;
    }

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            return "Bitte einen Profilnamen vergeben.";

        if (ModeEndpoint.IsChecked == true)
        {
            if (FollowDefaultBox.IsChecked != true && EndpointBox.SelectedItem is not AudioEndpointInfo)
                return "Bitte ein Wiedergabegerät auswählen oder Default folgen aktivieren.";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(ProcessNameBox.Text))
                return "Bitte einen Prozessnamen angeben (z.B. Spotify).";
        }
        return null;
    }
}
