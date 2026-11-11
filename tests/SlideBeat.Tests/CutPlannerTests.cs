using SlideBeat.Core.Audio;

namespace SlideBeat.Tests;

public class CutPlannerTests
{
    private static BeatAnalysis Beats(int count, Func<int, double> strength, double interval = 0.5) =>
        new(120,
            Enumerable.Range(0, count).Select(i => i * interval).ToArray(),
            Enumerable.Range(0, count).Select(strength).ToArray(),
            count * interval);

    [Fact]
    public void Picks_the_phase_with_the_strongest_beats()
    {
        // a batida forte é a 3ª de cada grupo de 4 (índices 2, 6, 10...)
        var a = Beats(65, i => i % 4 == 2 ? 10 : 1);
        var plan = CutPlanner.Plan(a, photoCount: 10, beatsPerPhoto: 4);

        Assert.Equal(4, plan.BeatsPerPhoto);
        Assert.Equal(2, plan.Phase);
        Assert.Equal(1.0, plan.CutTimes[0], 6);   // índice 2 * 0,5 s
        Assert.Equal(3.0, plan.CutTimes[1], 6);   // índice 6 * 0,5 s
    }

    [Theory]
    [InlineData(300, 30, 8)]  // sobram batidas: usa o ritmo mais calmo
    [InlineData(150, 30, 4)]
    [InlineData(70, 30, 2)]
    [InlineData(20, 30, 2)]   // não cabe nem a cada 2: usa 2 mesmo assim
    public void Auto_chooses_the_calmest_pace_that_fits_all_photos(int beats, int photos, int expected)
    {
        var plan = CutPlanner.Plan(Beats(beats, _ => 1), photos);
        Assert.Equal(expected, plan.BeatsPerPhoto);
    }

    [Fact]
    public void Works_with_no_beats()
    {
        var plan = CutPlanner.Plan(new BeatAnalysis(120, [], [], 5), photoCount: 5);
        Assert.Empty(plan.CutTimes);
    }
}
