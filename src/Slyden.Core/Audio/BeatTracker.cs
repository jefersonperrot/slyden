namespace Slyden.Core.Audio;

/// <summary>
/// Marca as batidas por programação dinâmica (Ellis, 2007): cada batida soma a força de onset
/// do quadro e é penalizada se o intervalo para a anterior se afastar do período estimado.
/// </summary>
internal static class BeatTracker
{
    public static int[] Track(double[] env, int sampleRate, int hop, double bpm, double tightness = 100)
    {
        int n = env.Length;
        if (n < 8 || bpm <= 0) return [];

        double std = StdDev(env);
        if (std <= 0) return [];
        int period = Math.Max(2, (int)Math.Round(60.0 * sampleRate / hop / bpm));

        var local = LocalScore(env.Select(v => v / std).ToArray(), period);

        // possíveis predecessores: entre 2 períodos e meio período antes
        int lo = -2 * period;
        int hi = -(int)Math.Round(period / 2.0);
        int len = hi - lo + 1;
        var penalty = new double[len];
        for (int j = 0; j < len; j++)
        {
            double r = Math.Log(-(double)(lo + j) / period);
            penalty[j] = -tightness * r * r;
        }

        var cum = new double[n];
        var back = new int[n];
        double localMax = local.Max();
        bool first = true;
        for (int i = 0; i < n; i++)
        {
            double bestScore = double.NegativeInfinity;
            int bestJ = 0;
            for (int j = 0; j < len; j++)
            {
                int idx = i + lo + j;
                double c = penalty[j] + (idx >= 0 ? cum[idx] : 0);
                if (c > bestScore) { bestScore = c; bestJ = j; }
            }
            cum[i] = local[i] + bestScore;
            if (first && local[i] < 0.01 * localMax)
            {
                back[i] = -1;
            }
            else
            {
                back[i] = i + lo + bestJ;
                first = false;
            }
        }

        int tail = LastBeat(cum);
        var beats = new List<int> { tail };
        while (back[beats[^1]] >= 0) beats.Add(back[beats[^1]]);
        beats.Reverse();

        return Trim(local, beats.ToArray());
    }

    private static double[] LocalScore(double[] env, int period)
    {
        int w = period;
        var kernel = new double[2 * w + 1];
        for (int d = -w; d <= w; d++)
        {
            double x = d * 32.0 / period;
            kernel[d + w] = Math.Exp(-0.5 * x * x);
        }
        var result = new double[env.Length];
        for (int i = 0; i < env.Length; i++)
        {
            double s = 0;
            for (int d = -w; d <= w; d++)
            {
                int k = i + d;
                if (k >= 0 && k < env.Length) s += env[k] * kernel[d + w];
            }
            result[i] = s;
        }
        return result;
    }

    private static int LastBeat(double[] cum)
    {
        int n = cum.Length;
        var isMax = new bool[n];
        for (int i = 0; i < n; i++)
        {
            double prev = i > 0 ? cum[i - 1] : cum[i];
            double next = i < n - 1 ? cum[i + 1] : cum[i];
            isMax[i] = cum[i] > prev && cum[i] >= next;
        }
        var maxVals = Enumerable.Range(0, n).Where(i => isMax[i]).Select(i => cum[i]).OrderBy(v => v).ToArray();
        if (maxVals.Length == 0) return n - 1;
        double med = maxVals.Length % 2 == 1
            ? maxVals[maxVals.Length / 2]
            : (maxVals[maxVals.Length / 2 - 1] + maxVals[maxVals.Length / 2]) / 2;

        int tail = -1;
        for (int i = 0; i < n; i++)
            if (isMax[i] && cum[i] * 2 > med) tail = i;
        return tail >= 0 ? tail : n - 1;
    }

    /// <summary>Remove batidas fracas no começo e no fim (abaixo de metade do RMS da curva suavizada).</summary>
    private static int[] Trim(double[] local, int[] beats)
    {
        if (beats.Length < 5) return beats;
        double[] h = [0, 0.5, 1, 0.5, 0];
        var smooth = new double[beats.Length];
        for (int i = 0; i < beats.Length; i++)
        {
            double s = 0;
            for (int d = -2; d <= 2; d++)
            {
                int k = i + d;
                if (k >= 0 && k < beats.Length) s += h[d + 2] * local[beats[k]];
            }
            smooth[i] = s;
        }
        double rms = Math.Sqrt(smooth.Select(v => v * v).Average());
        double threshold = 0.5 * rms;
        int a = Array.FindIndex(smooth, v => v > threshold);
        int b = Array.FindLastIndex(smooth, v => v > threshold);
        return a < 0 ? beats : beats[a..(b + 1)];
    }

    private static double StdDev(double[] x)
    {
        double mean = x.Average();
        double ss = x.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(ss / (x.Length - 1));
    }
}
