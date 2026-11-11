using SlideBeat.Core.Config;

namespace SlideBeat.Core.Render;

/// <param name="Slide">Índice do slide atual.</param>
/// <param name="T">Progresso do movimento do slide, de 0 a 1.</param>
/// <param name="FadeAlpha">Quanto do próximo slide já aparece por cima (0 = nada, 1 = ele inteiro).</param>
/// <param name="Frame">Número do quadro dentro do slide (0 = o primeiro).</param>
/// <param name="Frames">Quantos quadros o slide tem.</param>
public readonly record struct FrameInfo(int Slide, double T, double FadeAlpha, int Frame, int Frames);

public static class Timeline
{
    /// <summary>
    /// Um item por quadro do vídeo. <paramref name="edges"/> são os quadros em que cada slide começa
    /// (mais o final). O fade para o próximo slide termina exatamente na troca, que cai na batida.
    /// </summary>
    public static IEnumerable<FrameInfo> Frames(IReadOnlyList<int> edges, IReadOnlyList<TransitionKind> transitionInto, int fadeFrames)
    {
        fadeFrames = Math.Max(1, fadeFrames);
        for (int i = 0; i + 1 < edges.Count; i++)
        {
            int frames = edges[i + 1] - edges[i];
            bool fadeOut = i + 1 < transitionInto.Count && transitionInto[i + 1] == TransitionKind.Fade;
            for (int k = 0; k < frames; k++)
            {
                double t = (double)k / Math.Max(frames - 1, 1);
                int left = frames - k;
                double alpha = fadeOut && left <= fadeFrames ? 1 - (double)(left - 1) / fadeFrames : 0;
                yield return new FrameInfo(i, t, alpha, k, frames);
            }
        }
    }
}
