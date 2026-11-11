namespace SlideBeat.Core.Audio;

/// <summary>Um filtro triangular da escala mel; guarda só a faixa de bins com peso diferente de zero.</summary>
internal readonly record struct MelFilter(int Start, float[] Weights);

/// <summary>Banco de filtros mel (escala Slaney, normalização Slaney), como o librosa usa por padrão.</summary>
internal static class MelFilterBank
{
    private const double FSp = 200.0 / 3;
    private static readonly double LogStep = Math.Log(6.4) / 27;

    private static double HzToMel(double f) => f < 1000 ? f / FSp : 15 + Math.Log(f / 1000) / LogStep;
    private static double MelToHz(double m) => m < 15 ? m * FSp : 1000 * Math.Exp(LogStep * (m - 15));

    public static MelFilter[] Create(int sampleRate, int fftSize, int mels)
    {
        int bins = fftSize / 2 + 1;
        double fmax = sampleRate / 2.0;
        double melMax = HzToMel(fmax);

        var edges = new double[mels + 2];
        for (int i = 0; i < edges.Length; i++)
            edges[i] = MelToHz(melMax * i / (mels + 1));

        var filters = new MelFilter[mels];
        for (int m = 0; m < mels; m++)
        {
            double f0 = edges[m], f1 = edges[m + 1], f2 = edges[m + 2];
            double norm = 2.0 / (f2 - f0);
            var w = new float[bins];
            int first = -1, last = -1;
            for (int k = 0; k < bins; k++)
            {
                double f = k * (double)sampleRate / fftSize;
                double v = Math.Max(0, Math.Min((f - f0) / (f1 - f0), (f2 - f) / (f2 - f1)));
                if (v > 0)
                {
                    w[k] = (float)(v * norm);
                    if (first < 0) first = k;
                    last = k;
                }
            }
            filters[m] = first < 0
                ? new MelFilter(0, [])
                : new MelFilter(first, w[first..(last + 1)]);
        }
        return filters;
    }
}
