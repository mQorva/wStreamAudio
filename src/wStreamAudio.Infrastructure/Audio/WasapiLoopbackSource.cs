using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Core.Models;

namespace wStreamAudio.Infrastructure.Audio;

/// <summary>
/// EndpointLoopback-Capture: greift auf einem konkreten Render-Endpoint
/// (z.B. SPDIF) das abgespielte Audio ab, ohne den System-Default zu ändern.
/// Liefert 16-bit-PCM-Frames in der Capture-Geräte-Samplerate.
/// </summary>
public sealed class WasapiLoopbackSource : IAudioCapture
{
    private readonly ILogger<WasapiLoopbackSource> _log;
    private readonly IAudioEndpointCatalog _catalog;
    private WasapiLoopbackCapture? _capture;
    private MMDevice? _device;
    private bool _running;
    private long _lastFrameTicks;
    private System.Threading.Timer? _silenceTimer;

    public WasapiLoopbackSource(IAudioEndpointCatalog catalog, ILogger<WasapiLoopbackSource> log)
    {
        _catalog = catalog;
        _log = log;
    }

    public bool IsRunning => _running;
    public int SampleRate { get; private set; }
    /// <summary>Wir liefern immer Stereo nach außen — Multi-Channel wird vorher downgemixt.</summary>
    public int Channels { get; private set; } = 2;
    public int BitsPerSample { get; private set; } = 16;
    private int _wasapiChannels = 2;

    public event EventHandler<AudioFrameEventArgs>? Frame;
    public event EventHandler<AudioLevelEventArgs>? LevelChanged;
    private long _lastLevelEmitTicks;

    public Task StartAsync(CaptureProfile profile, CancellationToken ct = default)
    {
        if (profile.Mode != CaptureMode.EndpointLoopback)
        {
            throw new InvalidOperationException("WasapiLoopbackSource unterstützt nur CaptureMode.EndpointLoopback.");
        }

        StopInternal();

        var enumerator = new MMDeviceEnumerator();
        try
        {
            _device = ResolveDevice(enumerator, profile)
                      ?? throw new InvalidOperationException("Kein passender Render-Endpoint gefunden.");

            _capture = new WasapiLoopbackCapture(_device);
            SampleRate = _capture.WaveFormat.SampleRate;
            _wasapiChannels = _capture.WaveFormat.Channels;
            // Nach außen liefern wir IMMER Stereo. Multi-Channel-Quellen (5.1, 7.1)
            // werden in OnData per ITU-R-BS.775-Downmix in 2 Kanäle gemischt — sonst
            // schluckt LAME / die meisten anderen Encoder das nicht.
            Channels = 2;
            BitsPerSample = 16;

            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null)
                {
                    _log.LogError(e.Exception, "WASAPI-Loopback abgebrochen");
                }
            };
            _capture.StartRecording();
            _running = true;
            _lastFrameTicks = DateTime.UtcNow.Ticks;
            // Keep-Alive: alle 100 ms prüfen — wenn die Audio-Engine grade still ist
            // (Windows pausiert sie, sobald nichts spielt), liefern wir selbst Stille,
            // damit der HTTP-Stream zu LMS / DLNA-Renderern nicht abreißt.
            _silenceTimer = new System.Threading.Timer(EmitSilenceIfStale, null, 100, 100);
            _log.LogInformation("Loopback gestartet auf '{Name}' ({Sr} Hz, {Ch} ch)", _device.FriendlyName, SampleRate, Channels);
        }
        finally
        {
            enumerator.Dispose();
        }

        return Task.CompletedTask;
    }

    private static MMDevice? ResolveDevice(MMDeviceEnumerator enumerator, CaptureProfile profile)
    {
        if (profile.FollowDefaultEndpoint || string.IsNullOrEmpty(profile.EndpointId))
        {
            try
            {
                return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch
            {
                return null;
            }
        }

        foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            if (string.Equals(d.ID, profile.EndpointId, StringComparison.OrdinalIgnoreCase))
            {
                return d;
            }
            d.Dispose();
        }

        return null;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        // Nach StopAsync können NAudio-Callbacks noch ein-, zweimal nachfeuern. Diese
        // Frames würden sonst LevelChanged-Events auslösen und die UI „weiterlaufen" lassen.
        if (!_running) return;
        if (e.BytesRecorded <= 0) return;

        // WASAPI-Loopback liefert IEEE float (32-bit). Wir mixen runter auf Stereo
        // und konvertieren zu 16-bit PCM. Ergibt immer Channels == 2 nach außen.
        var src = e.Buffer;
        var srcCh = Math.Max(1, _wasapiChannels);
        const int srcSampleSize = 4; // float32
        int frameCount = e.BytesRecorded / (srcSampleSize * srcCh);
        var pcm = new byte[frameCount * 2 * 2]; // Stereo, 16-bit

        const float DownmixGain = 0.707f; // -3 dB per ITU-R BS.775
        float peakL = 0f, peakR = 0f;

        for (int frame = 0; frame < frameCount; frame++)
        {
            int srcOffset = frame * srcSampleSize * srcCh;
            float l, r;

            switch (srcCh)
            {
                case 1:
                    // Mono → beide Kanäle gleich
                    l = r = BitConverter.ToSingle(src, srcOffset);
                    break;
                case 2:
                    l = BitConverter.ToSingle(src, srcOffset);
                    r = BitConverter.ToSingle(src, srcOffset + 4);
                    break;
                default:
                {
                    // 3+ Kanäle: WASAPI-Standard-Layout ist FL, FR, FC, LFE, BL, BR, SL, SR.
                    // Standard-Downmix: L = FL + 0.707·(FC + BL + SL), analog für R.
                    // LFE wird nicht eingemischt (Hochpass-relevant, klingt sonst dröhnig).
                    float fl = BitConverter.ToSingle(src, srcOffset + 0);
                    float fr = BitConverter.ToSingle(src, srcOffset + 4);
                    float fc = srcCh >= 3 ? BitConverter.ToSingle(src, srcOffset + 8) : 0f;
                    // [12..16] = LFE → ignorieren
                    float bl = srcCh >= 5 ? BitConverter.ToSingle(src, srcOffset + 16) : 0f;
                    float br = srcCh >= 6 ? BitConverter.ToSingle(src, srcOffset + 20) : 0f;
                    float sl = srcCh >= 7 ? BitConverter.ToSingle(src, srcOffset + 24) : 0f;
                    float sr = srcCh >= 8 ? BitConverter.ToSingle(src, srcOffset + 28) : 0f;
                    l = fl + DownmixGain * (fc + bl + sl);
                    r = fr + DownmixGain * (fc + br + sr);
                    break;
                }
            }

            l = Math.Clamp(l, -1.0f, 1.0f);
            r = Math.Clamp(r, -1.0f, 1.0f);

            var absL = MathF.Abs(l);
            var absR = MathF.Abs(r);
            if (absL > peakL) peakL = absL;
            if (absR > peakR) peakR = absR;

            short sl16 = (short)(l * short.MaxValue);
            short sr16 = (short)(r * short.MaxValue);
            int dst = frame * 4;
            pcm[dst + 0] = (byte)(sl16 & 0xFF);
            pcm[dst + 1] = (byte)((sl16 >> 8) & 0xFF);
            pcm[dst + 2] = (byte)(sr16 & 0xFF);
            pcm[dst + 3] = (byte)((sr16 >> 8) & 0xFF);
        }

        _lastFrameTicks = DateTime.UtcNow.Ticks;
        Frame?.Invoke(this, new AudioFrameEventArgs(pcm, pcm.Length));

        // Pegel-Event auf ~33 Hz drosseln.
        var nowTicks = DateTime.UtcNow.Ticks;
        if (nowTicks - _lastLevelEmitTicks > TimeSpan.TicksPerMillisecond * 30)
        {
            _lastLevelEmitTicks = nowTicks;
            LevelChanged?.Invoke(this, new AudioLevelEventArgs(peakL, peakR));
        }
    }

    private void EmitSilenceIfStale(object? state)
    {
        if (!_running) return;
        var ageMs = (DateTime.UtcNow.Ticks - Volatile.Read(ref _lastFrameTicks)) / TimeSpan.TicksPerMillisecond;
        if (ageMs < 150) return; // Echtes Audio kommt — kein Bedarf für Stille.

        var sr = SampleRate > 0 ? SampleRate : 44100;
        var ch = Channels > 0 ? Channels : 2;
        // ~80 ms Block: gibt LMS und DLNA-Renderern genug Buffer, um den Stream offen zu halten.
        var samples = sr * 80 / 1000;
        var pcm = new byte[samples * ch * 2];
        // pcm ist bereits 0-initialisiert (16-bit PCM, also Stille).
        _lastFrameTicks = DateTime.UtcNow.Ticks;
        Frame?.Invoke(this, new AudioFrameEventArgs(pcm, pcm.Length));
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        StopInternal();
        return Task.CompletedTask;
    }

    private void StopInternal()
    {
        // Zuerst _running auf false — damit der OnData-Frühausstieg greift, BEVOR
        // NAudios StopRecording die letzten Frames durch den Callback drückt.
        _running = false;

        try
        {
            _silenceTimer?.Dispose();
            _silenceTimer = null;
        }
        catch { /* ignore */ }

        try
        {
            _capture?.StopRecording();
        }
        catch
        {
            // ignore
        }

        _capture?.Dispose();
        _capture = null;
        _device?.Dispose();
        _device = null;
    }

    public ValueTask DisposeAsync()
    {
        StopInternal();
        return ValueTask.CompletedTask;
    }
}
