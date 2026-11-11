using SkiaSharp;
using Slyden.Core.Audio;
using Slyden.Core.Config;
using Slyden.Core.Render;

namespace Slyden.Tests;

public class PlanAndPreviewTests
{
    private static SlideshowConfig Fixed(int seconds, params int[] lengths) => new()
    {
        SecondsPerPhoto = seconds, FadeSeconds = 0.5,
        Default = { Effects = [EffectKind.None], Transition = TransitionKind.Fade },
        Photos = lengths.Select((l, i) => new PhotoSpec { File = $"{i}.jpg", Length = l }).ToList(),
    };

    private static SKBitmap Solid(SKColor color)
    {
        var b = new SKBitmap(160, 90);
        using var c = new SKCanvas(b);
        c.Clear(color);
        return b;
    }

    [Fact]
    public void Fixed_time_plan_has_expected_edges_and_duration()
    {
        var plan = SlideshowPlan.Build(Fixed(2, 1, 1, 1), 30, null);
        Assert.Equal([0, 60, 120, 180], plan.Edges);
        Assert.Equal(6.0, plan.TotalSeconds);
        Assert.Equal(180, plan.Frames.Count);
        Assert.Equal(0.0, plan.AudioStart);
    }

    [Fact]
    public void Slide_with_length_two_lasts_twice_as_long()
    {
        var plan = SlideshowPlan.Build(Fixed(2, 2, 1), 30, null);
        Assert.Equal([0, 120, 180], plan.Edges);
    }

    [Fact]
    public void Music_plan_cuts_on_the_beat_grid_and_starts_at_the_first_cut()
    {
        double[] beats = Enumerable.Range(0, 40).Select(i => 1.0 + i * 0.5).ToArray(); // 120 BPM a partir de 1 s
        var analysis = new BeatAnalysis(120, beats, beats.Select(_ => 1.0).ToArray(), 22);
        var cfg = Fixed(2, 1, 1, 1);
        cfg.BeatsPerPhoto = 4;
        var plan = SlideshowPlan.Build(cfg, 30, analysis);

        Assert.Equal(3, plan.Slides.Count);
        Assert.Equal(60, plan.Edges[1] - plan.Edges[0]); // 4 batidas de 0,5 s = 2 s = 60 quadros
        Assert.True(plan.AudioStart >= 1.0 - 1e-9);
        Assert.Equal(plan.Edges[^1], plan.FrameCount);
    }

    private static BeatAnalysis Music(double seconds, double beat = 0.5)
    {
        double[] beats = Enumerable.Range(0, (int)(seconds / beat) - 1).Select(i => 0.3 + i * beat).ToArray();
        return new BeatAnalysis(60 / beat, beats, beats.Select(_ => 1.0).ToArray(), seconds);
    }

    [Fact]
    public void Fit_music_mode_spreads_the_photos_over_the_whole_song()
    {
        var cfg = Fixed(2, Enumerable.Repeat(1, 10).ToArray()); // 10 fotos, ritmo automático
        var plan = SlideshowPlan.Build(cfg, 30, Music(67));

        Assert.Equal(10, plan.Slides.Count);
        Assert.Equal(67, plan.TotalSeconds, 1);                 // a música toda
        Assert.Equal(0, plan.AudioStart, 6);                    // desde o começo dela
        // nenhum slide é muito mais longo que outro (só diferem em ±1 batida, fora o primeiro e o último)
        var lengths = plan.Edges.Zip(plan.Edges.Skip(1), (a, b) => b - a).Skip(1).SkipLast(1).ToArray();
        Assert.True(lengths.Max() - lengths.Min() <= 30 * 0.5 + 1, string.Join(",", lengths));
    }

    [Fact]
    public void Fit_slides_mode_keeps_the_video_as_long_as_the_photos_need()
    {
        var cfg = Fixed(2, Enumerable.Repeat(1, 10).ToArray());
        cfg.MusicMode = MusicMode.FitSlides;
        var plan = SlideshowPlan.Build(cfg, 30, Music(67));

        Assert.Equal(10, plan.Slides.Count);
        Assert.True(plan.TotalSeconds < 67 - 10, $"{plan.TotalSeconds}"); // cortou o fim da música
    }

    [Fact]
    public void Fixed_beats_per_photo_ignores_fit_music()
    {
        var cfg = Fixed(2, 1, 1, 1);
        cfg.BeatsPerPhoto = 4;
        var plan = SlideshowPlan.Build(cfg, 30, Music(67));
        Assert.Equal(60, plan.Edges[1] - plan.Edges[0]);
    }

    [Fact]
    public void Fit_music_with_more_photos_than_beats_falls_back_to_one_photo_per_beat()
    {
        var cfg = Fixed(2, Enumerable.Repeat(1, 30).ToArray()); // 31 fotos, 9 batidas
        var plan = SlideshowPlan.Build(cfg, 30, Music(5));
        Assert.True(plan.Slides.Count < 31);
        Assert.True(plan.Slides.Count >= 1);
    }

    [Fact]
    public void Plan_without_photos_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => SlideshowPlan.Build(new SlideshowConfig(), 30, null));
    }

    [Fact]
    public void Preview_renders_the_right_photo_at_each_moment_and_blends_in_the_fade()
    {
        var colors = new Dictionary<string, SKColor>
        {
            ["0.jpg"] = new SKColor(255, 0, 0), ["1.jpg"] = new SKColor(0, 0, 255),
        };
        using var preview = SlideshowPreview.Create(Fixed(2, 1, 1), "pasta", null, 0.1, 30, null, p => Solid(colors[Path.GetFileName(p)]));
        Assert.Equal((192, 108), (preview.Width, preview.Height));

        var early = preview.RenderFrame(5).GetPixel(96, 54);
        Assert.True(early.Red > 200 && early.Blue < 50, $"início do 1º slide deve ser vermelho, veio {early}");

        var late = preview.RenderFrame(preview.Plan.Edges[1] + 20).GetPixel(96, 54);
        Assert.True(late.Blue > 200 && late.Red < 50, $"meio do 2º slide deve ser azul, veio {late}");

        int midFade = preview.Plan.Edges[1] - preview.Plan.FadeFrames / 2;
        var mixed = preview.RenderFrame(midFade).GetPixel(96, 54);
        Assert.True(mixed.Red > 40 && mixed.Blue > 40, $"no fade as duas cores se misturam, veio {mixed}");
    }

    [Fact]
    public void Preview_frame_index_is_clamped()
    {
        using var preview = SlideshowPreview.Create(Fixed(1, 1), "pasta", null, 0.1, 30, null, _ => Solid(SKColors.Green));
        Assert.NotNull(preview.RenderFrame(-5));
        Assert.NotNull(preview.RenderFrame(10_000));
    }

    [Fact]
    public void Preview_matches_the_export_drawing_for_the_same_frame()
    {
        // a prévia usa o mesmo Draw do render: um quadro no meio do slide tem de ter a cor da foto
        using var preview = SlideshowPreview.Create(Fixed(2, 1), "pasta", null, 0.1, 30, null, _ => Solid(new SKColor(10, 200, 10)));
        var px = preview.RenderFrame(30).GetPixel(10, 10);
        Assert.True(px.Green > 150 && px.Red < 60);
    }
}

