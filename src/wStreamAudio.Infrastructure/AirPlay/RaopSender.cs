using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Core.Models;

namespace wStreamAudio.Infrastructure.AirPlay;

/// <summary>
/// AirPlay-1-Sender via RAOP (Remote Audio Output Protocol). Implementiert das
/// klassische RTSP+RTP-Verfahren mit AES-CBC-verschlüsselten L16-PCM-Frames.
/// Funktioniert mit AirPort-Express, shairport-sync und Denon-AVRs im AirPlay-1-Modus.
///
/// Was bewusst NICHT da ist:
///  * AirPlay 2 (Curve25519/PTP/SRP-Pairing) — Apple-Geräte wie HomePod/AppleTV
///    sind damit ausgeschlossen.
///  * Retransmission via Control-Port — Empfänger-Requests werden ignoriert.
///  * Hi-res / 24-bit ALAC — wir senden L16 16-bit-Stereo bei 44.1 kHz.
/// </summary>
public sealed class RaopSender : IAirPlaySender, IAsyncDisposable
{
    // Wohlbekannter AirPort-Express-Public-Key. Wird zur RSA-OAEP-Verschlüsselung
    // des AES-Session-Keys verwendet — alle RAOP-Empfänger akzeptieren das.
    private const string AirPortExpressPublicKeyPem = @"-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA59dE8qLieItsH1WgjrcF
RKj6eUWqi+bGLOX1HL3U3GhC/j0Qg90u3sG/1CUtwC5vOYvfDmFI6oSFXi5ELabW
JmT2dKHzBJKa3k9ok+8t9ucRqMd6DZHJ2YCCLlDRKSKv6kDqnw4UwPdpOMXziC/A
MJ1hRW0t1zKkymMxK5JKhYr+MeqOaheh50sTV2YeM0Ng4S0qzlW7+pHasJyfgRkk
0uH+xQZGqJ08gV3x69b9owWzL8XjEEHJW1MzhXLuNYxnFFkF/dV2/u4anPGTtPiC
NvRTtkbQYV4Y8YqgPMA3WzCYrYDXq7+jLqsRzWjjyXdQqOQjGmcOSr5e1iy1IiPI
twIDAQAB
-----END PUBLIC KEY-----";

    // RAOP-Konstanten — fest verdrahtet, weil Empfänger das so erwarten.
    private const int RaopSampleRate = 44100;
    private const int RaopChannels = 2;
    private const int RaopBitsPerSample = 16;
    private const int FramesPerPacket = 352;          // Samples pro Kanal
    private const int BytesPerPacket = FramesPerPacket * RaopChannels * 2; // 1408
    private const int RtpHeaderLen = 12;
    private const byte RtpPayloadType = 96;

    private readonly ILogger<RaopSender> _log;
    private readonly ConcurrentDictionary<string, RaopSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public RaopSender(ILogger<RaopSender> log)
    {
        _log = log;
    }

    public async Task PlayAsync(AirPlayDevice device, CancellationToken ct = default)
    {
        if (_sessions.ContainsKey(device.Id))
        {
            _log.LogDebug("RAOP: Session zu {Host} läuft bereits", device.Host);
            return;
        }
        var session = new RaopSession(device, _log);
        try
        {
            await session.OpenAsync(ct).ConfigureAwait(false);
            _sessions[device.Id] = session;
            _log.LogInformation("RAOP: Stream gestartet zu {Name} @ {Host}:{Port}",
                device.FriendlyName, device.Host, device.Port);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "RAOP: Verbindung zu {Name} @ {Host}:{Port} fehlgeschlagen",
                device.FriendlyName, device.Host, device.Port);
            try { await session.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
            throw;
        }
    }

    public async Task StopAsync(AirPlayDevice device, CancellationToken ct = default)
    {
        if (_sessions.TryRemove(device.Id, out var session))
        {
            try { await session.CloseAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) { _log.LogDebug(ex, "RAOP: Fehler beim Schließen von {Host}", device.Host); }
            await session.DisposeAsync().ConfigureAwait(false);
            _log.LogInformation("RAOP: Stream beendet zu {Name}", device.FriendlyName);
        }
    }

    public async Task SetVolumeAsync(AirPlayDevice device, int volumePercent, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(device.Id, out var session)) return;
        try { await session.SetVolumeAsync(volumePercent, ct).ConfigureAwait(false); }
        catch (Exception ex) { _log.LogDebug(ex, "RAOP: SetVolume an {Host} fehlgeschlagen", device.Host); }
    }

    public void PushPcmFrame(ReadOnlySpan<byte> pcm16LeStereo, int sampleRate)
    {
        // Falls keine Sessions aktiv sind — nichts tun. Stark heißer Pfad.
        if (_sessions.IsEmpty) return;
        // Kopie für die Sessions (jede hat ihren eigenen Ring-Puffer + Send-Thread).
        // Wir kopieren EINMAL und teilen das Array — Sessions lesen nur.
        var copy = pcm16LeStereo.ToArray();
        foreach (var s in _sessions.Values)
        {
            s.Enqueue(copy, sampleRate);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var s in _sessions.Values)
        {
            try { await s.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
        }
        _sessions.Clear();
    }

    // ============================================================
    // Eine konkrete Session — eine RTSP-Verbindung + UDP-Audio-Sender.
    // ============================================================
    private sealed class RaopSession : IAsyncDisposable
    {
        private readonly AirPlayDevice _device;
        private readonly ILogger _log;
        private readonly byte[] _aesKey = RandomNumberGenerator.GetBytes(16);
        private readonly byte[] _aesIv = RandomNumberGenerator.GetBytes(16);
        private readonly string _clientInstance;
        private readonly string _dacpId;
        private readonly string _activeRemote;
        private readonly Channel<byte[]> _audioQueue;
        private readonly CancellationTokenSource _cts = new();

        private TcpClient? _rtsp;
        private NetworkStream? _rtspStream;
        private int _cseq;
        private string? _sessionCookie;
        private int _serverAudioPort;
        private int _serverControlPort;
        private int _serverTimingPort;
        private int _localAudioPort;
        private int _localControlPort;
        private int _localTimingPort;
        private UdpClient? _audioUdp;
        private UdpClient? _controlUdp;
        private UdpClient? _timingUdp;
        private IPEndPoint? _audioEndpoint;
        private readonly uint _ssrc = (uint)RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);
        private ushort _sequence;
        private uint _rtpTimestamp;
        private Task? _senderTask;
        private Task? _timingTask;
        private readonly byte[] _carry = new byte[BytesPerPacket * 2];
        private int _carryLen;

        public RaopSession(AirPlayDevice device, ILogger log)
        {
            _device = device;
            _log = log;
            _clientInstance = RandomHex(8);
            _dacpId = RandomHex(8);
            _activeRemote = RandomNumberGenerator.GetInt32(1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
            // Größerer Puffer — bei Aussetzern (Discovery o.ä.) verlieren wir nicht sofort Audio.
            _audioQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
        }

        public async Task OpenAsync(CancellationToken ct)
        {
            _rtsp = new TcpClient();
            _rtsp.NoDelay = true;
            await _rtsp.ConnectAsync(_device.Host, _device.Port, ct).ConfigureAwait(false);
            _rtspStream = _rtsp.GetStream();
            _log.LogDebug("RAOP: TCP verbunden zu {Host}:{Port}", _device.Host, _device.Port);

            // UDP-Sockets vorbereiten — die Ports brauchen wir im SETUP-Header.
            _audioUdp = new UdpClient(0, AddressFamily.InterNetwork);
            _controlUdp = new UdpClient(0, AddressFamily.InterNetwork);
            _timingUdp = new UdpClient(0, AddressFamily.InterNetwork);
            _localAudioPort = ((IPEndPoint)_audioUdp.Client.LocalEndPoint!).Port;
            _localControlPort = ((IPEndPoint)_controlUdp.Client.LocalEndPoint!).Port;
            _localTimingPort = ((IPEndPoint)_timingUdp.Client.LocalEndPoint!).Port;

            // 1) OPTIONS — gibt Empfänger Gelegenheit, Challenge-Response zu fordern.
            await SendRtspAsync("OPTIONS", "*", null, null, ct).ConfigureAwait(false);

            // 2) ANNOUNCE mit SDP-Body. AES-Key wird RSA-verschlüsselt mitgeschickt.
            var sdp = BuildSdp();
            var extraAnnounce = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/sdp",
            };
            await SendRtspAsync("ANNOUNCE", $"rtsp://{_device.Host}/{_clientInstance}", extraAnnounce, sdp, ct).ConfigureAwait(false);

            // 3) SETUP — UDP-Transport aushandeln.
            var transport = $"RTP/AVP/UDP;unicast;interleaved=0-1;mode=record;" +
                             $"control_port={_localControlPort};timing_port={_localTimingPort}";
            var extraSetup = new Dictionary<string, string> { ["Transport"] = transport };
            var setupResp = await SendRtspAsync("SETUP", $"rtsp://{_device.Host}/{_clientInstance}", extraSetup, null, ct).ConfigureAwait(false);
            ParseSetupResponse(setupResp);

            if (_serverAudioPort == 0)
            {
                throw new InvalidOperationException("RAOP-SETUP: server_port fehlt in der Antwort.");
            }
            var ip = (await Dns.GetHostAddressesAsync(_device.Host, ct).ConfigureAwait(false))
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? IPAddress.Parse(_device.Host);
            _audioEndpoint = new IPEndPoint(ip, _serverAudioPort);

            // 4) RECORD — der Empfänger erwartet ab jetzt RTP.
            _sequence = (ushort)RandomNumberGenerator.GetInt32(0, 65535);
            _rtpTimestamp = (uint)RandomNumberGenerator.GetInt32(0, int.MaxValue);
            var rtpInfo = $"seq={_sequence};rtptime={_rtpTimestamp}";
            var extraRecord = new Dictionary<string, string>
            {
                ["Range"] = "npt=0-",
                ["RTP-Info"] = rtpInfo,
            };
            await SendRtspAsync("RECORD", $"rtsp://{_device.Host}/{_clientInstance}", extraRecord, null, ct).ConfigureAwait(false);

            // 5) Initial-Volume auf den persistierten Wert setzen — Default -10 dB.
            try
            {
                var initial = _device is { } ? 50 : 50; // wird gleich von außen mit echtem Wert überschrieben
                await SetVolumeAsync(initial, ct).ConfigureAwait(false);
            }
            catch { /* nicht fatal */ }

            // Audio-Sender starten.
            _senderTask = Task.Run(() => SendLoopAsync(_cts.Token));
            _timingTask = Task.Run(() => TimingLoopAsync(_cts.Token));
        }

        public async Task CloseAsync(CancellationToken ct)
        {
            _cts.Cancel();
            try
            {
                if (_rtspStream is not null)
                {
                    await SendRtspAsync("TEARDOWN", $"rtsp://{_device.Host}/{_clientInstance}", null, null, ct).ConfigureAwait(false);
                }
            }
            catch { /* Empfänger oft schon weg */ }
        }

        public async Task SetVolumeAsync(int percent, CancellationToken ct)
        {
            var clamped = Math.Clamp(percent, 0, 100);
            double db = clamped == 0 ? -144.0 : -30.0 + (clamped / 100.0) * 30.0;
            var body = "volume: " + db.ToString("0.000000", CultureInfo.InvariantCulture) + "\r\n";
            var extra = new Dictionary<string, string> { ["Content-Type"] = "text/parameters" };
            await SendRtspAsync("SET_PARAMETER", $"rtsp://{_device.Host}/{_clientInstance}", extra, body, ct).ConfigureAwait(false);
        }

        public void Enqueue(byte[] pcmStereo16, int sampleRate)
        {
            // Lineares Resampling 48 → 44.1 kHz, falls nötig. Verlustbehaftet aber einfach.
            byte[] resampled = sampleRate == RaopSampleRate
                ? pcmStereo16
                : Resample(pcmStereo16, sampleRate, RaopSampleRate);
            _audioQueue.Writer.TryWrite(resampled);
        }

        private static byte[] Resample(byte[] src, int srcRate, int dstRate)
        {
            // src ist interleaved-stereo-16bit. Linear-nearest: nicht antialiased, aber tut's für Wurf 1.
            int srcFrames = src.Length / 4;
            int dstFrames = (int)((long)srcFrames * dstRate / srcRate);
            var dst = new byte[dstFrames * 4];
            for (int i = 0; i < dstFrames; i++)
            {
                int sIdx = (int)((long)i * srcRate / dstRate);
                if (sIdx >= srcFrames) sIdx = srcFrames - 1;
                int srcOff = sIdx * 4;
                int dstOff = i * 4;
                dst[dstOff] = src[srcOff];
                dst[dstOff + 1] = src[srcOff + 1];
                dst[dstOff + 2] = src[srcOff + 2];
                dst[dstOff + 3] = src[srcOff + 3];
            }
            return dst;
        }

        private async Task SendLoopAsync(CancellationToken ct)
        {
            try
            {
                var reader = _audioQueue.Reader;
                while (!ct.IsCancellationRequested)
                {
                    var chunk = await reader.ReadAsync(ct).ConfigureAwait(false);
                    AppendCarry(chunk);
                    while (_carryLen >= BytesPerPacket)
                    {
                        SendOnePacket(_carry.AsSpan(0, BytesPerPacket));
                        // restliche Bytes nach vorne schieben.
                        Buffer.BlockCopy(_carry, BytesPerPacket, _carry, 0, _carryLen - BytesPerPacket);
                        _carryLen -= BytesPerPacket;
                    }
                }
            }
            catch (OperationCanceledException) { /* normal */ }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "RAOP send-loop für {Host} abgebrochen", _device.Host);
            }
        }

        private void AppendCarry(byte[] chunk)
        {
            if (_carryLen + chunk.Length > _carry.Length)
            {
                // Carry-Buffer dynamisch wachsen lassen — kommt vor wenn der Reader hinterherhinkt.
                var bigger = new byte[Math.Max(_carry.Length * 2, _carryLen + chunk.Length)];
                Buffer.BlockCopy(_carry, 0, bigger, 0, _carryLen);
                Buffer.BlockCopy(chunk, 0, bigger, _carryLen, chunk.Length);
                _carryLen += chunk.Length;
                // _carry-Field ist readonly — wir können nicht ersetzen. Stattdessen direkt senden.
                int offset = 0;
                while (_carryLen - offset >= BytesPerPacket)
                {
                    SendOnePacket(bigger.AsSpan(offset, BytesPerPacket));
                    offset += BytesPerPacket;
                }
                int remaining = _carryLen - offset;
                Buffer.BlockCopy(bigger, offset, _carry, 0, remaining);
                _carryLen = remaining;
                return;
            }
            Buffer.BlockCopy(chunk, 0, _carry, _carryLen, chunk.Length);
            _carryLen += chunk.Length;
        }

        private void SendOnePacket(ReadOnlySpan<byte> audio)
        {
            // RTP-Header: 12 Byte fest. Marker im ersten Paket — Empfänger nimmt's als Start-Signal.
            Span<byte> packet = stackalloc byte[RtpHeaderLen + BytesPerPacket];
            packet[0] = 0x80;
            packet[1] = (byte)(_sequence == 0 ? (0x80 | RtpPayloadType) : RtpPayloadType);
            BinaryPrimitives.WriteUInt16BigEndian(packet[2..4], _sequence);
            BinaryPrimitives.WriteUInt32BigEndian(packet[4..8], _rtpTimestamp);
            BinaryPrimitives.WriteUInt32BigEndian(packet[8..12], _ssrc);

            // AES-CBC: nur volle 16-Byte-Blöcke verschlüsselt, Rest bleibt Klartext.
            int encLen = (audio.Length / 16) * 16;
            var encrypted = new byte[audio.Length];
            audio.CopyTo(encrypted);
            if (encLen > 0)
            {
                using var aes = Aes.Create();
                aes.Key = _aesKey;
                aes.IV = _aesIv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                using var enc = aes.CreateEncryptor();
                enc.TransformBlock(encrypted, 0, encLen, encrypted, 0);
            }
            encrypted.AsSpan().CopyTo(packet[RtpHeaderLen..]);

            try
            {
                _audioUdp!.Send(packet.ToArray(), packet.Length, _audioEndpoint!);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "RAOP: RTP-Send an {Host}:{Port} fehlgeschlagen", _device.Host, _serverAudioPort);
            }

            _sequence++;
            _rtpTimestamp += (uint)FramesPerPacket;
        }

        private async Task TimingLoopAsync(CancellationToken ct)
        {
            // Empfänger fragt per NTP-Style — wir antworten mit gültigem Timestamp.
            // Sehr stark vereinfachte Implementation; Sync ist mit AirPlay-1 nicht streng nötig.
            try
            {
                if (_timingUdp is null) return;
                while (!ct.IsCancellationRequested)
                {
                    var result = await _timingUdp.ReceiveAsync(ct).ConfigureAwait(false);
                    if (result.Buffer.Length < 32) continue;
                    var reply = new byte[32];
                    reply[0] = 0x80; reply[1] = 0xd3; reply[2] = 0x00; reply[3] = 0x07;
                    // Originate-Timestamp echo zurück (Bytes 24..31).
                    Buffer.BlockCopy(result.Buffer, 24, reply, 8, 8);
                    // Receive + Transmit-Timestamp = jetzt.
                    var now = NtpNow();
                    BinaryPrimitives.WriteUInt64BigEndian(reply.AsSpan(16, 8), now);
                    BinaryPrimitives.WriteUInt64BigEndian(reply.AsSpan(24, 8), now);
                    try { await _timingUdp.SendAsync(reply, reply.Length, result.RemoteEndPoint).ConfigureAwait(false); }
                    catch { /* ignore */ }
                }
            }
            catch (OperationCanceledException) { /* normal */ }
            catch (Exception ex) { _log.LogDebug(ex, "RAOP: timing-loop für {Host} beendet", _device.Host); }
        }

        private static ulong NtpNow()
        {
            // Unix-Epoche → NTP-Epoche (1900-01-01). 64-bit-Fixed-Point.
            var now = DateTimeOffset.UtcNow;
            var ntpSeconds = (ulong)now.ToUnixTimeSeconds() + 2208988800UL;
            var frac = (ulong)((now.Millisecond / 1000.0) * uint.MaxValue);
            return (ntpSeconds << 32) | (frac & 0xFFFFFFFFUL);
        }

        private string BuildSdp()
        {
            // RFC-4566-SDP + a=fmtp:96 mit ALAC-Pseudo-Header. Wir senden zwar L16-Payload,
            // aber Empfänger erwarten zumindest ein gültiges rtpmap-Statement.
            // Vereinfacht: wir deklarieren rtpmap als L16/44100/2.
            var localIp = GetLocalIpFor(_device.Host);
            var aesKeyEnc = RsaEncryptAesKey(_aesKey);
            var aesIvB64 = Base64UrlNoPad(_aesIv);
            var sessionId = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

            var sb = new StringBuilder();
            sb.Append($"v=0\r\n");
            sb.Append($"o=iTunes {sessionId} 0 IN IP4 {localIp}\r\n");
            sb.Append("s=iTunes\r\n");
            sb.Append($"c=IN IP4 {_device.Host}\r\n");
            sb.Append("t=0 0\r\n");
            sb.Append("m=audio 0 RTP/AVP 96\r\n");
            sb.Append("a=rtpmap:96 L16/44100/2\r\n");
            sb.Append("a=fmtp:96 352 0 16 40 10 14 2 255 0 0 44100\r\n");
            sb.Append($"a=rsaaeskey:{aesKeyEnc}\r\n");
            sb.Append($"a=aesiv:{aesIvB64}\r\n");
            return sb.ToString();
        }

        private static string GetLocalIpFor(string remoteHost)
        {
            try
            {
                using var u = new UdpClient();
                u.Connect(remoteHost, 65530);
                var ep = (IPEndPoint)u.Client.LocalEndPoint!;
                return ep.Address.ToString();
            }
            catch { return "0.0.0.0"; }
        }

        private static string RsaEncryptAesKey(byte[] aesKey)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(AirPortExpressPublicKeyPem.AsSpan());
            var encrypted = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA1);
            return Base64UrlNoPad(encrypted);
        }

        private static string Base64UrlNoPad(byte[] data)
            => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private async Task<string> SendRtspAsync(string method, string uri, IDictionary<string, string>? extraHeaders, string? body, CancellationToken ct)
        {
            if (_rtspStream is null) throw new InvalidOperationException("RTSP stream is null");
            _cseq++;
            var sb = new StringBuilder();
            sb.Append(method).Append(' ').Append(uri).Append(" RTSP/1.0\r\n");
            sb.Append("CSeq: ").Append(_cseq).Append("\r\n");
            sb.Append("User-Agent: wStreamAudio/1.0\r\n");
            sb.Append("Client-Instance: ").Append(_clientInstance).Append("\r\n");
            sb.Append("DACP-ID: ").Append(_dacpId).Append("\r\n");
            sb.Append("Active-Remote: ").Append(_activeRemote).Append("\r\n");
            if (!string.IsNullOrEmpty(_sessionCookie))
                sb.Append("Session: ").Append(_sessionCookie).Append("\r\n");
            byte[]? bodyBytes = null;
            if (body is not null)
            {
                bodyBytes = Encoding.UTF8.GetBytes(body);
                sb.Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n");
            }
            if (extraHeaders is not null)
            {
                foreach (var (k, v) in extraHeaders) sb.Append(k).Append(": ").Append(v).Append("\r\n");
            }
            sb.Append("\r\n");

            var headerBytes = Encoding.UTF8.GetBytes(sb.ToString());
            await _rtspStream.WriteAsync(headerBytes, ct).ConfigureAwait(false);
            if (bodyBytes is not null)
                await _rtspStream.WriteAsync(bodyBytes, ct).ConfigureAwait(false);

            _log.LogDebug("RAOP → {Method} {Uri} (CSeq {Cseq})", method, uri, _cseq);

            var response = await ReadRtspResponseAsync(ct).ConfigureAwait(false);
            // Status-Line prüfen.
            var firstNewline = response.IndexOf("\r\n", StringComparison.Ordinal);
            var statusLine = firstNewline > 0 ? response[..firstNewline] : response;
            _log.LogDebug("RAOP ← {Status}", statusLine);
            if (!statusLine.Contains(" 200 "))
            {
                throw new InvalidOperationException($"RTSP {method} an {_device.Host}: '{statusLine}'");
            }
            // Session-Cookie aus jeder Antwort einsammeln.
            ExtractSession(response);
            return response;
        }

        private async Task<string> ReadRtspResponseAsync(CancellationToken ct)
        {
            if (_rtspStream is null) throw new InvalidOperationException();
            var buffer = new byte[4096];
            var sb = new StringBuilder();
            int contentLength = 0;
            int headerEnd = -1;

            while (true)
            {
                int read = await _rtspStream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read <= 0) break;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                var text = sb.ToString();
                if (headerEnd < 0)
                {
                    headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (headerEnd > 0)
                    {
                        // Content-Length parsen.
                        foreach (var line in text[..headerEnd].Split("\r\n"))
                        {
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            {
                                if (int.TryParse(line.AsSpan("Content-Length:".Length).Trim(), out var cl)) contentLength = cl;
                            }
                        }
                    }
                }
                if (headerEnd > 0 && text.Length >= headerEnd + 4 + contentLength) break;
            }
            return sb.ToString();
        }

        private void ExtractSession(string response)
        {
            if (!string.IsNullOrEmpty(_sessionCookie)) return;
            foreach (var line in response.Split("\r\n"))
            {
                if (line.StartsWith("Session:", StringComparison.OrdinalIgnoreCase))
                {
                    var val = line["Session:".Length..].Trim();
                    var semi = val.IndexOf(';');
                    if (semi > 0) val = val[..semi];
                    _sessionCookie = val.Trim();
                    return;
                }
            }
        }

        private void ParseSetupResponse(string response)
        {
            foreach (var line in response.Split("\r\n"))
            {
                if (!line.StartsWith("Transport:", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = line["Transport:".Length..].Split(';');
                foreach (var p in parts)
                {
                    var t = p.Trim();
                    if (t.StartsWith("server_port=", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(t["server_port=".Length..], out _serverAudioPort);
                    else if (t.StartsWith("control_port=", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(t["control_port=".Length..], out _serverControlPort);
                    else if (t.StartsWith("timing_port=", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(t["timing_port=".Length..], out _serverTimingPort);
                }
            }
        }

        private static string RandomHex(int bytes)
        {
            var b = RandomNumberGenerator.GetBytes(bytes);
            return Convert.ToHexString(b);
        }

        public async ValueTask DisposeAsync()
        {
            try { _cts.Cancel(); } catch { /* ignore */ }
            try { _audioQueue.Writer.TryComplete(); } catch { /* ignore */ }
            if (_senderTask is not null) { try { await _senderTask.ConfigureAwait(false); } catch { } }
            if (_timingTask is not null) { try { await _timingTask.ConfigureAwait(false); } catch { } }
            _audioUdp?.Dispose();
            _controlUdp?.Dispose();
            _timingUdp?.Dispose();
            _rtspStream?.Dispose();
            _rtsp?.Dispose();
            _cts.Dispose();
        }
    }
}
