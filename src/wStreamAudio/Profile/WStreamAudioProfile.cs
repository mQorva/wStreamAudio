using wStreamAudio.Core.Abstractions;

namespace wStreamAudio.Profile;

public sealed class WStreamAudioProfile : IAppProfile
{
    public string AppName => "wStreamAudio";
    public string DataFolderName => "wStreamAudio";
    public string AuthorName => "Ronny Schulz";
    public string CopyrightText => "Copyright 2026 Ronny Schulz";
    public string LicenseName => "MIT-Lizenz";
    public string MutexName => "Global\\wStreamAudio.SingleInstance";
    public string AumId => "RonnySchulz.wStreamAudio";
    public string AutostartRegistryValueName => "wStreamAudio";
    public string SingleInstancePipeName => "wStreamAudio.IPC";
}
