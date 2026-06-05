namespace wStreamAudio.Core.Models;

public enum CaptureMode
{
    EndpointLoopback,
    ProcessLoopback
}

public enum ProcessLoopbackMode
{
    Include,
    Exclude
}

public sealed class CaptureProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Default Speakers";
    public CaptureMode Mode { get; set; } = CaptureMode.EndpointLoopback;

    /// <summary>Bei EndpointLoopback: Wenn true, folgt das Profil dem System-Default-Render-Endpoint.</summary>
    public bool FollowDefaultEndpoint { get; set; } = true;

    /// <summary>Bei EndpointLoopback (ohne FollowDefault): konkrete Endpoint-ID aus MMDevice.</summary>
    public string? EndpointId { get; set; }

    /// <summary>Anzeigename des Endpoints zur Wiedererkennung in der UI bei nicht erreichbarem Gerät.</summary>
    public string? EndpointDisplayName { get; set; }

    /// <summary>Bei ProcessLoopback: Prozessname (z.B. "Spotify"). Wird zur Laufzeit zur PID aufgelöst.</summary>
    public string? ProcessName { get; set; }

    public ProcessLoopbackMode ProcessMode { get; set; } = ProcessLoopbackMode.Include;

    /// <summary>0 = Auto (Mix-Format/Resample-Quelle); sonst 44100 oder 48000.</summary>
    public int SampleRate { get; set; }
}
