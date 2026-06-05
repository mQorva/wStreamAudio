using Microsoft.Win32;
using wStreamAudio.Core.Abstractions;

namespace wStreamAudio.Infrastructure.Autostart;

public sealed class WindowsAutostartService : IAutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly IAppProfile _profile;

    public WindowsAutostartService(IAppProfile profile) { _profile = profile; }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(_profile.AutostartRegistryValueName) is not null;
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key is null) return;

        if (enabled)
        {
            var exe = Environment.ProcessPath ?? AppContext.BaseDirectory;
            key.SetValue(_profile.AutostartRegistryValueName, $"\"{exe}\" --tray", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(_profile.AutostartRegistryValueName, throwOnMissingValue: false);
        }
    }
}
