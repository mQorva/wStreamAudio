using wStreamAudio.Core.Models;

namespace wStreamAudio.Core.Abstractions;

public interface IAudioCapture : IAsyncDisposable
{
    bool IsRunning { get; }
    int SampleRate { get; }
    int Channels { get; }
    int BitsPerSample { get; }

    Task StartAsync(CaptureProfile profile, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);

    /// <summary>Wird für jeden gelieferten PCM-Block aufgerufen (interleaved 16-bit).</summary>
    event EventHandler<AudioFrameEventArgs>? Frame;

    /// <summary>
    /// Liefert für UI-VU-Anzeige Spitzenpegel (0..1) je Kanal pro echtem Frame.
    /// Wird gedrosselt auf ~30 Hz, damit der UI-Thread nicht überflutet wird.
    /// Stille-Keepalive-Frames werden NICHT gemeldet — so sieht man, ob echtes Audio fließt.
    /// </summary>
    event EventHandler<AudioLevelEventArgs>? LevelChanged;
}

public sealed class AudioFrameEventArgs(byte[] buffer, int length) : EventArgs
{
    public byte[] Buffer { get; } = buffer;
    public int Length { get; } = length;
}

public sealed class AudioLevelEventArgs(float peakLeft, float peakRight) : EventArgs
{
    public float PeakLeft { get; } = peakLeft;
    public float PeakRight { get; } = peakRight;
}

public interface IAudioEndpointCatalog
{
    IReadOnlyList<AudioEndpointInfo> EnumerateRenderEndpoints();
    AudioEndpointInfo? GetDefaultRenderEndpoint();
}

public sealed record AudioEndpointInfo(string Id, string DisplayName, bool IsDefault);
