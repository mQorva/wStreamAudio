namespace wStreamAudio.Infrastructure.Logging;

/// <summary>
/// Single-File-Logger nach <c>%LOCALAPPDATA%/{DataFolderName}/logs/{AppName}.log</c>.
/// Identisch zu allen anderen WinUI-3-Apps in unserem Bestand (Vereinheitlichung).
/// Schreibt synchron append-only; Logging darf keinen Folgefehler auslösen.
/// </summary>
public static class AppLogger
{
    private static string _appName = "wStreamAudio";
    private static string _dataFolder = "wStreamAudio";

    public static void Configure(string appName, string dataFolderName)
    {
        _appName = appName;
        _dataFolder = dataFolderName;
    }

    public static void WriteMessage(string message)
    {
        try
        {
            var path = LogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} [app]    {message}{Environment.NewLine}");
        }
        catch { /* Logging darf nicht crashen. */ }
    }

    public static void Write(Exception exception)
    {
        try
        {
            var path = LogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(
                path,
                $"{DateTimeOffset.Now:O} [error]  {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* siehe oben */ }
    }

    private static string LogPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        _dataFolder,
        "logs",
        $"{_appName}.log");
}
