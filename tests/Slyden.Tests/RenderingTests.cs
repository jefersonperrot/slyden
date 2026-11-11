using SkiaSharp;
using Slyden.Core.Render;

namespace Slyden.Tests;

public class RenderingTests
{
    private const int W = 192, H = 108;
    private static readonly Motion Still = new(1, 1, .5, .5, .5, .5);

    private static SKBitmap Solid(int w, int h, SKColor color)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(color);
        return bmp;
    }

    private static SKBitmap NewFrame() => new(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Opaque));

    private static void AssertColor(SKColor expected, SKColor actual, int tolerance = 4)
    {
        Assert.InRange(actual.Red, expected.Red - tolerance, expected.Red + tolerance);
        Assert.InRange(actual.Green, expected.Green - tolerance, expected.Green + tolerance);
        Assert.InRange(actual.Blue, expected.Blue - tolerance, expected.Blue + tolerance);
    }

    [Fact]
    public void Photo_with_similar_aspect_fills_the_whole_frame()
    {
        using var slide = new SlideRenderer(Solid(300, 200, SKColors.Red), Still, W, H);
        using var frame = NewFrame();
        using var canvas = new SKCanvas(frame);
        slide.Draw(canvas, 0);

        AssertColor(SKColors.Red, frame.GetPixel(0, 0));
        AssertColor(SKColors.Red, frame.GetPixel(W / 2, H / 2));
        AssertColor(SKColors.Red, frame.GetPixel(W - 1, H - 1));
    }

    [Fact]
    public void Portrait_photo_is_centered_over_a_darkened_blurred_background()
    {
        using var slide = new SlideRenderer(Solid(100, 200, SKColors.Blue), Still, W, H);
        using var frame = NewFrame();
        using var canvas = new SKCanvas(frame);
        slide.Draw(canvas, 0);

        AssertColor(SKColors.Blue, frame.GetPixel(W / 2, H / 2));               // a foto, intacta
        AssertColor(new SKColor(0, 0, 230), frame.GetPixel(5, 5), tolerance: 8); // fundo: azul ~10% mais escuro
    }

    [Fact]
    public void Fade_layer_blends_the_next_slide_over_the_current_one()
    {
        using var red = new SlideRenderer(Solid(300, 200, SKColors.Red), Still, W, H);
        using var blue = new SlideRenderer(Solid(300, 200, SKColors.Blue), Still, W, H);
        using var frame = NewFrame();
        using var canvas = new SKCanvas(frame);

        red.Draw(canvas, 0);
        using (var layer = new SKPaint { Color = new SKColor(255, 255, 255, 128) })
        {
            canvas.SaveLayer(layer);
            blue.Draw(canvas, 0);
            canvas.Restore();
        }

        AssertColor(new SKColor(127, 0, 128), frame.GetPixel(W / 2, H / 2), tolerance: 3);
    }

    [Fact]
    public void Zoom_in_changes_what_is_visible_over_time()
    {
        // metade esquerda vermelha, metade direita azul; pan da esquerda para a direita com zoom
        var bmp = new SKBitmap(400, 300, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.Red);
            using var blue = new SKPaint { Color = SKColors.Blue };
            c.DrawRect(SKRect.Create(200, 0, 200, 300), blue);
        }
        var pan = new Motion(2, 2, .25, .5, .75, .5);
        using var slide = new SlideRenderer(bmp, pan, W, H);
        using var frame = NewFrame();
        using var canvas = new SKCanvas(frame);

        slide.Draw(canvas, 0);
        AssertColor(SKColors.Red, frame.GetPixel(W / 2, H / 2));
        slide.Draw(canvas, 1);
        AssertColor(SKColors.Blue, frame.GetPixel(W / 2, H / 2));
    }

    // 4x2, só o pixel do canto superior esquerdo é vermelho
    private static SKBitmap Marked()
    {
        var bmp = Solid(4, 2, SKColors.Blue);
        bmp.SetPixel(0, 0, SKColors.Red);
        return bmp;
    }

    [Theory]
    [InlineData(SKEncodedOrigin.TopLeft, 4, 2, 0, 0)]
    [InlineData(SKEncodedOrigin.TopRight, 4, 2, 3, 0)]     // espelho horizontal
    [InlineData(SKEncodedOrigin.BottomRight, 4, 2, 3, 1)]  // 180°
    [InlineData(SKEncodedOrigin.BottomLeft, 4, 2, 0, 1)]   // espelho vertical
    [InlineData(SKEncodedOrigin.LeftTop, 2, 4, 0, 0)]      // transposta
    [InlineData(SKEncodedOrigin.RightTop, 2, 4, 1, 0)]     // 90° horário
    [InlineData(SKEncodedOrigin.RightBottom, 2, 4, 1, 3)]  // transversa
    [InlineData(SKEncodedOrigin.LeftBottom, 2, 4, 0, 3)]   // 90° anti-horário
    public void Exif_orientation_moves_the_marked_corner_to_the_right_place(
        SKEncodedOrigin origin, int newWidth, int newHeight, int redX, int redY)
    {
        using var oriented = ImageLoader.Orient(Marked(), origin);

        Assert.Equal(newWidth, oriented.Width);
        Assert.Equal(newHeight, oriented.Height);
        AssertColor(SKColors.Red, oriented.GetPixel(redX, redY), tolerance: 2);
        int reds = 0;
        for (int y = 0; y < oriented.Height; y++)
            for (int x = 0; x < oriented.Width; x++)
                if (oriented.GetPixel(x, y).Red > 200) reds++;
        Assert.Equal(1, reds);
    }
}
