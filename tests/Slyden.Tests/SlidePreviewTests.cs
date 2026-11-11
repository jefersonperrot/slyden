using SkiaSharp;
using Slyden.Core.Config;
using Slyden.Core.Render;

namespace Slyden.Tests;

public class SlidePreviewTests
{
    private static string MakePhotoFolder(out string file)
    {
        string folder = Path.Combine(Path.GetTempPath(), $"sl_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        file = "a.jpg";
        using var bmp = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(new SKColor(200, 60, 40));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 90);
        File.WriteAllBytes(Path.Combine(folder, file), data.ToArray());
        return folder;
    }

    [Theory]
    [InlineData("16:9", 960, 540)]
    [InlineData("9:16", 540, 960)]
    [InlineData("1:1", 540, 540)]
    public void Frame_has_scaled_format_size(string format, int w, int h)
    {
        string folder = MakePhotoFolder(out string file);
        try
        {
            var cfg = new SlideshowConfig { Format = format, Photos = [new PhotoSpec { File = file }] };
            using var frame = SlidePreview.Frame(cfg, folder, 0, 0.5, 0.5);
            Assert.Equal((w, h), (frame.Width, frame.Height));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Frame_uses_custom_photo_loader()
    {
        var cfg = new SlideshowConfig { Photos = [new PhotoSpec { File = "nao-existe.jpg", Effect = EffectKind.None }] };
        using var frame = SlidePreview.Frame(cfg, "pasta", 0, 0, 0.25, null, _ =>
        {
            var b = new SKBitmap(64, 36);
            using var c = new SKCanvas(b);
            c.Clear(new SKColor(10, 200, 10));
            return b;
        });
        var px = frame.GetPixel(frame.Width / 2, frame.Height / 2);
        Assert.True(px.Green > 150 && px.Red < 80);
    }

    [Fact]
    public void Clone_is_independent()
    {
        var cfg = new SlideshowConfig { Photos = [new PhotoSpec { File = "a.jpg", Length = 2 }] };
        var copy = cfg.Clone();
        copy.Photos[0].Length = 4;
        copy.Format = "1:1";
        Assert.Equal(2, cfg.Photos[0].Length);
        Assert.Equal("16:9", cfg.Format);
    }

    [Fact]
    public void Out_of_range_slide_is_rejected()
    {
        var cfg = new SlideshowConfig { Photos = [new PhotoSpec { File = "a.jpg" }] };
        Assert.Throws<ArgumentOutOfRangeException>(() => SlidePreview.Frame(cfg, "x", 3, 0, 0.5, null, _ => new SKBitmap(4, 4)));
    }
}
