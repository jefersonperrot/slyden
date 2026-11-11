using SkiaSharp;
using Slyden.Core.Config;
using Slyden.Core.Render;

namespace Slyden.Tests;

public class ScaleAndResolutionTests
{
    private const int W = 320, H = 180;
    private static readonly Motion Still = new(1, 1, .5, .5, .5, .5);

    private static SKBitmap Solid(int w, int h, SKColor color)
    {
        var b = new SKBitmap(w, h);
        using var c = new SKCanvas(b);
        c.Clear(color);
        return b;
    }

    private static SKBitmap Draw(double scale, bool blur)
    {
        using var slide = new SlideRenderer(Solid(300, 200, SKColors.Red), Still, W, H, scale: scale, blurBackground: blur);
        var frame = new SKBitmap(W, H);
        using var canvas = new SKCanvas(frame);
        canvas.Clear(SKColors.Magenta);
        slide.Draw(canvas, 0);
        return frame;
    }

    private static SKColor PixelBelowPhoto(double shadow)
    {
        // fundo preto liso e foto branca a 50%: o pixel logo abaixo da foto só escurece se houver sombra
        using var slide = new SlideRenderer(Solid(300, 200, SKColors.White), Still, W, H, scale: 0.5, blurBackground: false, shadow: shadow);
        using var frame = new SKBitmap(W, H);
        using var canvas = new SKCanvas(frame);
        slide.Draw(canvas, 0);
        return frame.GetPixel(W / 2, H / 2 + (int)(H * 0.25) + 3);
    }

    [Fact]
    public void Shadow_darkens_the_area_just_outside_the_photo()
    {
        Assert.Equal(0, PixelBelowPhoto(0).Red);
        Assert.Equal(0, PixelBelowPhoto(1).Red); // sobre fundo preto continua preto
    }

    [Fact]
    public void Shadow_shows_over_a_blurred_background()
    {
        using var noShadow = new SlideRenderer(Solid(300, 200, SKColors.White), Still, W, H, scale: 0.5, shadow: 0);
        using var withShadow = new SlideRenderer(Solid(300, 200, SKColors.White), Still, W, H, scale: 0.5, shadow: 1);
        using var a = new SKBitmap(W, H);
        using var b = new SKBitmap(W, H);
        using (var c = new SKCanvas(a)) noShadow.Draw(c, 0);
        using (var c = new SKCanvas(b)) withShadow.Draw(c, 0);
        int y = H / 2 + (int)(H * 0.25) + 3;
        Assert.True(b.GetPixel(W / 2, y).Red < a.GetPixel(W / 2, y).Red - 20);
        Assert.Equal(a.GetPixel(3, 3), b.GetPixel(3, 3)); // longe da foto não muda
    }

    [Fact]
    public void Solid_color_background_fills_the_borders()
    {
        using var slide = new SlideRenderer(Solid(300, 200, SKColors.Red), Still, W, H, scale: 0.5,
                                            blurBackground: false, fillColor: new SKColor(0x20, 0x40, 0x80));
        using var frame = new SKBitmap(W, H);
        using var canvas = new SKCanvas(frame);
        slide.Draw(canvas, 0);
        var edge = frame.GetPixel(3, 3);
        Assert.Equal((0x20, 0x40, 0x80), (edge.Red, edge.Green, edge.Blue));
    }

    [Fact]
    public void Config_rejects_an_invalid_background_color()
    {
        var cfg = new SlideshowConfig();
        cfg.Default.BackgroundColor = "azul";
        Assert.Throws<InvalidDataException>(cfg.Validate);
    }

    [Fact]
    public void Full_scale_fills_the_frame_with_the_photo()
    {
        using var frame = Draw(1, true);
        Assert.Equal(SKColors.Red, frame.GetPixel(2, 2));
        Assert.Equal(SKColors.Red, frame.GetPixel(W / 2, H / 2));
    }

    [Fact]
    public void Reduced_scale_shows_photo_in_the_middle_and_black_edges()
    {
        using var frame = Draw(0.5, blur: false);
        Assert.Equal(SKColors.Red, frame.GetPixel(W / 2, H / 2));
        Assert.Equal(new SKColor(0, 0, 0), frame.GetPixel(3, 3).WithAlpha(255));
        Assert.Equal(new SKColor(0, 0, 0), frame.GetPixel(W - 4, H - 4).WithAlpha(255));
    }

    [Fact]
    public void Reduced_scale_with_blur_uses_a_darkened_copy_of_the_photo_behind()
    {
        using var frame = Draw(0.5, blur: true);
        var edge = frame.GetPixel(3, 3);
        Assert.True(edge.Red > 200 && edge.Red < 250, $"vermelho levemente escurecido esperado, veio {edge}");
        Assert.True(edge.Green < 30 && edge.Blue < 30);
        Assert.Equal(SKColors.Red, frame.GetPixel(W / 2, H / 2));
    }

    [Fact]
    public void Scale_shrinks_the_photo_proportionally()
    {
        using var frame = Draw(0.5, blur: false);
        // quadro 320x180, foto ocupa metade da largura/altura: x entre 80 e 240
        Assert.Equal(SKColors.Red, frame.GetPixel(90, H / 2));
        Assert.Equal(new SKColor(0, 0, 0), frame.GetPixel(70, H / 2).WithAlpha(255));
    }

    [Fact]
    public void Resolver_applies_default_and_per_photo_scale_and_background()
    {
        var cfg = new SlideshowConfig
        {
            Default = { Scale = 0.8, Background = BackgroundKind.Black },
            Photos = [new PhotoSpec { File = "a.jpg" }, new PhotoSpec { File = "b.jpg", Scale = 0.6, Background = BackgroundKind.Blur }],
        };
        var slides = SlideResolver.Resolve(cfg);
        Assert.Equal((0.8, BackgroundKind.Black), (slides[0].Scale, slides[0].Background));
        Assert.Equal((0.6, BackgroundKind.Blur), (slides[1].Scale, slides[1].Background));
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(1.2)]
    public void Invalid_scale_is_rejected(double scale)
    {
        Assert.Throws<InvalidDataException>(new SlideshowConfig { Default = { Scale = scale } }.Validate);
        Assert.Throws<InvalidDataException>(new SlideshowConfig { Photos = [new PhotoSpec { File = "a.jpg", Scale = scale }] }.Validate);
    }

    [Theory]
    [InlineData("16:9", 720, 1280, 720)]
    [InlineData("16:9", 1080, 1920, 1080)]
    [InlineData("16:9", 1440, 2560, 1440)]
    [InlineData("16:9", 2160, 3840, 2160)]
    [InlineData("9:16", 720, 720, 1280)]
    [InlineData("1:1", 1440, 1440, 1440)]
    public void Resolution_sets_the_short_side(string format, int resolution, int w, int h)
    {
        Assert.Equal((w, h), VideoFormat.SizeOf(format, resolution));
    }

    [Fact]
    public void Unknown_resolution_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => VideoFormat.SizeOf("16:9", 999));
        Assert.Throws<InvalidDataException>(new SlideshowConfig { Resolution = 999 }.Validate);
    }

    [Fact]
    public void Scale_and_resolution_survive_json_round_trip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sr_{Guid.NewGuid():N}.json");
        try
        {
            new SlideshowConfig
            {
                Resolution = 1440, Default = { Scale = 0.8, Background = BackgroundKind.Black },
                Photos = [new PhotoSpec { File = "a.jpg", Scale = 0.6, Background = BackgroundKind.Blur }],
            }.Save(path);
            var back = SlideshowConfig.Load(path);
            Assert.Equal(1440, back.Resolution);
            Assert.Equal(0.8, back.Default.Scale);
            Assert.Equal(BackgroundKind.Black, back.Default.Background);
            Assert.Equal(0.6, back.Photos[0].Scale);
            Assert.Equal(BackgroundKind.Blur, back.Photos[0].Background);
        }
        finally { File.Delete(path); }
    }
}
