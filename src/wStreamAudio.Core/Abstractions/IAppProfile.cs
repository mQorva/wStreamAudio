namespace wStreamAudio.Core.Abstractions;

public interface IAppProfile
{
    string AppName { get; }
    string DataFolderName { get; }
    string AuthorName { get; }
    string CopyrightText { get; }
    string LicenseName { get; }
    string MutexName { get; }
    string AumId { get; }
    string AutostartRegistryValueName { get; }
    string SingleInstancePipeName { get; }
}
