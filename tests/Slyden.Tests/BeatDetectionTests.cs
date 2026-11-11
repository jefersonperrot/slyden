using Slyden.Core.Audio;

namespace Slyden.Tests;

public class BeatDetectionTests
{
    [Fact]
    public void Onset_peak_is_near_the_click()
    {
        var x = TestAudio.Clicks(2, interval: 10, first: 1.0); // um único clique em 1,0 s
        var env = OnsetDetector.Strength(x, TestAudio.Sr);

        int peak = Array.IndexOf(env, env.Max());
        double expected = 1.0 * TestAudio.Sr / OnsetDetector.Hop;
        Assert.InRange(peak, expected - 3, expected + 3);
    }

    [Fact]
    public void Finds_tempo_and_beats_of_a_120bpm_click_track()
    {
        var x = TestAudio.Clicks(30, interval: 0.5);
        var a = BeatAnalyzer.Analyze(x);

        Assert.InRange(a.Tempo, 112, 128);
        Assert.InRange(a.BeatTimes.Length, 50, 62);

        // cada batida detectada cai perto de um clique (0,25 + k * 0,5)
        foreach (double t in a.BeatTimes)
        {
            double k = Math.Round((t - 0.25) / 0.5);
            Assert.InRange(Math.Abs(t - (0.25 + k * 0.5)), 0, 0.08);
        }
    }

    [Fact]
    public void Silence_gives_no_beats_and_does_not_crash()
    {
        var a = BeatAnalyzer.Analyze(new float[TestAudio.Sr * 5]);
        Assert.Empty(a.BeatTimes);
    }
}
