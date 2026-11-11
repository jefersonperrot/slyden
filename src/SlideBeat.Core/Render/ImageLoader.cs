using SkiaSharp;

namespace SlideBeat.Core.Render;

public static class ImageLoader
{
    /// <summary>Carrega a foto já na orientação correta (respeitando o EXIF).</summary>
    public static SKBitmap Load(string path)
    {
        using var codec = SKCodec.Create(path)
            ?? throw new InvalidDataException($"Não foi possível ler a imagem: {path}");
        var info = codec.Info.WithColorType(SKColorType.Bgra8888).WithAlphaType(SKAlphaType.Premul);
        var bitmap = SKBitmap.Decode(codec, info)
            ?? throw new InvalidDataException($"Não foi possível decodificar a imagem: {path}");
        return Orient(bitmap, codec.EncodedOrigin);
    }

    /// <summary>Aplica a orientação do EXIF; devolve o mesmo bitmap se não houver o que girar.</summary>
    internal static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft) return source;

        int w = source.Width, h = source.Height;
        bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                           or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        // x' = a*x + b*y + tx ; y' = c*x + d*y + ty
        (float a, float b, float tx, float c, float d, float ty) = origin switch
        {
            SKEncodedOrigin.TopRight    => (-1f, 0f, w, 0f, 1f, 0f),
            SKEncodedOrigin.BottomRight => (-1f, 0f, w, 0f, -1f, h),
            SKEncodedOrigin.BottomLeft  => (1f, 0f, 0f, 0f, -1f, h),
            SKEncodedOrigin.LeftTop     => (0f, 1f, 0f, 1f, 0f, 0f),
            SKEncodedOrigin.RightTop    => (0f, -1f, h, 1f, 0f, 0f),
            SKEncodedOrigin.RightBottom => (0f, -1f, h, -1f, 0f, w),
            SKEncodedOrigin.LeftBottom  => (0f, 1f, 0f, -1f, 0f, w),
            _ => (1f, 0f, 0f, 0f, 1f, 0f),
        };

        var result = new SKBitmap(swap ? h : w, swap ? w : h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(result))
        using (var image = SKImage.FromBitmap(source))
        {
            canvas.SetMatrix(new SKMatrix(a, b, tx, c, d, ty, 0, 0, 1));
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        source.Dispose();
        return result;
    }
}
