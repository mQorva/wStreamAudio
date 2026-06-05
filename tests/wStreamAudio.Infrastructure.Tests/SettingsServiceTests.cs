using Microsoft.Extensions.Logging.Abstractions;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Core.Models;
using wStreamAudio.Infrastructure.Settings;
using Xunit;

namespace wStreamAudio.Infrastructure.Tests;

public class SettingsServiceTests
{
    private sealed class TestProfile : IAppProfile
    {
        public string AppName => "wStreamAudio.Test";
        public string DataFolderName { get; set; } = $"wStreamAudio.Test.{Guid.NewGuid():N}";
        public string AuthorName => "Test";
        public string CopyrightText => "Test";
        public string LicenseName => "MIT";
        public string MutexName => "Global\\test";
        public string AumId => "Test.AumId";
        public string AutostartRegistryValueName => "Test";
        public string SingleInstancePipeName => "test.pipe";
    }

    [Fact]
    public async Task LoadAsync_returns_defaults_when_file_missing()
    {
        var profile = new TestProfile();
        var sut = new SettingsService(profile, NullLogger<SettingsService>.Instance);
        var model = await sut.LoadAsync();
        Assert.NotNull(model);
        Assert.Empty(model.Players);
        Assert.Empty(model.CaptureProfiles);
    }

    [Fact]
    public async Task SaveAsync_then_LoadAsync_roundtrips_player_state()
    {
        var profile = new TestProfile();
        try
        {
            var sut = new SettingsService(profile, NullLogger<SettingsService>.Instance);
            var model = await sut.LoadAsync();
            model.Players.Add(new PersistedPlayer
            {
                Id = "aa:bb:cc:dd:ee:ff",
                CustomName = "Küche",
                AppControlsVolume = true,
                TrimPercent = 75
            });
            await sut.SaveAsync();

            var sut2 = new SettingsService(profile, NullLogger<SettingsService>.Instance);
            var loaded = await sut2.LoadAsync();
            Assert.Single(loaded.Players);
            Assert.Equal("Küche", loaded.Players[0].CustomName);
            Assert.Equal(75, loaded.Players[0].TrimPercent);
            Assert.True(loaded.Players[0].AppControlsVolume);
        }
        finally
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                profile.DataFolderName);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
