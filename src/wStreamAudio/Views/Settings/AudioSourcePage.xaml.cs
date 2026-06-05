using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Core.Models;

namespace wStreamAudio.Views.Settings;

public sealed partial class AudioSourcePage : Page
{
    private readonly ISettingsService _settings;
    private readonly IAudioEndpointCatalog _catalog;

    public AudioSourcePage(ISettingsService settings, IAudioEndpointCatalog catalog)
    {
        _settings = settings;
        _catalog = catalog;
        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        var active = _settings.Current.ActiveCaptureProfileId;
        var items = _settings.Current.CaptureProfiles
            .Select(p => new CaptureProfileListItem
            {
                Profile = p,
                Name = p.Name,
                ModeText = p.Mode == CaptureMode.EndpointLoopback ? "Endpoint-Loopback" : "Per-App-Capture",
                IsActive = p.Id == active,
            })
            .ToList();
        ProfilesList.ItemsSource = items;
        if (active is not null)
        {
            ProfilesList.SelectedItem = items.FirstOrDefault(i => i.Profile.Id == active);
        }
    }

    private CaptureProfile? SelectedProfile()
        => (ProfilesList.SelectedItem as CaptureProfileListItem)?.Profile;

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection only — Bearbeiten erfolgt explizit über Button oder Doppelklick.
    }

    private async void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        // Häufiger Fall ist Endpoint-Loopback mit System-Default. Im Editor lässt sich
        // jederzeit auf Per-App-Capture umschalten — daher ein einziger "Neu"-Button.
        var defaultEp = _catalog.GetDefaultRenderEndpoint();
        var p = new CaptureProfile
        {
            Name = defaultEp?.DisplayName ?? "Default Speakers",
            Mode = CaptureMode.EndpointLoopback,
            FollowDefaultEndpoint = true,
            EndpointId = defaultEp?.Id,
            EndpointDisplayName = defaultEp?.DisplayName,
            ProcessMode = ProcessLoopbackMode.Include
        };
        if (await EditAsync(p))
        {
            _settings.Current.CaptureProfiles.Add(p);
            if (_settings.Current.ActiveCaptureProfileId is null)
                _settings.Current.ActiveCaptureProfileId = p.Id;
            _settings.NotifyChanged();
            Refresh();
            ProfilesList.SelectedItem = p;
        }
    }

    private async void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile() is not { } p) return;
        if (await EditAsync(p))
        {
            _settings.NotifyChanged();
            Refresh();
        }
    }

    private async void ProfilesList_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (SelectedProfile() is not { } p) return;
        if (await EditAsync(p))
        {
            _settings.NotifyChanged();
            Refresh();
        }
    }

    private async Task<bool> EditAsync(CaptureProfile profile)
    {
        var dlg = new CaptureProfileEditDialog(profile, _catalog) { XamlRoot = XamlRoot };
        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile() is not { } p) return;
        _settings.Current.CaptureProfiles.Remove(p);
        if (_settings.Current.ActiveCaptureProfileId == p.Id)
        {
            _settings.Current.ActiveCaptureProfileId = _settings.Current.CaptureProfiles.FirstOrDefault()?.Id;
        }
        _settings.NotifyChanged();
        Refresh();
    }

    private void SetDefault_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile() is not { } p) return;
        _settings.Current.ActiveCaptureProfileId = p.Id;
        _settings.NotifyChanged();
        Refresh();
    }
}

public sealed class CaptureProfileListItem
{
    public required CaptureProfile Profile { get; init; }
    public required string Name { get; init; }
    public required string ModeText { get; init; }
    public bool IsActive { get; init; }
    public string Marker => IsActive ? "★" : string.Empty;
}
