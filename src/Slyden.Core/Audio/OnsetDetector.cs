namespace Slyden.Core.Audio;

/// <summary>
/// Curva de "força de onset": quanto o espectro (mel, em dB) aumenta de um quadro para o seguinte.
/// Mesma ideia e mesmo alinhamento temporal do librosa.onset.onset_strength.
/// </summary>
public static class OnsetDetector
{
    public const int Hop = 512;
    public const int FftSize = 2048;
    public const int Mels = 128;
    private const double TopDb = 80;

    /// <summary>Um valor por quadro; o quadro t cobre o tempo t * Hop / sampleRate.</summary>
    public static double[] Strength(float[] samples, int sampleRate)
    {
        int frames = 1 + samples.Length / Hop;
        var filters = MelFilterBank.Create(sampleRate, FftSize, Mels);

        var window = new double[FftSize];
        for (int n = 0; n < FftSize; n++)
            window[n] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / FftSize); // Hann periódica

        var re = new double[FftSize];
        var im = new double[FftSize];
        var power = new double[FftSize / 2 + 1];
        var db = new float[frames][];
        double maxDb = double.NegativeInfinity;

        for (int t = 0; t < frames; t++)
        {
            int start = t * Hop - FftSize / 2; // quadro centralizado, zeros fora do áudio
            for (int n = 0; n < FftSize; n++)
            {
                int idx = start + n;
                re[n] = idx >= 0 && idx < samples.Length ? samples[idx] * window[n] : 0;
                im[n] = 0;
            }
            Fft.Transform(re, im);
            for (int k = 0; k < power.Length; k++)
                power[k] = re[k] * re[k] + im[k] * im[k];

            var row = new float[Mels];
            for (int m = 0; m < Mels; m++)
            {
                var f = filters[m];
                double sum = 0;
                for (int j = 0; j < f.Weights.Length; j++)
                    sum += f.Weights[j] * power[f.Start + j];
                double v = 10 * Math.Log10(Math.Max(1e-10, sum));
                row[m] = (float)v;
                if (v > maxDb) maxDb = v;
            }
            db[t] = row;
        }

        double floor = maxDb - TopDb;
        foreach (var row in db)
            for (int m = 0; m < Mels; m++)
                if (row[m] < floor) row[m] = (float)floor;

        // fluxo espectral F[f] = média dos aumentos positivos entre o quadro f-1 e f
        var env = new double[frames];
        for (int f = 1; f < frames; f++)
        {
            double s = 0;
            for (int m = 0; m < Mels; m++)
            {
                double d = db[f][m] - db[f - 1][m];
                if (d > 0) s += d;
            }
            // o librosa atrasa a curva em 1 + FftSize/(2*Hop) quadros para compensar a janela
            int at = f + 2;
            if (at < frames) env[at] = s / Mels;
        }
        return env;
    }
}
