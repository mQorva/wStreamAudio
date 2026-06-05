using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Infrastructure.Audio;
using wStreamAudio.Infrastructure.Autostart;
using wStreamAudio.Infrastructure.Firewall;
using wStreamAudio.Infrastructure.Dlna;
using wStreamAudio.Infrastructure.Lms;
using wStreamAudio.Infrastructure.Settings;
using wStreamAudio.Infrastructure.SingleInstance;
using wStreamAudio.Infrastructure.Streaming;
using wStreamAudio.Infrastructure.Volume;
using wStreamAudio.Profile;
using wStreamAudio.Services;

namespace wStreamAudio;

internal static class ServiceConfigurator
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug());

        // Profile / App-Identität
        services.AddSingleton<IAppProfile, WStreamAudioProfile>();

        // Persistenz
        services.AddSingleton<ISettingsService, SettingsService>();

        // Plattform-Services
        services.AddSingleton<IAutostartService, WindowsAutostartService>();
        services.AddSingleton<ISingleInstance, NamedPipeSingleInstance>();
        services.AddSingleton<IFirewallService, NetshFirewallService>();
        services.AddSingleton<EndpointMonitor>();

        // Audio
        services.AddSingleton<IAudioEndpointCatalog, WindowsAudioEndpointCatalog>();
        services.AddSingleton<IAudioCapture, WasapiLoopbackSource>();

        // Streaming
        services.AddSingleton<IStreamServer, HttpStreamServer>();

        // LMS
        services.AddHttpClient<ILmsClient, LmsJsonRpcClient>();
        services.AddSingleton<IVolumeService, WindowsVolumeService>();
        services.AddSingleton<IPlayerStateBus, Infrastructure.Players.InProcessPlayerStateBus>();
        services.AddSingleton<IAirPlayDiscovery, Infrastructure.AirPlay.AirPlayDiscovery>();
        services.AddSingleton<IAirPlaySender, Infrastructure.AirPlay.RaopSender>();

        // DLNA (native Steuerung von MediaRenderern)
        services.AddHttpClient<IDlnaService, DlnaService>();

        // Pipeline
        services.AddSingleton<StreamPipelineCoordinator>();

        return services.BuildServiceProvider();
    }
}
