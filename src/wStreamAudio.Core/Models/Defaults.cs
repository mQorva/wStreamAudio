namespace wStreamAudio.Core.Models;

public static class Defaults
{
    public const int LmsHttpPort = 9000;
    public const int StreamHttpPort = 8721;
    public const string StreamPath = "/stream.mp3";

    public const int PlayerTrimMin = 0;
    public const int PlayerTrimMax = 100;
    public const int PlayerTrimDefault = 100;

    public const int SettingsAutoSaveDebounceMs = 300;
}
