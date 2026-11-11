namespace Slyden.Tests;

internal static class TestAudio
{
    public const int Sr = 22050;

    /// <summary>Trilha com um "clique" a cada <paramref name="interval"/> segundos (o primeiro em <paramref name="first"/>).</summary>
    public static float[] Clicks(double seconds, double interval, double first = 0.25, double accentEvery = 0, int accentPhase = 0)
    {
        var x = new float[(int)(seconds * Sr)];
        var rng = new Random(42);
        int n = 0;
        for (double t = first; t < seconds - 0.1; t += interval, n++)
        {
            double amp = accentEvery > 0 && n % (int)accentEvery == accentPhase ? 1.0 : (accentEvery > 0 ? 0.4 : 0.8);
            int i0 = (int)(t * Sr);
            for (int i = 0; i < (int)(0.03 * Sr) && i0 + i < x.Length; i++)
            {
                double env = Math.Exp(-i / (0.006 * Sr));
                double tone = Math.Sin(2 * Math.PI * 1500 * i / Sr);
                x[i0 + i] += (float)(amp * env * (0.7 * tone + 0.3 * (rng.NextDouble() * 2 - 1)));
            }
        }
        return x;
    }
}
