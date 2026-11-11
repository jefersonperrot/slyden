using Slyden.Core.Config;

namespace Slyden.Core.Render;

/// <summary>Um texto presente em um slide. <paramref name="First"/>/<paramref name="Last"/> dizem se o slide abre/fecha a faixa do texto.</summary>
public sealed record ActiveText(TextBlock Block, bool First, bool Last);

/// <param name="Frame">Quadro atual dentro do slide.</param>
/// <param name="Frames">Total de quadros do slide.</param>
/// <param name="BeatSeconds">Duração de uma batida, para converter o fade em quadros.</param>
public readonly record struct TextTiming(int Frame, int Frames, double BeatSeconds, int Fps)
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
                    plan[j].Add(new ActiveText(block, First: j == i, Last: j == last));
            }
        }
        return plan;
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
