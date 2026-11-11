using Slyden.Core.Config;
using Slyden.Core.Render;

namespace Slyden.Tests;

public class TimelineTests
{
    private static readonly TransitionKind Cut = TransitionKind.Cut, Fade = TransitionKind.Fade;

    [Fact]
    public void Has_one_item_per_video_frame_and_t_goes_from_0_to_1_on_each_slide()
    {
        int[] edges = [0, 60, 120, 180];
        var frames = Timeline.Frames(edges, [Cut, Cut, Cut], fadeFrames: 8).ToList();

        Assert.Equal(180, frames.Count);
        for (int s = 0; s < 3; s++)
        {
            var mine = frames.Where(f => f.Slide == s).ToList();
            Assert.Equal(60, mine.Count);
            Assert.Equal(0, mine[0].T);
            Assert.Equal(1, mine[^1].T, 9);
        }
        Assert.All(frames, f => Assert.Equal(0, f.FadeAlpha));
    }

    [Fact]
    public void Slides_can_have_different_lengths_when_beats_are_irregular()
    {
        var frames = Timeline.Frames([0, 50, 130], [Cut, Cut], 8).ToList();
        Assert.Equal(50, frames.Count(f => f.Slide == 0));
        Assert.Equal(80, frames.Count(f => f.Slide == 1));
    }

    [Fact]
    public void Fade_into_the_next_slide_ends_exactly_at_the_cut()
    {
        // transição do slide 1 para o 2 é fade; do 0 para o 1 é corte
        var frames = Timeline.Frames([0, 60, 120, 180], [Cut, Cut, Fade], fadeFrames: 8).ToList();

        var slide1 = frames.Where(f => f.Slide == 1).ToList();
        var fading = slide1.Where(f => f.FadeAlpha > 0).ToList();
        Assert.Equal(8, fading.Count);                       // só os últimos 8 quadros
        Assert.Equal(slide1.TakeLast(8), fading);
        Assert.Equal(1, slide1[^1].FadeAlpha, 9);            // no último quadro o próximo já está inteiro
        Assert.Equal(1.0 / 8, fading[0].FadeAlpha, 9);
        Assert.True(fading.Zip(fading.Skip(1), (a, b) => b.FadeAlpha > a.FadeAlpha).All(x => x));

        Assert.All(frames.Where(f => f.Slide == 0), f => Assert.Equal(0, f.FadeAlpha)); // corte: sem fade
        Assert.All(frames.Where(f => f.Slide == 2), f => Assert.Equal(0, f.FadeAlpha)); // último slide
    }

    [Fact]
    public void Fade_never_exceeds_the_slide_length()
    {
        var frames = Timeline.Frames([0, 5, 10], [Cut, Fade], fadeFrames: 20).ToList();
        Assert.All(frames.Where(f => f.Slide == 0), f => Assert.InRange(f.FadeAlpha, 0.0, 1.0));
    }
}
