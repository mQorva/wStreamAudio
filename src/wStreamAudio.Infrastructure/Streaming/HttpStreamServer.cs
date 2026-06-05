using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using NAudio.Lame;
using NAudio.Wave;
using wStreamAudio.Core.Abstractions;

namespace wStreamAudio.Infrastructure.Streaming;

/// <summary>
/// Live-Audio-HTTP-Server. Stream wird als MP3 (128 kbps CBR) ausgeliefert, weil das
/// die einzige Format-Kette ist, die auf praktisch jedem LMS-Setup ohne zusätzliches
/// Transcoding (sox / convert.conf-Fummelei) läuft. Pro Client ein eigener LAME-Encoder.
/// </summary>
public sealed class HttpStreamServer : IStreamServer
{
    private const int Mp3BitrateKbps = 128;

    private readonly ILogger<HttpStreamServer> _log;
    private readonly ISettingsService _settings;
    private readonly object _clientsLock = new();
    private readonly List<StreamClient> _clients = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private int _port;
    private int _sampleRate;
    private int _channels;

    public HttpStreamServer(ILogger<HttpStreamServer> log, ISettingsService settings)
    {
        _log = log;
        _settings = settings;
    }

    public bool IsRunning => _listener is not null;
    public Uri? StreamUrl => _listener is null ? null : new Uri($"http://{ResolveAdvertisedHost()}:{_port}/stream.mp3");
    public int Port => _port;

    public Task StartAsync(int port, CancellationToken ct = default)
    {
        if (_listener is not null) return Task.CompletedTask;

        _port = port;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        _log.LogInformation("StreamServer hört auf Port {Port} (MP3 {Br} kbps)", port, Mp3BitrateKbps);
        return Task.CompletedTask;
    }

    public void SetFormat(int sampleRate, int channels)
    {
        if (sampleRate > 0) _sampleRate = sampleRate;
        if (channels > 0) _channels = channels;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "AcceptLoop-Fehler");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
    {
        StreamClient? streamClient = null;
        var remote = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
        try
        {
            tcp.NoDelay = true;
            tcp.SendBufferSize = 64 * 1024;
            var netStream = tcp.GetStream();
            using var reader = new StreamReader(netStream, Encoding.ASCII, leaveOpen: true);

            var requestLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                if (line.Length == 0) break;
            }
            _log.LogInformation("Stream-Client {Remote} → {Request}", remote, requestLine ?? "(leer)");
            StreamLog.Write($"connect {remote} request='{requestLine ?? string.Empty}'");

            var isHead = requestLine != null && requestLine.StartsWith("HEAD ", StringComparison.OrdinalIgnoreCase);

            // Sample-Rate steht durch SetFormat in der Regel sofort fest. Falls nicht,
            // sehr kurz warten und auf 44100/2 fallen.
            var waitEnd = DateTime.UtcNow + TimeSpan.FromMilliseconds(200);
            while (_sampleRate == 0 && DateTime.UtcNow < waitEnd && !ct.IsCancellationRequested)
            {
                await Task.Delay(20, ct).ConfigureAwait(false);
            }
            if (_sampleRate == 0) _sampleRate = 44100;
            if (_channels == 0) _channels = 2;

            // HTTP-Header SOFORT senden. Icy-Header sagen LMS und anderen Stream-Clients,
            // dass es sich um einen Live-Stream handelt — kein Datei-Scan nötig.
            var headerBytes = Encoding.ASCII.GetBytes(BuildHttpHeaders());
            await netStream.WriteAsync(headerBytes, ct).ConfigureAwait(false);
            await netStream.FlushAsync(ct).ConfigureAwait(false);
            StreamLog.Write($"sent http headers to {remote} ({headerBytes.Length} bytes), format {_sampleRate}/{_channels}");

            if (isHead)
            {
                StreamLog.Write($"HEAD-only response to {remote}, closing");
                return;
            }

            // LAME-Encoder pro Client erzeugen. Schreibt MP3-Frames direkt in den TCP-Stream.
            LameMP3FileWriter writer;
            CountingWriteStream encodedStream;
            try
            {
                var inputFormat = new WaveFormat(_sampleRate, 16, _channels);
                encodedStream = new CountingWriteStream(netStream);
                writer = new LameMP3FileWriter(encodedStream, inputFormat, Mp3BitrateKbps);
                StreamLog.Write($"LAME init OK for {remote}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "MP3-Encoder konnte nicht initialisiert werden — libmp3lame-DLL fehlt?");
                StreamLog.Write($"LAME init FAILED for {remote}: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            // Anschiebe-Buffer: standardmäßig 200 ms Stille. Wenn der Client (typisch DLNA-
            // Renderer) per ?buf=<ms> mehr verlangt, schicken wir entsprechend mehr vorab —
            // dann ist der Receiver-Puffer voll und der Stream startet sofort statt nach
            // mehreren Sekunden. WICHTIG: NICHT writer.Flush() rufen (siehe LAME-Comment).
            var primingMs = ParseBufferQuery(requestLine) ?? 200;
            primingMs = Math.Clamp(primingMs, 0, 10000);
            var primingSamples = _sampleRate * primingMs / 1000;
            var primingBuffer = new byte[primingSamples * _channels * 2];
            StreamLog.Write($"priming {remote} with {primingMs} ms silence (request-buf override applied)");
            try
            {
                var before = encodedStream.BytesWritten;
                writer.Write(primingBuffer, 0, primingBuffer.Length);
                var encodedBytes = encodedStream.BytesWritten - before;
                StreamLog.Write($"primed {remote} with {primingBuffer.Length} PCM bytes ({primingMs} ms silence), encoded {encodedBytes} MP3 bytes");
            }
            catch (Exception ex)
            {
                StreamLog.Write($"priming FAILED for {remote}: {ex.GetType().Name}: {ex.Message}");
            }

            streamClient = new StreamClient(tcp, encodedStream, writer);
            lock (_clientsLock) { _clients.Add(streamClient); }
            StreamLog.Write($"client {remote} added, total clients: {_clients.Count}");

            // Hinweis: Priming wurde oben außerhalb des Locks geschrieben — solange der
            // Client noch nicht in _clients ist, kann ihn auch niemand pushen, daher safe.

            try
            {
                while (!ct.IsCancellationRequested && tcp.Connected)
                {
                    await Task.Delay(500, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                lock (_clientsLock) { _clients.Remove(streamClient); }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            StreamLog.Write($"client {remote} disconnected: {ex.GetType().Name}");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Stream-Client-Fehler");
            StreamLog.Write($"client {remote} error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            StreamLog.Write($"client {remote} closing");
            if (streamClient is not null)
            {
                lock (streamClient.WriteLock)
                {
                    if (!streamClient.Closed)
                    {
                        streamClient.Closed = true;
                        try { streamClient.Encoder.Dispose(); } catch { /* ignore */ }
                    }
                }
            }
            try { tcp.Close(); } catch { /* ignore */ }
        }
    }

    public void PushPcmFrame(ReadOnlySpan<byte> pcm, int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
        _channels = channels;

        StreamClient[] snapshot;
        lock (_clientsLock) { snapshot = _clients.ToArray(); }
        if (snapshot.Length == 0) return;

        // PCM in eine kopierbare Form bringen — der LAME-Writer braucht ein Array.
        var buffer = pcm.ToArray();

        foreach (var c in snapshot)
        {
            // Remote-Endpoint vor dem evtl. Drop einfangen — nach Close ist er weg.
            string remote;
            try { remote = c.Tcp.Client.RemoteEndPoint?.ToString() ?? "?"; }
            catch { remote = "?"; }

            if (c.Closed) continue;

            try
            {
                if (!c.Tcp.Connected) { Drop(c); continue; }
                // Per-Client serialisieren: WASAPI-OnData und Silence-Timer können
                // parallel pushen, LAME ist NICHT thread-safe. Außerdem stellen wir
                // sicher, dass der Encoder zwischen den Threads nicht disposed wird.
                lock (c.WriteLock)
                {
                    if (c.Closed) continue;
                    c.Encoder.Write(buffer, 0, buffer.Length);
                }
                c.BytesIn += buffer.Length;
                c.EncodedBytes = c.Stream.BytesWritten;
                var nowTicks = DateTime.UtcNow.Ticks;
                if (nowTicks - c.LastLogTicks > TimeSpan.TicksPerSecond * 3)
                {
                    var encodedDelta = c.EncodedBytes - c.LastEncodedBytes;
                    StreamLog.Write($"client {remote} +{c.BytesIn} PCM bytes, +{encodedDelta} MP3 bytes since last log");
                    c.BytesIn = 0;
                    c.LastEncodedBytes = c.EncodedBytes;
                    c.LastLogTicks = nowTicks;
                }
            }
            catch (IOException ex)
            {
                // Normaler Client-Disconnect (LMS-Scanner schließt nach Header-Lesen,
                // Player verbindet neu, etc.). Kein echter Fehler.
                StreamLog.Write($"push to {remote}: client disconnected ({ex.Message.Split('\n')[0].Trim()})");
                Drop(c);
            }
            catch (Exception ex)
            {
                StreamLog.Write($"push to {remote} failed: {ex.GetType().Name}: {ex.Message}");
                Drop(c);
            }
        }
    }

    private void Drop(StreamClient c)
    {
        lock (_clientsLock) { _clients.Remove(c); }
        // Closed-Flag und Dispose unter dem WriteLock — sonst kann ein paralleler
        // Push den gerade disposed Encoder treffen und InvalidOperationException werfen.
        // Kein Flush() vor Dispose: Dispose macht den finalen LAME-Flush selbst.
        lock (c.WriteLock)
        {
            if (c.Closed) return;
            c.Closed = true;
            try { c.Encoder.Dispose(); } catch { /* ignore */ }
        }
        try { c.Tcp.Close(); } catch { /* ignore */ }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        var listener = _listener;
        _listener = null;

        try { _cts?.Cancel(); } catch { /* ignore */ }
        listener?.Stop();

        StreamClient[] snapshot;
        lock (_clientsLock)
        {
            snapshot = _clients.ToArray();
            _clients.Clear();
        }
        foreach (var c in snapshot)
        {
            lock (c.WriteLock)
            {
                if (!c.Closed)
                {
                    c.Closed = true;
                    try { c.Encoder.Dispose(); } catch { /* ignore */ }
                }
            }
            try { c.Tcp.Close(); } catch { /* ignore */ }
        }

        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); } catch { /* ignore */ }
        }

        _cts?.Dispose();
        _cts = null;
        _acceptLoop = null;
    }

    private string ResolveAdvertisedHost()
    {
        try
        {
            var lms = _settings.Current.Lms;
            if (!string.IsNullOrWhiteSpace(lms.Host))
            {
                using var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                udp.Connect(lms.Host, lms.Port > 0 ? lms.Port : 9000);
                if (udp.LocalEndPoint is IPEndPoint ep
                    && !IPAddress.IsLoopback(ep.Address)
                    && ep.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ep.Address.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Routing-basierte IP-Auflösung fehlgeschlagen, falle auf NIC-Enumeration zurück");
        }

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    var addr = ua.Address;
                    if (addr.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(addr)) continue;
                    var bytes = addr.GetAddressBytes();
                    if (bytes[0] == 169 && bytes[1] == 254) continue;
                    return addr.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "NIC-Enumeration fehlgeschlagen");
        }

        return "localhost";
    }

    /// <summary>
    /// Liest den optionalen Query-Parameter <c>?buf=&lt;ms&gt;</c> aus der HTTP-Request-Zeile
    /// (z.B. „GET /stream.mp3?buf=3000 HTTP/1.1") und gibt den Wert in Millisekunden zurück.
    /// Null wenn nicht angegeben oder nicht parsebar.
    /// </summary>
    private static int? ParseBufferQuery(string? requestLine)
    {
        if (string.IsNullOrEmpty(requestLine)) return null;
        // „GET <path> HTTP/1.1" → mittlerer Teil
        var parts = requestLine.Split(' ');
        if (parts.Length < 2) return null;
        var path = parts[1];
        var q = path.IndexOf('?');
        if (q < 0) return null;
        foreach (var kv in path[(q + 1)..].Split('&'))
        {
            var eq = kv.IndexOf('=');
            if (eq <= 0) continue;
            if (!string.Equals(kv[..eq], "buf", StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(kv[(eq + 1)..], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var ms))
                return ms;
        }
        return null;
    }

    private static string BuildHttpHeaders()
    {
        var sb = new StringBuilder(384);
        sb.Append("HTTP/1.0 200 OK\r\n");
        sb.Append("Server: wStreamAudio\r\n");
        sb.Append("Cache-Control: no-cache\r\n");
        sb.Append("Pragma: no-cache\r\n");
        sb.Append("Content-Type: audio/mpeg\r\n");
        sb.Append("Accept-Ranges: none\r\n");
        sb.Append("icy-name: wStreamAudio\r\n");
        sb.Append("icy-genre: System Audio\r\n");
        sb.Append("icy-pub: 0\r\n");
        sb.Append("icy-br: ").Append(Mp3BitrateKbps).Append("\r\n");
        sb.Append("\r\n");
        return sb.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private sealed class StreamClient
    {
        public StreamClient(TcpClient tcp, CountingWriteStream stream, LameMP3FileWriter encoder)
        {
            Tcp = tcp;
            Stream = stream;
            Encoder = encoder;
            LastLogTicks = DateTime.UtcNow.Ticks;
            LastEncodedBytes = stream.BytesWritten;
        }
        public TcpClient Tcp { get; }
        public CountingWriteStream Stream { get; }
        public LameMP3FileWriter Encoder { get; }
        public object WriteLock { get; } = new();
        public long BytesIn { get; set; }
        public long EncodedBytes { get; set; }
        public long LastEncodedBytes { get; set; }
        public long LastLogTicks { get; set; }
        // Wird unter WriteLock gesetzt, sobald der Encoder disposed werden soll. Verhindert,
        // dass parallele Push-Aufrufe auf einem soeben disposed Encoder enden ("Output stream closed").
        public bool Closed { get; set; }
    }

    private sealed class CountingWriteStream : Stream
    {
        private readonly Stream _inner;

        public CountingWriteStream(Stream inner) => _inner = inner;

        public long BytesWritten { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            BytesWritten += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _inner.Write(buffer);
            BytesWritten += buffer.Length;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            BytesWritten += buffer.Length;
        }
    }
}
