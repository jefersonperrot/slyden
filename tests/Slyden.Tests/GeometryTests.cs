using Slyden.Core.Config;
using Slyden.Core.Render;

namespace Slyden.Tests;

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

    [Theory]
    [InlineData(EffectKind.PanLr, 2048, 1365, 16.0 / 9, 0.15)]
    [InlineData(EffectKind.PanRl, 2048, 1365, 16.0 / 9, 0.15)]
    [InlineData(EffectKind.TiltUp, 2048, 1365, 16.0 / 9, 0.15)]
    [InlineData(EffectKind.TiltDown, 2048, 1365, 16.0 / 9, 0.3)]
    [InlineData(EffectKind.PanLr, 1365, 2048, 1365.0 / 2048, 0.15)]   // vertical inteira (janela com a proporção da foto)
    [InlineData(EffectKind.TiltUp, 2048, 1365, 9.0 / 16, 0.15)]       // 3:2 recortada em 9:16
    [InlineData(EffectKind.PanLr, 6000, 4000, 16.0 / 9, 0.05)]
    public void Pan_and_tilt_use_the_whole_free_range_without_being_clamped(
        EffectKind effect, int w, int h, double aspect, double intensity)
    {
        var m = SlideResolver.Preset(effect, intensity, w, h, aspect);
        var v = SlideGeometry.View(w, h, aspect, m.Zoom0, .5, .5);

        // o centro pedido nas duas pontas já cabe na foto: nada é cortado pela borda, então não há "arranque"
        foreach (var (cx, cy) in new[] { (m.Cx0, m.Cy0), (m.Cx1, m.Cy1) })
        {
            double wantX = cx * w - v.Width / 2, wantY = cy * h - v.Height / 2;
            var got = SlideGeometry.View(w, h, aspect, m.Zoom0, cx, cy);
            Assert.Equal(wantX, got.X0, 6);
            Assert.Equal(wantY, got.Y0, 6);
        }

        // e a janela vai de uma borda da foto à outra no eixo do movimento
        var start = SlideGeometry.View(w, h, aspect, m.Zoom0, m.Cx0, m.Cy0);
        var end = SlideGeometry.View(w, h, aspect, m.Zoom1, m.Cx1, m.Cy1);
        bool horizontal = effect is EffectKind.PanLr or EffectKind.PanRl;
        double travelled = horizontal ? Math.Abs(end.X0 - start.X0) : Math.Abs(end.Y0 - start.Y0);
        double free = horizontal ? w - v.Width : h - v.Height;
        Assert.Equal(free, travelled, 6);
    }

    [Fact]
    public void Pan_moves_from_the_first_frame_with_no_dead_zone()
    {
        var m = SlideResolver.Preset(EffectKind.PanLr, 0.15, 2048, 1365, 16.0 / 9);
        double X(double t)
        {
            double e = SlideGeometry.Ease(t);
            return SlideGeometry.View(2048, 1365, 16.0 / 9, m.Zoom0,
                SlideGeometry.Lerp(m.Cx0, m.Cx1, e), SlideGeometry.Lerp(m.Cy0, m.Cy1, e)).X0;
        }

        // com a curva suave, 10% do tempo já anda algo, e o deslocamento nunca fica parado e arranca de repente
        Assert.True(X(0.1) > X(0.0));
        double biggestStep = 0;
        for (int k = 1; k <= 60; k++) biggestStep = Math.Max(biggestStep, X(k / 60.0) - X((k - 1) / 60.0));
        double total = X(1) - X(0);
        Assert.True(biggestStep < total * 0.05, $"passo máximo {biggestStep} vs total {total}");
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
