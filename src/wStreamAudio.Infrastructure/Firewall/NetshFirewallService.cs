using System.Diagnostics;
using Microsoft.Extensions.Logging;
using wStreamAudio.Core.Abstractions;

namespace wStreamAudio.Infrastructure.Firewall;

/// <summary>
/// Setzt eine eingehende TCP-Regel via netsh. Erfordert Admin-Rechte —
/// wir starten netsh elevated; bei Ablehnung gibt es nur ein Log-Warning.
/// </summary>
public sealed class NetshFirewallService : IFirewallService
{
    private readonly ILogger<NetshFirewallService> _log;
    public NetshFirewallService(ILogger<NetshFirewallService> log) { _log = log; }

    public async Task EnsureInboundRuleAsync(string ruleName, int port, CancellationToken ct = default)
    {
        // Erst prüfen, ob die Regel schon existiert — netsh show ist read-only und braucht
        // keinen UAC-Prompt. Wenn sie da ist, kein Add nötig (kein lästiger Dialog).
        if (await DoesRuleExistAsync(ruleName, ct).ConfigureAwait(false)) return;

        // Programmbasierte Regel auf die Exe — robuster als rein port-basiert, weil Windows
        // dann auch den App-Firewall-Popup beim ersten Listen unterdrückt. Plus zusätzlich
        // den konkreten TCP-Port erlauben, falls jemand explizit nach Port filtert.
        var exePath = Environment.ProcessPath ?? string.Empty;
        var args = string.IsNullOrEmpty(exePath)
            ? $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port} profile=any"
            : $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port} profile=any program=\"{exePath}\"";

        RunNetshElevated(args, ct);
    }

    public Task RemoveRuleAsync(string ruleName, CancellationToken ct = default)
    {
        RunNetshElevated($"advfirewall firewall delete rule name=\"{ruleName}\"", ct);
        return Task.CompletedTask;
    }

    private static async Task<bool> DoesRuleExistAsync(string ruleName, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", $"advfirewall firewall show rule name=\"{ruleName}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private void RunNetshElevated(string args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", args)
            {
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Firewall-Regel konnte nicht gesetzt werden — manuell ausführen: netsh {Args}", args);
        }
    }
}
