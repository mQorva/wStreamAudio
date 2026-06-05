namespace wStreamAudio.Core.Models;

public enum PlayerKind
{
    Squeeze,
    AirPlayBridge,
    UpnpBridge,
    LocalPc,
    /// <summary>Direkt angesteuerter DLNA/UPnP-Renderer (kein LMS-Player).</summary>
    Dlna,
    /// <summary>Direkt angesteuerter AirPlay-Empfänger (kein LMS-Player).</summary>
    AirPlay,
    Unknown
}
