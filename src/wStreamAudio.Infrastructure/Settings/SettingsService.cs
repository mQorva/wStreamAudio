using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Core.Settings;

namespace wStreamAudio.Infrastructure.Settings;

public sealed class SettingsService : ISettingsService, IAsyncDisposable
{
    private readonly IAppProfile _profile;
    private readonly ILogger<SettingsService> _log;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly object _stateLock = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Lazy<string> _settingsPath;

    private SettingsModel _current = new();
    private bool _loaded;
    private bool _disposed;
    private CancellationTokenSource? _debounceCts;

    public SettingsService(IAppProfile profile, ILogger<SettingsService> log)
    {
        _profile = profile;
        _log = log;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        _settingsPath = new Lazy<string>(ResolveSettingsPath);
    }

    public SettingsModel Current
    {
        get { lock (_stateLock) { return _current; } }
    }

    public event EventHandler? Saved;

    public async Task<SettingsModel> LoadAsync(CancellationToken ct = default)
    {
        if (_loaded)
        {
            return Current;
        }

        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return Current;
            }

            var path = _settingsPath.Value;
            SettingsModel model = new();
            var fileExisted = File.Exists(path);
            if (fileExisted)
            {
                try
                {
                    await using var fs = File.OpenRead(path);
                    var loaded = await JsonSerializer.DeserializeAsync<SettingsModel>(fs, _jsonOptions, ct)
                        .ConfigureAwait(false);
                    if (loaded is not null)
                    {
                        model = loaded;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "settings.json konnte nicht gelesen werden, nutze Defaults");
                }
            }

            // Erststart-Detection: noch keine Settings-Datei → UI-Sprache aus System ableiten.
            if (!fileExisted)
            {
                try
                {
                    var ui = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                    model.General.LanguageCode = string.Equals(ui, "de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
                }
                catch { /* default bleibt "de" */ }
            }

            lock (_stateLock)
            {
                _current = model;
                _loaded = true;
            }

            return model;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public void NotifyChanged()
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _debounceCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        _ = DebouncedSaveAsync(cts.Token);
    }

    private async Task DebouncedSaveAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(Core.Models.Defaults.SettingsAutoSaveDebounceMs, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await SaveAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Auto-Save fehlgeschlagen");
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        // Nach Dispose nichts mehr schreiben — sonst wirft das SemaphoreSlim ObjectDisposed.
        if (_disposed) return;
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = _settingsPath.Value;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";

            SettingsModel snapshot;
            lock (_stateLock) { snapshot = _current; }

            await using (var fs = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(fs, snapshot, _jsonOptions, ct).ConfigureAwait(false);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            _ioLock.Release();
        }

        Saved?.Invoke(this, EventArgs.Empty);
    }

    private string ResolveSettingsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        _profile.DataFolderName,
        "settings.json");

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try { await SaveAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        _disposed = true;
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _ioLock.Dispose();
    }
}
