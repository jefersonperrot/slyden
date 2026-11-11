using SlideBeat.Core.Config;

namespace SlideBeat.Core.Render;

/// <summary>Movimento de um slide: zoom e centro (0 a 1) no início e no fim.</summary>
public readonly record struct Motion(double Zoom0, double Zoom1, double Cx0, double Cy0, double Cx1, double Cy1);

/// <summary>Foto com padrão e exceções já aplicados.</summary>
public sealed record ResolvedSlide(string File, EffectKind Effect, Motion Motion, TransitionKind Transition);

public static class SlideResolver
{
    private const double Pan = 0.15;

    public static Motion MotionOf(EffectKind effect, double intensity)
    {
        double z = 1 + intensity;
        return effect switch
        {
            EffectKind.None => new Motion(1, 1, .5, .5, .5, .5),
            EffectKind.ZoomIn => new Motion(1, z, .5, .5, .5, .5),
            EffectKind.ZoomOut => new Motion(z, 1, .5, .5, .5, .5),
            EffectKind.PanLr => new Motion(z, z, .5 - Pan, .5, .5 + Pan, .5),
            EffectKind.PanRl => new Motion(z, z, .5 + Pan, .5, .5 - Pan, .5),
            EffectKind.TiltUp => new Motion(z, z, .5, .5 + Pan, .5, .5 - Pan),
            EffectKind.TiltDown => new Motion(z, z, .5, .5 - Pan, .5, .5 + Pan),
            _ => throw new ArgumentOutOfRangeException(nameof(effect)),
        };
    }

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
            var effect = p.Effect ?? auto;
            var motion = MotionOf(effect, p.Intensity ?? d.Intensity);
            if (p.Zoom is { } z) motion = motion with { Zoom0 = z[0], Zoom1 = z[1] };
            if (p.Center is { } c) motion = motion with { Cx0 = c[0][0], Cy0 = c[0][1], Cx1 = c[1][0], Cy1 = c[1][1] };
            result.Add(new ResolvedSlide(p.File, effect, motion, p.Transition ?? d.Transition));
        }
        return result;
    }
}
