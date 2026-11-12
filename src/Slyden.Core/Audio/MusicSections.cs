using Slyden.Core.Config;

namespace Slyden.Core.Audio;

/// <summary>
/// "Música por trecho": cada música cobre um grupo de fotos. Quando alguma música (depois da primeira) diz em que
/// foto entra (<see cref="MusicTrack.StartPhoto"/>), as fotos de cada grupo se espalham só pela música dele, e a
/// troca de música cai exatamente na troca de foto. As músicas sem foto escolhida entram na proporção do tempo.
/// </summary>
public static class MusicSections
{
    public static bool Active(IReadOnlyList<MusicTrack> tracks) =>
        tracks.Count > 1 && tracks.Skip(1).Any(t => t.StartPhoto is not null);

    /// <summary>
    /// Primeiro slide de cada música (índice base 0 entre os <paramref name="count"/> slides, já sem os
    /// <paramref name="skip"/> do começo, como a capa que toca antes da música). Nulo se não há trechos ativos
    /// ou não dá (menos fotos do que músicas, ou a análise não traz o início de cada música).
    /// </summary>
    public static int[]? FirstSlides(IReadOnlyList<MusicTrack> tracks, BeatAnalysis analysis, int count, int skip)
    {
        if (!Active(tracks) || analysis.TrackStarts is not { } starts || starts.Length != tracks.Count
            || count < tracks.Count || analysis.DurationSeconds <= 0)
            return null;

        var first = new int[tracks.Count];
        for (int k = 1; k < tracks.Count; k++)
        {
            int wanted = tracks[k].StartPhoto is { } photo
                ? photo - 1 - skip
                : (int)Math.Round(starts[k] / analysis.DurationSeconds * count);
            // sempre pelo menos um slide por música, na ordem
            first[k] = Math.Clamp(wanted, first[k - 1] + 1, count - (tracks.Count - k));
        }
        return first;
    }
}
