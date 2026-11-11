using SkiaSharp;
using Slyden.Core.Config;

namespace Slyden.Core.Render;

/// <summary>Imagens para a interface: miniaturas e quadro de prévia de um slide (sem codificar vídeo).</summary>
public static class SlidePreview
{
    /// <summary>Miniatura com a largura pedida (mantém a proporção, respeita a orientação EXIF).</summary>
    public static SKBitmap Thumbnail(string photoPath, int width)
    {
        using var source = ImageLoader.Load(photoPath);
        int height = Math.Max(1, (int)Math.Round((double)source.Height * width / source.Width));
        var result = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var image = SKImage.FromBitmap(source);
        using var canvas = new SKCanvas(result);
        canvas.DrawImage(image, SKRect.Create(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return result;
    }

    /// <summary>
    /// Desenha um quadro do slide <paramref name="index"/> da configuração, com efeito (no ponto <paramref name="t"/>, 0 a 1),
    /// escurecimento e textos. <paramref name="scale"/> reduz o quadro (0,5 = metade do tamanho do vídeo).
    /// </summary>
    /// <param name="loadPhoto">Opcional: entrega a foto (ex.: de um cache reduzido). O bitmap devolvido passa a pertencer ao quadro.</param>
    public static SKBitmap Frame(SlideshowConfig config, string photosFolder, int index, double t, double scale = 0.5,
                                 IReadOnlyList<string>? fontFolders = null, Func<string, SKBitmap>? loadPhoto = null)
    {
        config.Validate();
        var (fullW, fullH) = VideoFormat.SizeOf(config.Format);
        int width = Math.Max(2, (int)Math.Round(fullW * scale)), height = Math.Max(2, (int)Math.Round(fullH * scale));

        var slides = SlideResolver.Resolve(config);
        if (index < 0 || index >= slides.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var overlays = OverlayPlan.Build(slides);

        var folders = new List<string> { Path.Combine(AppContext.BaseDirectory, "fonts") };
        folders.AddRange(fontFolders ?? []);
        using var fonts = new FontResolver(folders);
        var painter = new TextPainter(fonts);

        var frame = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(frame);
        canvas.Clear(SKColors.Black);
        string path = Path.Combine(photosFolder, slides[index].File);
        using var renderer = SlideRenderer.Create(
            loadPhoto is null ? ImageLoader.Load(path) : loadPhoto(path), slides[index], width, height, overlays[index], painter);
        renderer.Draw(canvas, Math.Clamp(t, 0, 1));
        return frame;
    }
}
