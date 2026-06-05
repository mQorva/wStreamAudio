namespace wStreamAudio.Infrastructure.Streaming;

/// <summary>
/// Stream-Server-Events landen in der gemeinsamen App-Log-Datei
/// %LOCALAPPDATA%\wStreamAudio\logs\wStreamAudio.log mit Kategorie-Prefix [stream].
/// </summary>
public static class StreamLog
{
    public static void Write(string message)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "wStreamAudio",
                "logs",
                "wStreamAudio.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} [stream] {message}{Environment.NewLine}");
        }
        catch { /* ignore */ }
    }
}
