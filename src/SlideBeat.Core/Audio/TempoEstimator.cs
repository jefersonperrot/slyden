namespace SlideBeat.Core.Audio;

/// <summary>
/// Estima o andamento (BPM) pela autocorrelação da curva de onsets, com preferência por
/// andamentos perto de 120 BPM (como o librosa.feature.tempo).
/// </summary>
internal static class TempoEstimator
{
    private const int WinLength = 384;
    private const double MaxBpm = 320;

    public static double Estimate(double[] env, int sampleRate, int hop, double startBpm = 120)
    {
        int win = Math.Min(WinLength, env.Length);
        if (win < 16) return startBpm;

        var hann = new double[win];
        for (int i = 0; i < win; i++) hann[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / win);

        var mean = new double[win];
        int count = 0;
        int step = Math.Max(1, win / 2);
        for (int s = 0; s + win <= env.Length; s += step)
        {
            var seg = new double[win];
            for (int i = 0; i < win; i++) seg[i] = env[s + i] * hann[i];

            var acf = new double[win];
            for (int lag = 0; lag < win; lag++)
            {
                double sum = 0;
                for (int i = 0; i + lag < win; i++) sum += seg[i] * seg[i + lag];
                acf[lag] = sum;
            }
            if (acf[0] <= 0) continue;
            for (int lag = 0; lag < win; lag++) mean[lag] += acf[lag] / acf[0];
            count++;
        }
        if (count == 0) return startBpm;

        double best = double.NegativeInfinity, bestBpm = startBpm;
        for (int lag = 1; lag < win; lag++)
        {
            double bpm = 60.0 * sampleRate / (hop * lag);
            if (bpm > MaxBpm) continue;
            double a = Math.Max(0, mean[lag] / count);
            double octaves = Math.Log2(bpm) - Math.Log2(startBpm);
            double score = Math.Log(1 + 1e6 * a) - 0.5 * octaves * octaves;
            if (score > best) { best = score; bestBpm = bpm; }
        }
        return bestBpm;
    }
}
