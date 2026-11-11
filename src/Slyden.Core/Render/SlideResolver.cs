using Slyden.Core.Config;

namespace Slyden.Core.Render;

/// <summary>Movimento de um slide: zoom e centro (0 a 1) no início e no fim.</summary>
public readonly record struct Motion(double Zoom0, double Zoom1, double Cx0, double Cy0, double Cx1, double Cy1);

/// <summary>Foto com padrão e exceções já aplicados. O <see cref="Motion"/> depende do tamanho da foto: ver <see cref="SlideResolver.MotionOf"/>.</summary>
public sealed record ResolvedSlide(
    string File, EffectKind Effect, double Intensity, TransitionKind Transition,
    double[]? Zoom = null, double[][]? Center = null,
    int Length = 1, double Dim = 0, IReadOnlyList<TextBlock>? Texts = null,
    double Scale = 1, BackgroundKind Background = BackgroundKind.Blur);

public static class SlideResolver
{
    /// <summary>Aplica o padrão (efeitos alternados ou sorteados) e as exceções de cada foto.</summary>
    public static IReadOnlyList<ResolvedSlide> Resolve(SlideshowConfig cfg)
    {
        var d = cfg.Default;
        var rng = new Random(7); // fixo: o mesmo arquivo de configuração sempre gera o mesmo vídeo
        var result = new List<ResolvedSlide>(cfg.Photos.Count);
        for (int i = 0; i < cfg.Photos.Count; i++)
        {
            var p = cfg.Photos[i];
            var auto = d.Order == EffectOrder.Cycle ? d.Effects[i % d.Effects.Count] : d.Effects[rng.Next(d.Effects.Count)];
            result.Add(new ResolvedSlide(p.File, p.Effect ?? auto, p.Intensity ?? d.Intensity,
                                         p.Transition ?? d.Transition, p.Zoom, p.Center,
                                         p.Length ?? 1, p.Dim ?? 0, p.Texts ?? [],
                                         p.Scale ?? d.Scale, p.Background ?? d.Background));
        }
        return result;
    }

    /// <summary>
    /// Movimento do slide para uma foto de <paramref name="imageWidth"/> x <paramref name="imageHeight"/> mostrada em uma janela
    /// de proporção <paramref name="aspect"/>. Nos movimentos laterais e verticais o centro vai de um extremo possível ao outro:
    /// pedir mais do que isso faria a imagem parar na borda e arrancar de repente, o que parece um tranco.
    /// </summary>
    public static Motion MotionOf(ResolvedSlide slide, int imageWidth, int imageHeight, double aspect)
    {
        var m = Preset(slide.Effect, slide.Intensity, imageWidth, imageHeight, aspect);
        if (slide.Zoom is { } z) m = m with { Zoom0 = z[0], Zoom1 = z[1] };
        if (slide.Center is { } c) m = m with { Cx0 = c[0][0], Cy0 = c[0][1], Cx1 = c[1][0], Cy1 = c[1][1] };
        return m;
    }

    internal static Motion Preset(EffectKind effect, double intensity, int imageWidth, int imageHeight, double aspect)
    {
        double z = 1 + intensity;

        // menor e maior posição do centro em que a janela ainda cabe dentro da foto
        var view = SlideGeometry.View(imageWidth, imageHeight, aspect, z, .5, .5);
        double left = view.Width / 2 / imageWidth, right = 1 - left;
        double top = view.Height / 2 / imageHeight, bottom = 1 - top;

        return effect switch
        {
            EffectKind.None => new Motion(1, 1, .5, .5, .5, .5),
            EffectKind.ZoomIn => new Motion(1, z, .5, .5, .5, .5),
            EffectKind.ZoomOut => new Motion(z, 1, .5, .5, .5, .5),
            EffectKind.PanLr => new Motion(z, z, left, .5, right, .5),
            EffectKind.PanRl => new Motion(z, z, right, .5, left, .5),
            EffectKind.TiltUp => new Motion(z, z, .5, bottom, .5, top),
            EffectKind.TiltDown => new Motion(z, z, .5, top, .5, bottom),
            _ => throw new ArgumentOutOfRangeException(nameof(effect)),
        };
    }
}
