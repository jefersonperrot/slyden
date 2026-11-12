namespace Slyden.Core.Audio;

/// <summary>Resumo do volume do áudio para desenhar a onda: o maior valor de cada fatia de tempo, de 0 a 1.</summary>
public static class Waveform
{
    /// <summary>Fatias por segundo (20 = uma a cada 50 ms; uma música de 4 min gera ~4800 valores).</summary>
    public const int PerSecond = 20;

    public static float[] Peaks(float[] samples, int sampleRate)
    {
        int bucket = Math.Max(1, sampleRate / PerSecond);
        var peaks = new float[(samples.Length + bucket - 1) / bucket];
        float loudest = 0;
        for (int i = 0; i < peaks.Length; i++)
        {
            float max = 0;
            int end = Math.Min(samples.Length, (i + 1) * bucket);
            for (int k = i * bucket; k < end; k++) max = Math.Max(max, Math.Abs(samples[k]));
            peaks[i] = max;
            loudest = Math.Max(loudest, max);
        }
        if (loudest > 0)
            for (int i = 0; i < peaks.Length; i++) peaks[i] /= loudest;
        return peaks;
    }
}
