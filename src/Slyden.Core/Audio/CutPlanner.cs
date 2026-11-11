namespace Slyden.Core.Audio;

/// <param name="BeatsPerPhoto">Troca de foto a cada quantas batidas.</param>
/// <param name="Phase">Em qual batida do ciclo (0..BeatsPerPhoto-1) caem as trocas.</param>
/// <param name="CutTimes">Instantes (segundos) em que a foto troca; o primeiro é o início do vídeo.</param>
public sealed record CutPlan(int BeatsPerPhoto, int Phase, double[] CutTimes);

public static class CutPlanner
{
    private static readonly int[] AutoChoices = [8, 4, 2];

    /// <summary>
    /// Espalha <paramref name="units"/> slides por TODA a música: cada troca cai numa batida e os slides ficam com
    /// o mesmo número de batidas (±1). O primeiro começa no início da música e o último vai até o fim dela.
    /// Devolve nulo se houver menos batidas do que slides (aí não dá para trocar a cada batida).
    /// </summary>
    public static CutPlan? PlanToFit(BeatAnalysis analysis, int units)
    {
        int count = analysis.BeatTimes.Length;
        if (units < 1 || count < units + 1) return null;
        var cuts = new double[units + 1];
        for (int i = 1; i < units; i++)
            cuts[i] = analysis.BeatTimes[(int)Math.Round((double)i * count / units)];
        cuts[units] = Math.Max(analysis.DurationSeconds, cuts[Math.Max(0, units - 1)] + 0.5);
        return new CutPlan(Math.Max(1, (int)Math.Round((double)count / units)), 0, cuts);
    }

    /// <summary>
    /// Decide em quais batidas trocar a foto. Com <paramref name="beatsPerPhoto"/> nulo ("auto") usa o
    /// ritmo mais calmo (8, 4 ou 2 batidas) que ainda comporta todas as fotos. A fase escolhida é a das
    /// batidas mais fortes, que costuma ser o início do compasso.
    /// </summary>
    public static CutPlan Plan(BeatAnalysis analysis, int photoCount, int? beatsPerPhoto = null)
    {
        int count = analysis.BeatTimes.Length;
        int per = beatsPerPhoto
            ?? AutoChoices.FirstOrDefault(p => (count - 1) / p >= photoCount, 2);
        if (per < 1) throw new ArgumentOutOfRangeException(nameof(beatsPerPhoto));

        int bestPhase = 0;
        double bestSum = double.NegativeInfinity;
        for (int phase = 0; phase < Math.Min(per, count); phase++)
        {
            double sum = 0;
            for (int i = phase; i < count; i += per) sum += analysis.BeatStrengths[i];
            if (sum > bestSum) { bestSum = sum; bestPhase = phase; }
        }

        var cuts = new List<double>();
        for (int i = bestPhase; i < count; i += per) cuts.Add(analysis.BeatTimes[i]);
        return new CutPlan(per, bestPhase, cuts.ToArray());
    }
}
