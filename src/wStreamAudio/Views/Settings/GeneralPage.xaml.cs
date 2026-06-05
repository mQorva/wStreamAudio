using Microsoft.UI.Xaml.Controls;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Core.Settings;
using wStreamAudio.Localization;

namespace wStreamAudio.Views.Settings;

public sealed partial class GeneralPage : Page
{
    private readonly ISettingsService _settings;
    private readonly IAutostartService _autostart;
    private bool _suppress;

    public GeneralPage(ISettingsService settings, IAutostartService autostart)
    {
        _settings = settings;
        _autostart = autostart;
        InitializeComponent();
        ApplyTexts();
        Strings.LanguageChanged += OnLanguageChanged;
        // Mini-Fenster-Sichtbarkeit kann auch vom Tray oder vom Popup selbst gewechselt werden;
        // wir lauschen am Sichtbarkeits-Event der App, damit der Toggle live mitläuft.
        if (App.Instance is not null) App.Instance.QuickPopupVisibilityChanged += OnQuickPopupVisibilityChanged;
        Unloaded += (_, _) =>
        {
            Strings.LanguageChanged -= OnLanguageChanged;
            if (App.Instance is not null) App.Instance.QuickPopupVisibilityChanged -= OnQuickPopupVisibilityChanged;
        };
        Load();
    }

    private void OnQuickPopupVisibilityChanged(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(SyncMiniWindowToggle);

    private void SyncMiniWindowToggle()
    {
        _suppress = true;
        try { MiniWindowToggle.IsOn = App.Instance?.IsQuickPopupVisible == true; }
        finally { _suppress = false; }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(ApplyTexts);

    private void ApplyTexts()
    {
        AutostartCard.Header = Strings.GenAutostartHeader;
        AutostartCard.Description = Strings.GenAutostartDesc;
        LaunchHiddenCard.Header = Strings.GenLaunchHiddenHeader;
        LaunchHiddenCard.Description = Strings.GenLaunchHiddenDesc;
        ResumePlaybackCard.Header = Strings.GenResumePlaybackHeader;
        ResumePlaybackCard.Description = Strings.GenResumePlaybackDesc;
        ResumePlaybackToggle.OnContent = Strings.On;
        ResumePlaybackToggle.OffContent = Strings.Off;
        MiniWindowCard.Header = Strings.GenMiniWindowHeader;
        MiniWindowCard.Description = Strings.GenMiniWindowDesc;
        MiniWindowToggle.OnContent = Strings.On;
        MiniWindowToggle.OffContent = Strings.Off;
        AutoActivateCard.Header = Strings.GenAutoActivateHeader;
        AutoActivateCard.Description = Strings.GenAutoActivateDesc;
        AutoActivateToggle.OnContent = Strings.On;
        AutoActivateToggle.OffContent = Strings.Off;
        ThemeCard.Header = Strings.GenThemeHeader;
        ThemeCard.Description = Strings.GenThemeDesc;
        ThemeSystemItem.Content = Strings.GenThemeSystem;
        ThemeLightItem.Content = Strings.GenThemeLight;
        ThemeDarkItem.Content = Strings.GenThemeDark;
        LanguageCard.Header = Strings.GenLanguageHeader;
        LanguageCard.Description = Strings.GenLanguageDesc;
        LangDeItem.Content = Strings.GenLanguageDe;
        LangEnItem.Content = Strings.GenLanguageEn;

        AutostartToggle.OnContent = Strings.On;
        AutostartToggle.OffContent = Strings.Off;
        LaunchHiddenToggle.OnContent = Strings.On;
        LaunchHiddenToggle.OffContent = Strings.Off;
    }

    private void Load()
    {
        _suppress = true;
        var s = _settings.Current.General;
        AutostartToggle.IsOn = _autostart.IsEnabled();
        LaunchHiddenToggle.IsOn = s.LaunchMinimizedToTray;
        ResumePlaybackToggle.IsOn = s.ResumePlaybackOnStart;
        MiniWindowToggle.IsOn = App.Instance?.IsQuickPopupVisible == true;
        AutoActivateToggle.IsOn = s.AutoActivateNewDevices;
        ThemeBox.SelectedIndex = s.Theme switch
        {
            AppTheme.Light => 1,
            AppTheme.Dark => 2,
            _ => 0
        };
        LanguageBox.SelectedIndex = string.Equals(s.LanguageCode, "en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _suppress = false;
    }

    private void AutostartToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_suppress) return;
        _autostart.SetEnabled(AutostartToggle.IsOn);
        _settings.Current.General.Autostart = AutostartToggle.IsOn;
        _settings.NotifyChanged();
    }

    private void LaunchHiddenToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_suppress) return;
        _settings.Current.General.LaunchMinimizedToTray = LaunchHiddenToggle.IsOn;
        _settings.NotifyChanged();
    }

    private void ResumePlaybackToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_suppress) return;
        _settings.Current.General.ResumePlaybackOnStart = ResumePlaybackToggle.IsOn;
        _settings.NotifyChanged();
    }

    private void AutoActivateToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_suppress) return;
        _settings.Current.General.AutoActivateNewDevices = AutoActivateToggle.IsOn;
        _settings.NotifyChanged();
    }

    private void MiniWindowToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_suppress) return;
        var app = App.Instance;
        if (app is null) return;
        // Toggle steuert nur die Sichtbarkeit (QuickPopupOpen). Pin (Always-on-top) ist
        // davon getrennt und wird im Mini-Fenster selbst gesetzt. ShowAtTray/CloseButton
        // persistieren QuickPopupOpen — wir müssen es hier also nicht selbst schreiben.
        if (MiniWindowToggle.IsOn) _ = app.ShowQuickPopupAsync();
        else app.HideQuickPopup();
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        _settings.Current.General.Theme = ThemeBox.SelectedIndex switch
        {
            1 => AppTheme.Light,
            2 => AppTheme.Dark,
            _ => AppTheme.System
        };
        _settings.NotifyChanged();
        App.Instance?.ApplyThemeToAllWindows();
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        var code = LanguageBox.SelectedIndex == 1 ? "en" : "de";
        _settings.Current.General.LanguageCode = code;
        _settings.NotifyChanged();
        Strings.SetLanguage(code);
    }
}
