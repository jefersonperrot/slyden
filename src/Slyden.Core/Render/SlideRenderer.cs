using SkiaSharp;

namespace Slyden.Core.Render;

/// <summary>Desenha um slide (foto + movimento) em um quadro de tamanho fixo.</summary>
internal sealed class SlideRenderer : IDisposable
{
    private static readonly SKSamplingOptions Cubic = new(SKCubicResampler.Mitchell);
    private static readonly SKSamplingOptions Linear = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly SKBitmap _source;
    private readonly SKImage _image;
    private readonly SKBitmap? _background;
    private readonly SKImage? _backgroundImage;
    private readonly SlideLayout _layout;
    private readonly Motion _motion;
    private readonly int _frameWidth, _frameHeight;
    private readonly double _dim;
    private readonly double _scale;
    private readonly double _shadow;
    private readonly SKColor _fillColor;
    private readonly bool _needsBackground;
    private readonly IReadOnlyList<ActiveText> _texts;
    private readonly TextPainter? _painter;

    /// <summary>Cria o desenhista do slide calculando o movimento para o tamanho real da foto.</summary>
    public static SlideRenderer Create(SKBitmap source, ResolvedSlide slide, int frameWidth, int frameHeight,
                                       IReadOnlyList<ActiveText>? texts = null, TextPainter? painter = null)
    {
        var layout = SlideGeometry.Layout(source.Width, source.Height, frameWidth, frameHeight);
        var motion = SlideResolver.MotionOf(slide, source.Width, source.Height, (double)layout.Width / layout.Height);
        return new SlideRenderer(source, motion, frameWidth, frameHeight, slide.Dim, texts, painter,
                                 slide.Scale, slide.Background == Config.BackgroundKind.Blur,
                                 slide.BackgroundBlur, slide.BackgroundDim, slide.Shadow,
                                 slide.Background == Config.BackgroundKind.Color ? ParseColor(slide.BackgroundColor) : null);
    }

    private static SKColor ParseColor(string text) =>
        Config.HexColor.TryParse(text, out var c) ? new SKColor(c.R, c.G, c.B) : SKColors.Black;

    /// <param name="fillColor">Cor lisa do fundo (quando não é a foto desfocada); sem valor, preto.</param>
    /// <param name="scale">Tamanho da foto no quadro (0,3 a 1); abaixo de 1 sobra borda preenchida pelo fundo.</param>
    /// <param name="blurBackground">Fundo = a própria foto desfocada; falso = preto.</param>
    public SlideRenderer(SKBitmap source, Motion motion, int frameWidth, int frameHeight,
                         double dim = 0, IReadOnlyList<ActiveText>? texts = null, TextPainter? painter = null,
                         double scale = 1, bool blurBackground = true, double backgroundBlur = 1, double backgroundDim = 0.1, double shadow = 0,
                         SKColor? fillColor = null)
    {
        _fillColor = fillColor ?? SKColors.Black;
        _shadow = Math.Clamp(shadow, 0, 1);
        _source = source;
        _motion = motion;
        _frameWidth = frameWidth;
        _frameHeight = frameHeight;
        _dim = dim;
        _texts = texts ?? [];
        _painter = painter;
        _scale = Math.Clamp(scale, 0.3, 1);
        _image = SKImage.FromBitmap(source);
        _layout = SlideGeometry.Layout(source.Width, source.Height, frameWidth, frameHeight);
        _needsBackground = !_layout.Fill || _scale < 1;
        if (_needsBackground && blurBackground)
        {
            _background = BuildBackground(source, frameWidth, frameHeight, backgroundBlur, backgroundDim);
            _backgroundImage = SKImage.FromBitmap(_background);
        }
    }

    /// <param name="t">Progresso do movimento, de 0 a 1.</param>
    /// <param name="timing">Onde estamos no slide, para o fade dos textos; sem ele os textos aparecem inteiros.</param>
    public void Draw(SKCanvas canvas, double t, TextTiming? timing = null)
    {
        double e = SlideGeometry.Ease(t);
        var view = SlideGeometry.View(
            _source.Width, _source.Height, (double)_layout.Width / _layout.Height,
            SlideGeometry.Lerp(_motion.Zoom0, _motion.Zoom1, e),
            SlideGeometry.Lerp(_motion.Cx0, _motion.Cx1, e),
            SlideGeometry.Lerp(_motion.Cy0, _motion.Cy1, e));

        if (_backgroundImage is not null) canvas.DrawImage(_backgroundImage, 0, 0, Linear);
        else if (_needsBackground)
        {
            // cor opaca: sem isso, no fade o slide anterior apareceria através das bordas
            using var black = new SKPaint { Color = _fillColor };
            canvas.DrawRect(SKRect.Create(_frameWidth, _frameHeight), black);
        }

        var src = new SKRect((float)view.X0, (float)view.Y0, (float)view.X1, (float)view.Y1);
        var dst = SKRect.Create(_layout.OffsetX, _layout.OffsetY, _layout.Width, _layout.Height);
        if (_scale < 1)
        {
            float w = (float)(_layout.Width * _scale), h = (float)(_layout.Height * _scale);
            dst = SKRect.Create((_frameWidth - w) / 2, (_frameHeight - h) / 2, w, h);
            if (_shadow > 0)
            {
                float side = Math.Min(_frameWidth, _frameHeight);
                float sigma = side * 0.018f;
                using var paint = new SKPaint
                {
                    Color = SKColors.Black,
                    ImageFilter = SKImageFilter.CreateDropShadowOnly(0, side * 0.01f, sigma, sigma,
                        new SKColor(0, 0, 0, (byte)Math.Round(_shadow * 0.75 * 255))),
                };
                canvas.DrawRect(dst, paint);
            }
        }
        canvas.DrawImage(_image, src, dst, Cubic);

        if (_dim > 0)
        {
            using var dark = new SKPaint { Color = new SKColor(0, 0, 0, (byte)Math.Round(_dim * 255)) };
            canvas.DrawRect(SKRect.Create(_frameWidth, _frameHeight), dark);
        }

        if (_painter is null) return;
        foreach (var text in _texts)
        {
            double alpha = timing is { } tm ? OverlayPlan.AlphaOf(text, tm.Frame, tm.Frames, tm.FadeFrames(text.Block)) : 1;
            lock (_painter) // o desenho de texto compartilha fontes: um quadro por vez
                _painter.Draw(canvas, text.Block, _frameWidth, _frameHeight, alpha);
        }
    }

    /// <summary>Fundo para fotos que não preenchem o quadro: a própria foto, ampliada, desfocada e escurecida.</summary>
    /// <param name="blur">Multiplica o desfoque padrão (1 = padrão).</param>
    /// <param name="dim">Quanto escurece o fundo (0 = nada, 0,5 = metade).</param>
    private static SKBitmap BuildBackground(SKBitmap source, int width, int height, double blur, double dim)
    {
        int sw = Math.Max(1, width / 10), sh = Math.Max(1, height / 10);

        // recorte central com a proporção do quadro, reduzido
        double frameAspect = (double)width / height;
        double cw = source.Width, ch = source.Height;
        if (cw / ch > frameAspect) cw = ch * frameAspect; else ch = cw / frameAspect;
        var crop = SKRect.Create((float)((source.Width - cw) / 2), (float)((source.Height - ch) / 2), (float)cw, (float)ch);

        using var small = new SKBitmap(sw, sh, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(small))
        using (var img = SKImage.FromBitmap(source))
            c.DrawImage(img, crop, SKRect.Create(sw, sh), Linear);

        using var blurred = new SKBitmap(sw, sh, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(blurred))
        using (var smallImage = SKImage.FromBitmap(small))
        using (var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur((float)(6 * blur), (float)(6 * blur), SKShaderTileMode.Clamp) })
            c.DrawImage(smallImage, 0, 0, Linear, paint);

        var background = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(background))
        using (var img = SKImage.FromBitmap(blurred))
        using (var dark = new SKPaint { Color = new SKColor(0, 0, 0, (byte)Math.Round(Math.Clamp(dim, 0, 1) * 255)) })
        {
            c.DrawImage(img, SKRect.Create(width, height), Linear);
            if (dim > 0) c.DrawRect(SKRect.Create(width, height), dark);
        }
        return background;
    }

    public void Dispose()
    {
        _image.Dispose();
        _backgroundImage?.Dispose();
        _source.Dispose();
        _background?.Dispose();
    }
}
