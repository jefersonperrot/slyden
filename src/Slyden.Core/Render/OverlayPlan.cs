using Slyden.Core.Config;

namespace Slyden.Core.Render;

/// <summary>Um texto presente em um slide. <paramref name="First"/>/<paramref name="Last"/> dizem se o slide abre/fecha a faixa do texto.</summary>
/// <param name="StartSlide">Primeiro slide da faixa do texto (índice no vídeo).</param>
/// <param name="EndSlide">Último slide da faixa do texto.</param>
public sealed record ActiveText(TextBlock Block, bool First, bool Last, int StartSlide = 0, int EndSlide = 0);

/// <param name="Frame">Quadro atual dentro do slide.</param>
/// <param name="Frames">Total de quadros do slide.</param>
/// <param name="BeatSeconds">Duração de uma batida, para converter o fade em quadros.</param>
/// <param name="Edges">Quadro em que cada slide começa (mais o final); com ele o texto sabe quanto tempo passou desde o início da sua faixa.</param>
/// <param name="Slide">Índice do slide que está sendo desenhado.</param>
public readonly record struct TextTiming(int Frame, int Frames, double BeatSeconds, int Fps,
                                         int[]? Edges = null, int Slide = 0)
{
    public int FadeFrames(TextBlock block) => Math.Max(1, (int)Math.Round(block.FadeBeats * BeatSeconds * Fps));
}

public static class OverlayPlan
{
    /// <summary>Para cada slide, os textos que aparecem nele (um texto com span 3 entra nos 3 slides seguidos).</summary>
    public static List<List<ActiveText>> Build(IReadOnlyList<ResolvedSlide> slides)
    {
        var plan = Enumerable.Range(0, slides.Count).Select(_ => new List<ActiveText>()).ToList();
        for (int i = 0; i < slides.Count; i++)
        {
            foreach (var block in slides[i].Texts ?? [])
            {
                int last = Math.Min(slides.Count - 1, i + block.Span - 1);
                for (int j = i; j <= last; j++)
                    plan[j].Add(new ActiveText(block, First: j == i, Last: j == last, StartSlide: i, EndSlide: last));
            }
        }
        return plan;
    }

    /// <summary>
    /// Opacidade do texto com os controles de animação: aparece depois de <see cref="TextBlock.Delay"/> segundos do
    /// início da faixa, entra com fade, fica visível por <see cref="TextBlock.Duration"/> segundos (ou até o fim da faixa)
    /// e sai com fade. Sem "FadeIn"/"FadeOut" no texto, o fade é de <see cref="TextBlock.FadeBeats"/> batidas.
    /// </summary>
    public static double AlphaOf(ActiveText text, TextTiming timing)
    {
        var b = text.Block;
        if (b.Animation == TextAnimation.None) return 1;

        double fps = timing.Fps;
        double elapsed, last; // segundos desde o início da faixa; instante do último quadro da faixa
        if (timing.Edges is { } e)
        {
            elapsed = (e[timing.Slide] - e[text.StartSlide] + timing.Frame) / fps;
            last = (e[text.EndSlide + 1] - e[text.StartSlide] - 1) / fps;
        }
        else
        {
            elapsed = timing.Frame / fps;
            last = (timing.Frames - 1) / fps;
        }
        bool canFadeIn = timing.Edges is not null || text.First;
        bool canFadeOut = timing.Edges is not null || text.Last;

        double fadeIn = canFadeIn ? b.FadeIn ?? b.FadeBeats * timing.BeatSeconds : 0;
        double fadeOut = canFadeOut ? b.FadeOut ?? b.FadeBeats * timing.BeatSeconds : 0;

        double t = elapsed - b.Delay;
        if (t < 0) return 0;
        double end = last - b.Delay;
        if (b.Duration is > 0) end = Math.Min(end, b.Duration.Value);
        if (t > end) return 0;

        double alpha = 1;
        if (fadeIn > 0) alpha = Math.Min(alpha, t / fadeIn);
        if (fadeOut > 0) alpha = Math.Min(alpha, (end - t) / fadeOut);
        return Math.Clamp(alpha, 0, 1);
    }

    /// <summary>Opacidade do texto: entra com fade no primeiro slide da faixa e sai com fade no último.</summary>
    public static double AlphaOf(ActiveText text, int frame, int frames, int fadeFrames)
    {
        if (text.Block.Animation == TextAnimation.None) return 1;
        double alpha = 1;
        if (text.First) alpha = Math.Min(alpha, (double)frame / fadeFrames);
        if (text.Last) alpha = Math.Min(alpha, (double)(frames - 1 - frame) / fadeFrames);
        return Math.Clamp(alpha, 0, 1);
    }
}
