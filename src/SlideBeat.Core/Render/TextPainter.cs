using SkiaSharp;
using SlideBeat.Core.Config;

namespace SlideBeat.Core.Render;

/// <summary>Desenha um <see cref="TextBlock"/> sobre o quadro: quebra linhas, reduz se não couber, alinha e aplica sombra/contorno.</summary>
internal sealed class TextPainter(FontResolver fonts)
{
    private const int FitPasses = 6;

    public void Draw(SKCanvas canvas, TextBlock block, int frameWidth, int frameHeight, double alpha)
    {
        if (alpha <= 0 || string.IsNullOrWhiteSpace(block.Text)) return;

        var typeface = fonts.Resolve(block.Font, block.Bold, block.Italic);
        float maxWidth = (float)(block.MaxWidth * frameWidth);
        float size = (float)(block.Size * Math.Min(frameWidth, frameHeight));

        // quebra as linhas e, se alguma palavra for larga demais, reduz a fonte até caber
        string[] lines = [];
        for (int pass = 0; pass < FitPasses; pass++)
        {
            using var probe = NewFont(typeface, size);
            lines = Wrap(block.Text, probe, maxWidth);
            float widest = lines.Max(l => probe.MeasureText(l));
            if (widest <= maxWidth * 1.001f) break;
            size *= maxWidth / widest * 0.99f;
        }

        using var font = NewFont(typeface, size);
        var metrics = font.Metrics;
        float lineHeight = (metrics.Descent - metrics.Ascent + metrics.Leading) * (float)block.LineSpacing;
        float top = (float)(block.Y * frameHeight) - lines.Length * lineHeight / 2;

        HexColor.TryParse(block.Color, out var c);
        byte A(double factor) => (byte)Math.Clamp(Math.Round(c.A * alpha * factor), 0, 255);

        for (int i = 0; i < lines.Length; i++)
        {
            float width = font.MeasureText(lines[i]);
            float x = (float)(block.X * frameWidth) - block.Align switch
            {
                TextAlign.Left => 0,
                TextAlign.Center => width / 2,
                _ => width,
            };
            float baseline = top + i * lineHeight - metrics.Ascent;

            if (block.Shadow)
            {
                using var shadow = new SKPaint
                {
                    IsAntialias = true,
                    Color = new SKColor(0, 0, 0, (byte)Math.Clamp(Math.Round(140 * alpha), 0, 255)),
                    MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, size * 0.05f),
                };
                canvas.DrawText(lines[i], x + size * 0.03f, baseline + size * 0.05f, SKTextAlign.Left, font, shadow);
            }
            if (block.Outline > 0)
            {
                using var outline = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = size * (float)block.Outline * 2,
                    StrokeJoin = SKStrokeJoin.Round,
                    Color = new SKColor(0, 0, 0, A(1)),
                };
                canvas.DrawText(lines[i], x, baseline, SKTextAlign.Left, font, outline);
            }
            using var fill = new SKPaint { IsAntialias = true, Color = new SKColor(c.R, c.G, c.B, A(1)) };
            canvas.DrawText(lines[i], x, baseline, SKTextAlign.Left, font, fill);
        }
    }

    private static SKFont NewFont(SKTypeface typeface, float size) => new(typeface, size)
    {
        Edging = SKFontEdging.Antialias, // em vídeo, o "LCD" (cores nas bordas) fica feio
        Subpixel = true,
    };

    /// <summary>Quebra por palavras sem passar de <paramref name="maxWidth"/>; "\n" força quebra.</summary>
    internal static string[] Wrap(string text, SKFont font, float maxWidth)
    {
        var result = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) { result.Add(""); continue; }

            string line = words[0];
            foreach (var word in words.Skip(1))
            {
                string candidate = line + " " + word;
                if (font.MeasureText(candidate) <= maxWidth) line = candidate;
                else { result.Add(line); line = word; }
            }
            result.Add(line);
        }
        return result.ToArray();
    }
}
