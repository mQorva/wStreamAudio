using wStreamAudio.Core.Volume;
using Xunit;

namespace wStreamAudio.Core.Tests;

public class VolumeMathTests
{
    [Theory]
    [InlineData(50, 100, 50)]   // Trim 100 % = exakt System
    [InlineData(50, 80, 40)]    // Trim 80 %
    [InlineData(70, 80, 56)]    // Beispiel aus Plan
    [InlineData(50, 130, 65)]   // Trim >100 % zur Kompensation
    [InlineData(0, 100, 0)]     // System stumm bleibt stumm
    [InlineData(100, 100, 100)] // Volle Lautstärke
    [InlineData(80, 150, 100)]  // Clamping bei 100
    public void EffectiveVolume_returns_clamped_product(int system, int trim, int expected)
    {
        Assert.Equal(expected, VolumeMath.EffectiveVolume(system, trim));
    }

    [Theory]
    [InlineData(40, 50, 80)]    // Player 40 / System 50 = 80 % Trim
    [InlineData(56, 70, 80)]
    [InlineData(0, 50, 0)]
    [InlineData(50, 0, 100)]    // System stumm — Default-Trim
    public void RecoverTrim_inverts_effective(int playerVol, int system, int expectedTrim)
    {
        Assert.Equal(expectedTrim, VolumeMath.RecoverTrim(playerVol, system));
    }

    [Fact]
    public void RecoverTrim_clamps_to_max()
    {
        // 100/30 wäre ~333; muss auf den TrimMax-Wert (Defaults.PlayerTrimMax) begrenzt werden.
        Assert.Equal(wStreamAudio.Core.Models.Defaults.PlayerTrimMax, VolumeMath.RecoverTrim(100, 30));
    }
}
