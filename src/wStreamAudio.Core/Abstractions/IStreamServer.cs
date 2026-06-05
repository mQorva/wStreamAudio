namespace wStreamAudio.Core.Abstractions;

public interface IStreamServer : IAsyncDisposable
{
    bool IsRunning { get; }
    Uri? StreamUrl { get; }
    int Port { get; }

    Task StartAsync(int port, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Stream-Format vorab setzen, damit HTTP-Header und WAV-Header sofort beim Connect
    /// rausgehen können, ohne auf das erste tatsächliche PCM-Frame warten zu müssen.
    /// </summary>
    void SetFormat(int sampleRate, int channels);

    /// <summary>Schreibt einen PCM-Frame (16-bit interleaved) in die Encoder-Pipeline.</summary>
    void PushPcmFrame(ReadOnlySpan<byte> pcm, int sampleRate, int channels);
}

