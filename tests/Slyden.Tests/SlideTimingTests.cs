using SkiaSharp;
using Slyden.Core.Audio;
using Slyden.Core.Config;
using Slyden.Core.Render;

namespace Slyden.Tests;

public class SlideTimingTests
{
    private static BeatAnalysis Music(double seconds, double beat = 0.5)
    {
        double[] beats = Enumerable.Range(0, (int)(seconds / beat) - 1).Select(i => 0.3 + i * beat).ToArray();
        return new BeatAnalysis(60 / beat, beats, beats.Select(_ => 1.0).ToArray(), seconds);
    }

    private static SlideshowConfig Config(params double?[] seconds) => new()
    {
        Default = { Effects = [EffectKind.None], Transition = TransitionKind.Fade },
        Photos = seconds.Select((s, i) => new PhotoSpec { File = $"{i}.jpg", Seconds = s }).ToList(),
    };

    [Fact]
    public void Without_music_a_slide_lasts_exactly_the_seconds_asked()
    {
        var cfg = Config(3.5, null, null);
        cfg.SecondsPerPhoto = 2;
        var plan = SlideshowPlan.Build(cfg, 30, null);
        Assert.Equal([0, 105, 165, 225], plan.Edges);
    }

    [Fact]
    public void With_music_the_seconds_round_to_the_nearest_beat()
    {
        var cfg = Config(3.2, null, null); // 3,2 s com batida de 0,5 s = 6,4 → 6 batidas = 3 s
        cfg.BeatsPerPhoto = 4;
        var plan = SlideshowPlan.Build(cfg, 30, Music(40));
        Assert.Equal([0, 90, 150, 210], plan.Edges); // 3 s, depois 4 batidas (2 s) cada
        Assert.Equal(6, SlideshowPlan.BeatsFor(3.2, 0.5));
        Assert.Equal(1, SlideshowPlan.BeatsFor(0.1, 0.5)); // nunca menos de uma batida
    }

    [Fact]
    public void Fixed_slides_fit_with_the_music_spread_filling_the_rest_of_the_song()
    {
        var cfg = Config(2, null, null, null); // 1 slide fixo (4 batidas) + 3 que dividem o resto da música
        var plan = SlideshowPlan.Build(cfg, 30, Music(20));
        Assert.Equal(4, plan.Slides.Count);
        Assert.Equal(60, plan.Edges[1] - plan.Edges[0]);
        Assert.Equal(20.0, plan.Boundaries[^1], 3); // o último vai até o fim da música
        for (int i = 1; i < plan.Edges.Length; i++) Assert.True(plan.Edges[i] > plan.Edges[i - 1]);
        // os três comuns têm quase a mesma duração (±1 batida; o último ainda ganha o resto até o fim da música)
        var spans = Enumerable.Range(1, 3).Select(i => plan.Edges[i + 1] - plan.Edges[i]).ToArray();
        Assert.True(spans.Max() - spans.Min() <= 30, string.Join(",", spans));
    }

    [Fact]
    public void Intro_cover_plays_before_the_music_and_the_music_starts_after_it()
    {
        var cfg = Config(null, null, null);
        cfg.BeatsPerPhoto = 4;
        cfg.Photos[0].IntroSeconds = 3;
        var plan = SlideshowPlan.Build(cfg, 30, Music(40));
        Assert.Equal(3.0, plan.IntroSeconds);
        Assert.Equal(90, plan.Edges[1]);                 // a capa dura 3 s
        Assert.Equal(60, plan.Edges[2] - plan.Edges[1]); // depois, 4 batidas por foto
        Assert.Equal(0.3, plan.AudioStart, 6);           // a música toca a partir da primeira batida
        Assert.Equal(3 + (plan.Boundaries[^1] - plan.Boundaries[1]), plan.TotalSeconds, 2);
    }

    [Fact]
    public void Intro_is_ignored_without_music()
    {
        var cfg = Config(null, null);
        cfg.SecondsPerPhoto = 2;
        cfg.Photos[0].IntroSeconds = 5;
        var plan = SlideshowPlan.Build(cfg, 30, null);
        Assert.Equal(0.0, plan.IntroSeconds);
        Assert.Equal([0, 60, 120], plan.Edges);
    }

    [Fact]
    public void Image_fades_in_from_black_while_the_text_stays_independent()
    {
        var cfg = Config(null, null);
        cfg.SecondsPerPhoto = 2;
        cfg.Photos[0].ImageFadeIn = 1;
        using var preview = SlideshowPreview.Create(cfg, "pasta", null, 0.1, 30, null, _ => { var b = new SKBitmap(160, 90); using var c = new SKCanvas(b); c.Clear(new SKColor(200, 200, 200)); return b; });
        var first = preview.RenderFrame(0).GetPixel(96, 54);
        var half = preview.RenderFrame(15).GetPixel(96, 54);
        var full = preview.RenderFrame(40).GetPixel(96, 54);
        Assert.True(first.Red < 10, $"primeiro quadro deve ser preto, veio {first}");
        Assert.InRange((int)half.Red, 60, 140);
        Assert.True(full.Red > 190, $"depois do fade a imagem é normal, veio {full}");
    }

    [Fact]
    public void Slide_timing_fields_are_validated_and_saved()
    {
        var cfg = Config(2.5);
        cfg.Photos[0].IntroSeconds = 3;
        cfg.Photos[0].ImageFadeIn = 0.5;
        var back = cfg.Clone();
        Assert.Equal((2.5, 3.0, 0.5), (back.Photos[0].Seconds, back.Photos[0].IntroSeconds, back.Photos[0].ImageFadeIn));
        cfg.Photos[0].Seconds = 0;
        Assert.Throws<InvalidDataException>(cfg.Validate);
    }
}
public class ExportQualityTests
{
    [Fact]
    public void Quality_profiles_map_to_the_measured_encoder_settings()
    {
        Assert.Equal(new VideoEncoding("libx264", 18, "veryfast"), VideoEncoding.For(ExportQuality.Fast));
        Assert.Equal(new VideoEncoding("libx264", 22, "medium"), VideoEncoding.For(ExportQuality.Balanced));
        Assert.Equal(new VideoEncoding("libx265", 22, "fast"), VideoEncoding.For(ExportQuality.Compact));
    }
}