using Microsoft.UI.Xaml;

namespace wStreamAudio;

/// <summary>
/// Platzhalter-Hauptfenster. wStreamAudio läuft als Tray-Tool ohne sichtbares
/// Hauptfenster; dieses Window existiert nur, damit der WinUI-3-XAML-Compiler
/// einen App-Lebenszyklus-Anker hat. Es wird nie aktiviert.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
