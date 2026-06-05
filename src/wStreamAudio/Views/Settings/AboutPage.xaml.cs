using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using wStreamAudio.Core.Abstractions;

namespace wStreamAudio.Views.Settings;

public sealed partial class AboutPage : Page
{
    private readonly IAppProfile _profile;

    public AboutPage(IAppProfile profile)
    {
        _profile = profile;
        InitializeComponent();
        Load();
    }

    private void Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? asm.GetName().Version?.ToString()
                      ?? "0.0.0";
        VersionText.Text = $"{_profile.AppName} {version}";
        // Copyright als Description der Version-Card — keine separate „Autor"-Card mehr,
        // weil CopyrightText den Namen bereits enthält und die Doppelung optisch unschön war.
        VersionCard.Description = _profile.CopyrightText;
        DataPathText.Text = "Settings: " + Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            _profile.DataFolderName, "settings.json");
        LogsPathText.Text = "Logs: " + Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            _profile.DataFolderName, "logs");
    }

    private void OpenLicense_Click(object sender, RoutedEventArgs e)
        => OpenLocal("LICENSE");

    private void OpenThirdParty_Click(object sender, RoutedEventArgs e)
        => OpenLocal("THIRD_PARTY_NOTICES.md");

    private static void OpenLocal(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { /* still ignore */ }
    }
}
