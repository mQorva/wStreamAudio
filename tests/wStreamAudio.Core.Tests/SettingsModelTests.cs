using System.Text.Json;
using System.Text.Json.Serialization;
using wStreamAudio.Core.Models;
using wStreamAudio.Core.Settings;
using Xunit;

namespace wStreamAudio.Core.Tests;

public class SettingsModelTests
{
    private static JsonSerializerOptions Options => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public void Roundtrip_preserves_player_trim_and_capture_profiles()
    {
        var model = new SettingsModel();
        model.Players.Add(new PersistedPlayer
        {
            Id = "00:04:20:11:22:33",
            CustomName = "Wohnzimmer",
            AppControlsVolume = true,
            TrimPercent = 80,
            InActiveSyncGroup = true
        });
        model.CaptureProfiles.Add(new CaptureProfile
        {
            Name = "SPDIF Hi-Q",
            Mode = CaptureMode.EndpointLoopback,
            FollowDefaultEndpoint = false,
            EndpointId = "{0.0.0.00000000}.{abc}",
            EndpointDisplayName = "SPDIF Out",
            SampleRate = 48000
        });
        model.ActiveCaptureProfileId = model.CaptureProfiles[0].Id;

        var json = JsonSerializer.Serialize(model, Options);
        var roundTrip = JsonSerializer.Deserialize<SettingsModel>(json, Options);

        Assert.NotNull(roundTrip);
        Assert.Single(roundTrip!.Players);
        Assert.Equal(80, roundTrip.Players[0].TrimPercent);
        Assert.True(roundTrip.Players[0].AppControlsVolume);
        Assert.Equal("Wohnzimmer", roundTrip.Players[0].CustomName);
        Assert.Single(roundTrip.CaptureProfiles);
        Assert.Equal(CaptureMode.EndpointLoopback, roundTrip.CaptureProfiles[0].Mode);
        Assert.Equal(48000, roundTrip.CaptureProfiles[0].SampleRate);
        Assert.Equal(roundTrip.CaptureProfiles[0].Id, roundTrip.ActiveCaptureProfileId);
    }
}
