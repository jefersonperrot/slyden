using Slyden.Core.Config;
using Slyden.Core.Render;

namespace Slyden.Tests;

public class FixedTimeAndSquareTests
{
    [Fact]
    public void Square_format_is_1080_by_1080()
    {
        Assert.True(VideoFormat.IsKnown("1:1"));
        Assert.Equal((1080, 1080), VideoFormat.SizeOf("1:1"));
    }

    [Fact]
    public void Seconds_per_photo_round_trips_through_json()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sb_{Guid.NewGuid():N}.json");
        try
        {
            new SlideshowConfig
            {
                Format = "1:1", SecondsPerPhoto = 6, FadeSeconds = 1.5,
                Photos = [new PhotoSpec { File = "a.jpg" }],
            }.Save(path);
            var back = SlideshowConfig.Load(path);
            Assert.Equal(6, back.SecondsPerPhoto);
            Assert.Equal(1.5, back.FadeSeconds);
            Assert.Equal("1:1", back.Format);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Non_positive_seconds_are_rejected(double seconds)
    {
        var cfg = new SlideshowConfig { SecondsPerPhoto = seconds };
        Assert.Throws<InvalidDataException>(cfg.Validate);
    }

    [Fact]
    public void Non_positive_fade_seconds_are_rejected()
    {
        var cfg = new SlideshowConfig { FadeSeconds = 0 };
        Assert.Throws<InvalidDataException>(cfg.Validate);
    }

    [Fact]
    public void Length_two_slide_lasts_twice_the_fixed_time()
    {
        double[] cuts = [0, 5, 10, 15, 20];
        var boundaries = SlideshowRenderer.Boundaries(cuts, [2, 1, 1]);
        Assert.Equal([0.0, 10, 15, 20], boundaries);
    }
}
