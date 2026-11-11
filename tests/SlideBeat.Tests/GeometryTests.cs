using SlideBeat.Core.Config;
using SlideBeat.Core.Render;

namespace SlideBeat.Tests;

public class GeometryTests
{
    [Theory]
    [InlineData(2048, 1365, 1920, 1080, true)]   // 3:2 em 16:9: preenche (corta um pouco)
    [InlineData(1920, 1080, 1920, 1080, true)]
    [InlineData(1365, 2048, 1920, 1080, false)]  // vertical em 16:9: inteira, com fundo
    [InlineData(1365, 2048, 1080, 1920, true)]   // 2:3 em 9:16: preenche
    [InlineData(2048, 1365, 1080, 1920, false)]  // horizontal em 9:16: inteira, com fundo
    [InlineData(1638, 2048, 1080, 1920, false)]  // 4:5 em 9:16: inteira, com fundo
    public void Layout_fills_only_when_aspect_is_close_to_the_frame(int iw, int ih, int fw, int fh, bool fill)
    {
        Assert.Equal(fill, SlideGeometry.Layout(iw, ih, fw, fh).Fill);
    }

    [Fact]
    public void Portrait_photo_in_landscape_frame_is_centered_at_full_height()
    {
        var l = SlideGeometry.Layout(1000, 1500, 1920, 1080);
        Assert.False(l.Fill);
        Assert.Equal(1080, l.Height);
        Assert.Equal(720, l.Width);
        Assert.Equal((1920 - 720) / 2, l.OffsetX);
        Assert.Equal(0, l.OffsetY);
    }

    [Fact]
    public void Landscape_photo_in_vertical_frame_is_centered_at_full_width()
    {
        var l = SlideGeometry.Layout(2000, 1000, 1080, 1920);
        Assert.False(l.Fill);
        Assert.Equal(1080, l.Width);
        Assert.Equal(540, l.Height);
        Assert.Equal(0, l.OffsetX);
        Assert.Equal((1920 - 540) / 2, l.OffsetY);
    }

    [Theory]
    [InlineData(1.0, .5, .5)]
    [InlineData(1.3, .0, .0)]   // centro fora: a janela é presa à borda
    [InlineData(1.3, 1.0, 1.0)]
    [InlineData(2.0, .2, .8)]
    public void View_keeps_the_aspect_and_stays_inside_the_photo(double zoom, double cx, double cy)
    {
        const int w = 2048, h = 1365;
        double aspect = 16.0 / 9;
        var v = SlideGeometry.View(w, h, aspect, zoom, cx, cy);

        Assert.Equal(aspect, v.Width / v.Height, 6);
        Assert.InRange(v.X0, -1e-9, w);
        Assert.InRange(v.Y0, -1e-9, h);
        Assert.True(v.X1 <= w + 1e-9 && v.Y1 <= h + 1e-9);
    }

    [Fact]
    public void Zoom_shrinks_the_visible_area()
    {
        var a = SlideGeometry.View(2000, 1000, 2.0, 1.0, .5, .5);
        var b = SlideGeometry.View(2000, 1000, 2.0, 2.0, .5, .5);
        Assert.Equal(a.Width / 2, b.Width, 6);
    }

    [Fact]
    public void Ease_goes_from_0_to_1_smoothly()
    {
        Assert.Equal(0, SlideGeometry.Ease(0));
        Assert.Equal(1, SlideGeometry.Ease(1));
        Assert.Equal(.5, SlideGeometry.Ease(.5), 9);
        Assert.True(SlideGeometry.Ease(.1) < .1); // começa devagar
    }

    [Theory]
    [InlineData("16:9", 1920, 1080)]
    [InlineData("9:16", 1080, 1920)]
    public void Video_formats_have_the_expected_size(string format, int w, int h)
    {
        Assert.Equal((w, h), VideoFormat.SizeOf(format));
    }
}
