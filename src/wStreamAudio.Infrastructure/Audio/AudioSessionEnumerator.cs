using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace wStreamAudio.Infrastructure.Audio;

/// <summary>
/// Liefert eine Liste der Apps, die gerade auf dem Default-Render-Endpoint Audio
/// abspielen oder abgespielt haben (WASAPI-Sessions). Wird vom Profil-Editor genutzt,
/// damit der User den Prozessnamen aus einer echten Liste wählen kann statt zu raten.
/// </summary>
public static class AudioSessionEnumerator
{
    public static IReadOnlyList<RunningAudioApp> EnumerateActive()
    {
        var seen = new Dictionary<string, RunningAudioApp>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                uint pid;
                try { pid = session.GetProcessID; }
                catch { continue; }
                if (pid == 0) continue;

                string name = string.Empty;
                string display = session.DisplayName ?? string.Empty;
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    name = p.ProcessName;
                    if (string.IsNullOrEmpty(display)) display = p.MainWindowTitle;
                    if (string.IsNullOrEmpty(display)) display = name;
                }
                catch { /* Prozess inzwischen weg, weiter */ }

                if (string.IsNullOrEmpty(name)) continue;
                if (seen.ContainsKey(name)) continue;

                seen[name] = new RunningAudioApp
                {
                    ProcessName = name,
                    DisplayName = display,
                    IsActiveAudio = session.State == AudioSessionState.AudioSessionStateActive,
                };
            }
        }
        catch
        {
            // WASAPI nicht verfügbar — leere Liste, der User kann den Namen weiterhin tippen.
        }

        return seen.Values.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public sealed class RunningAudioApp
{
    public required string ProcessName { get; init; }
    public required string DisplayName { get; init; }
    public bool IsActiveAudio { get; init; }
    public string SubLabel => IsActiveAudio ? $"{ProcessName}.exe · spielt gerade" : $"{ProcessName}.exe";
}
