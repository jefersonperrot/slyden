using SlideBeat.Core.Audio;

namespace SlideBeat.Tests;

public class FftTests
{
    [Fact]
    public void Matches_naive_dft()
    {
        const int n = 32;
        var rng = new Random(1);
        var x = Enumerable.Range(0, n).Select(_ => rng.NextDouble() * 2 - 1).ToArray();

        var re = (double[])x.Clone();
        var im = new double[n];
        Fft.Transform(re, im);

        for (int k = 0; k < n; k++)
        {
            double sr = 0, si = 0;
            for (int t = 0; t < n; t++)
            {
                double ang = -2 * Math.PI * k * t / n;
                sr += x[t] * Math.Cos(ang);
                si += x[t] * Math.Sin(ang);
            }
            Assert.Equal(sr, re[k], 9);
            Assert.Equal(si, im[k], 9);
        }
    }

    [Fact]
    public void Rejects_size_that_is_not_power_of_two()
    {
        Assert.Throws<ArgumentException>(() => Fft.Transform(new double[12], new double[12]));
    }
}
