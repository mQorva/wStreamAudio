using System.ComponentModel;
using System.Runtime.CompilerServices;
using wStreamAudio.Core.Models;

namespace wStreamAudio.ViewModels;

public sealed class PlayerListItemViewModel : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private string _statusText = string.Empty;
    private bool _isConnected;
    private bool _isPowered;
    private bool _inSyncGroup;
    private int _effectiveVolume;

    public required string PlayerId { get; init; }
    public PlayerKind Kind { get; set; } = PlayerKind.Squeeze;

    public string DisplayName { get => _displayName; set => Set(ref _displayName, value); }
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public bool IsConnected { get => _isConnected; set => Set(ref _isConnected, value); }
    public bool IsPowered
    {
        get => _isPowered;
        set
        {
            if (Set(ref _isPowered, value))
            {
                // Bei IsPowered-Wechsel auch das berechnete Power-Icon neu raussignalisieren,
                // damit das Mini-Fenster sofort Lautsprecher ↔ durchgestrichener Lautsprecher wechselt.
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PowerGlyph)));
            }
        }
    }
    public bool InSyncGroup { get => _inSyncGroup; set => Set(ref _inSyncGroup, value); }
    public int EffectiveVolume { get => _effectiveVolume; set => Set(ref _effectiveVolume, value); }

    /// <summary>Segoe-Fluent-Icon: Lautsprecher (E767) oder „Mute / durchgestrichen" (E74F).</summary>
    public string PowerGlyph => _isPowered ? "" : "";

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
