using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;
using wStreamAudio.Core.Settings;

namespace wStreamAudio.Services;

/// <summary>
/// Wendet das in den Settings konfigurierte Theme zur Laufzeit auf jedes Fenster an.
/// WinUI 3 hat keinen globalen Theme-Switch — wir setzen <see cref="FrameworkElement.RequestedTheme"/>
/// auf der jeweiligen Window-Root und färben zusätzlich die System-Caption-Buttons der Titelzeile
/// (Min/Max/Close), die WinUI 3 sonst nicht automatisch ans Theme anpasst.
/// </summary>
public static class ThemeService
{
    public static void ApplyTo(Window? window, AppTheme theme)
    {
        if (window is null) return;
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = ToElementTheme(theme);
        }
        ApplyCaptionButtonColors(window, ResolveEffectiveTheme(theme));
    }

    public static ElementTheme ToElementTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>System-Caption-Buttons (Min/Max/Close) im rechten Titelzeilen-Bereich
    /// passend zum aktiven Theme einfärben. Ohne das bleiben sie bei custom title bar
    /// weiß und werden auf hellem Hintergrund unsichtbar.</summary>
    private static void ApplyCaptionButtonColors(Window window, ElementTheme effectiveTheme)
    {
        try
        {
            var tb = window.AppWindow?.TitleBar;
            if (tb is null) return;

            tb.ButtonBackgroundColor = Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Colors.Transparent;

            if (effectiveTheme == ElementTheme.Dark)
            {
                tb.ButtonForegroundColor = Colors.White;
                tb.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9A);
                tb.ButtonHoverBackgroundColor = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
                tb.ButtonHoverForegroundColor = Colors.White;
                tb.ButtonPressedBackgroundColor = Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF);
                tb.ButtonPressedForegroundColor = Colors.White;
            }
            else
            {
                tb.ButtonForegroundColor = Colors.Black;
                tb.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0x60, 0x60, 0x60);
                tb.ButtonHoverBackgroundColor = Color.FromArgb(0x22, 0x00, 0x00, 0x00);
                tb.ButtonHoverForegroundColor = Colors.Black;
                tb.ButtonPressedBackgroundColor = Color.FromArgb(0x44, 0x00, 0x00, 0x00);
                tb.ButtonPressedForegroundColor = Colors.Black;
            }
        }
        catch { /* AppWindow noch nicht bereit — wir kommen beim nächsten ApplyTo wieder rein. */ }
    }

    /// <summary>Bei "System" das tatsächlich aktive Windows-Theme ermitteln, damit wir
    /// passend einfärben können (Light/Dark ist eindeutig).</summary>
    private static ElementTheme ResolveEffectiveTheme(AppTheme theme)
    {
        if (theme == AppTheme.Light) return ElementTheme.Light;
        if (theme == AppTheme.Dark) return ElementTheme.Dark;
        try
        {
            var ui = new UISettings();
            var bg = ui.GetColorValue(UIColorType.Background);
            // Hellgrund → Light, Dunkelgrund → Dark.
            var brightness = (bg.R + bg.G + bg.B) / 3;
            return brightness > 128 ? ElementTheme.Light : ElementTheme.Dark;
        }
        catch { return ElementTheme.Light; }
    }
}
