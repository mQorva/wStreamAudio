using wStreamAudio.Core.Settings;

namespace wStreamAudio.Core.Abstractions;

public interface ISettingsService
{
    /// <summary>Liefert das aktuelle Settings-Objekt. Beim ersten Aufruf wird aus Datei geladen.</summary>
    SettingsModel Current { get; }

    /// <summary>Lädt von Disk (oder erstellt Defaults). Idempotent.</summary>
    Task<SettingsModel> LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Markiert die Settings als geändert. Speichert debounced auf Disk.
    /// In der UI sollten Two-Way-Bindings dies via INotifyPropertyChanged automatisch auslösen.
    /// </summary>
    void NotifyChanged();

    /// <summary>Sofort speichern (z.B. beim Shutdown).</summary>
    Task SaveAsync(CancellationToken ct = default);

    /// <summary>Wird gefeuert nach erfolgreichem Speichern.</summary>
    event EventHandler? Saved;
}
