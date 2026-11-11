namespace SlideBeat.Core.Audio;

/// <param name="Tempo">Andamento estimado, em BPM.</param>
/// <param name="BeatTimes">Instante de cada batida, em segundos.</param>
/// <param name="BeatStrengths">Força de onset em cada batida (para achar o início do compasso).</param>
/// <param name="DurationSeconds">Duração do áudio.</param>
public sealed record BeatAnalysis(double Tempo, double[] BeatTimes, double[] BeatStrengths, double DurationSeconds);

public static class BeatAnalyzer
{
    public const int SampleRate = 22050;

    public static BeatAnalysis Analyze(float[] samples, int sampleRate = SampleRate)
    {
        var env = OnsetDetector.Strength(samples, sampleRate);
        double tempo = TempoEstimator.Estimate(env, sampleRate, OnsetDetector.Hop);
        var frames = BeatTracker.Track(env, sampleRate, OnsetDetector.Hop, tempo);

        var times = frames.Select(f => f * (double)OnsetDetector.Hop / sampleRate).ToArray();
        var strengths = frames.Select(f => env[f]).ToArray();
        return new BeatAnalysis(tempo, times, strengths, samples.Length / (double)sampleRate);
    }
}
