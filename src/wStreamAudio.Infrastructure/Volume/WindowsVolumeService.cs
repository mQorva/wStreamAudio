using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Core.Settings;
using wStreamAudio.Core.Volume;

namespace wStreamAudio.Infrastructure.Volume;

/// <summary>
/// Beobachtet die System-Lautstärke des Default-Render-Endpoints und
/// rechnet pro Player den persistierten Trim auf eine LMS-Lautstärke hoch.
/// LMS-direkte Lautstärke-Änderungen werden vom <see cref="ILmsClient"/>
/// reportet; daraus rechnet der Service den Trim zurück.
/// </summary>
public sealed class WindowsVolumeService : IVolumeService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly ILmsClient _lms;
    private readonly ILogger<WindowsVolumeService> _log;
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _device;
    private AudioEndpointVolume? _endpointVolume;
    private int _systemPercent;

    public WindowsVolumeService(ISettingsService settings, ILmsClient lms, ILogger<WindowsVolumeService> log)
    {
        _settings = settings;
        _lms = lms;
        _log = log;

        try
        {
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _endpointVolume = _device.AudioEndpointVolume;
            _endpointVolume.OnVolumeNotification += OnSystemVolumeChanged;
            _systemPercent = (int)Math.Round(_endpointVolume.MasterVolumeLevelScalar * 100);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Default-Endpoint nicht ermittelbar; Volume-Service läuft im Read-only-Modus.");
        }

        _lms.PlayerVolumeChanged += OnLmsVolumeChanged;
    }

    public int SystemVolumePercent => _systemPercent;

    private void OnSystemVolumeChanged(AudioVolumeNotificationData data)
    {
        _systemPercent = (int)Math.Round(data.MasterVolume * 100);
        _ = ApplyAllAsync();
    }

    private void OnLmsVolumeChanged(object? sender, PlayerVolumeChangedEventArgs e)
    {
        var model = _settings.Current;
        var entry = model.Players.FirstOrDefault(p => p.Id == e.PlayerId);
        if (entry is null || !entry.AppControlsVolume) return;

        var newTrim = VolumeMath.RecoverTrim(e.Volume, _systemPercent);
        if (newTrim != entry.TrimPercent)
        {
            entry.TrimPercent = newTrim;
            _settings.NotifyChanged();
        }
    }

    public async Task SetTrimAsync(string playerId, int trimPercent, CancellationToken ct = default)
    {
        var trim = VolumeMath.ClampTrim(trimPercent);
        var entry = EnsurePlayerEntry(playerId);
        entry.TrimPercent = trim;
        _settings.NotifyChanged();

        if (entry.AppControlsVolume)
        {
            var effective = VolumeMath.EffectiveVolume(_systemPercent, trim);
            await _lms.SetVolumeAsync(playerId, effective, ct).ConfigureAwait(false);
        }
    }

    public async Task SetAppControlAsync(string playerId, bool enabled, CancellationToken ct = default)
    {
        var entry = EnsurePlayerEntry(playerId);
        entry.AppControlsVolume = enabled;
        _settings.NotifyChanged();

        if (enabled)
        {
            var effective = VolumeMath.EffectiveVolume(_systemPercent, entry.TrimPercent);
            await _lms.SetVolumeAsync(playerId, effective, ct).ConfigureAwait(false);
        }
    }

    public async Task ApplyAllAsync(CancellationToken ct = default)
    {
        var model = _settings.Current;
        foreach (var p in model.Players.Where(p => p.AppControlsVolume))
        {
            var effective = VolumeMath.EffectiveVolume(_systemPercent, p.TrimPercent);
            try
            {
                await _lms.SetVolumeAsync(p.Id, effective, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Volume-Apply fehlgeschlagen für {Id}", p.Id);
            }
        }
    }

    private Core.Models.PersistedPlayer EnsurePlayerEntry(string playerId)
    {
        var model = _settings.Current;
        var entry = model.Players.FirstOrDefault(p => p.Id == playerId);
        if (entry is null)
        {
            entry = new Core.Models.PersistedPlayer { Id = playerId };
            model.Players.Add(entry);
        }
        return entry;
    }

    public void Dispose()
    {
        _lms.PlayerVolumeChanged -= OnLmsVolumeChanged;
        if (_endpointVolume is not null)
        {
            try { _endpointVolume.OnVolumeNotification -= OnSystemVolumeChanged; } catch { /* ignore */ }
        }
        _device?.Dispose();
        _enumerator.Dispose();
    }
}
