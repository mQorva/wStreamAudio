using wStreamAudio.Core.Volume;
using Xunit;

namespace wStreamAudio.Core.Tests;

public class VolumeMathTests
{
    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(60, 60)]
    [InlineData(100, 100)]
    [InlineData(130, 100)]
    public void ClampVolume_limits_to_lms_range(int input, int expected)
    {
        Assert.Equal(expected, VolumeMath.ClampVolume(input));
    }
}
