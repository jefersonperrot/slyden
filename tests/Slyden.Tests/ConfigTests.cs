using Slyden.Core.Config;
using Slyden.Core.Render;

namespace Slyden.Tests;

public class ConfigTests
{
    // mesmo formato que o protótipo em Python gera
    private const string PythonStyleJson = """
        {
         "bpm": 120,
         "beats_per_photo": "auto",
         "format": "9:16",
         "default": {
          "effects": ["zoom_in", "zoom_out", "pan_lr", "pan_rl"],
          "order": "cycle",
          "intensity": 0.15,
          "transition": "fade",
          "fade_beats": 0.5
         },
         "photos": [
          {"file": "a.jpg"},
          {"file": "b.jpg", "effect": "zoom_in", "intensity": 0.25, "transition": "cut"},
          {"file": "c.jpg", "effect": "none"},
          {"file": "d.jpg", "zoom": [1.0, 1.5], "center": [[0.2, 0.3], [0.8, 0.7]]}
         ]
        }
        """;

    private static SlideshowConfig Load(string json)
    {
        string path = Path.GetTempFileName();
        try { File.WriteAllText(path, json); return SlideshowConfig.Load(path); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Reads_the_file_format_used_by_the_python_prototype()
    {
        var cfg = Load(PythonStyleJson);

        Assert.Null(cfg.BeatsPerPhoto);               // "auto"
        Assert.Equal("9:16", cfg.Format);
        Assert.Equal(TransitionKind.Fade, cfg.Default.Transition);
        Assert.Equal(EffectKind.PanLr, cfg.Default.Effects[2]);
        Assert.Equal(4, cfg.Photos.Count);
        Assert.Equal(EffectKind.ZoomIn, cfg.Photos[1].Effect);
        Assert.Equal(TransitionKind.Cut, cfg.Photos[1].Transition);
    }

    [Fact]
    public void Save_and_load_round_trip()
    {
        var cfg = Load(PythonStyleJson);
        cfg.BeatsPerPhoto = 4;
        string path = Path.GetTempFileName();
        try
        {
            cfg.Save(path);
            var again = SlideshowConfig.Load(path);
            Assert.Equal(4, again.BeatsPerPhoto);
            Assert.Equal("9:16", again.Format);
            Assert.Equal(cfg.Photos.Select(p => p.File), again.Photos.Select(p => p.File));
            Assert.Equal([1.0, 1.5], again.Photos[3].Zoom!);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Auto_is_written_back_as_the_word_auto()
    {
        string path = Path.GetTempFileName();
        try
        {
            new SlideshowConfig { BeatsPerPhoto = null }.Save(path);
            Assert.Contains("\"beats_per_photo\": \"auto\"", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Resolve_cycles_the_default_effects_and_applies_per_photo_overrides()
    {
        var slides = SlideResolver.Resolve(Load(PythonStyleJson));

        Assert.Equal(EffectKind.ZoomIn, slides[0].Effect);   // 1ª do ciclo
        Assert.Equal(TransitionKind.Fade, slides[0].Transition);
        Assert.Equal(EffectKind.ZoomIn, slides[1].Effect);   // exceção
        Assert.Equal(1.25, SlideResolver.MotionOf(slides[1], 2000, 1500, 16.0 / 9).Zoom1, 6); // intensidade 0,25
        Assert.Equal(TransitionKind.Cut, slides[1].Transition);
        Assert.Equal(EffectKind.None, slides[2].Effect);
        Assert.Equal(new Motion(1, 1.5, .2, .3, .8, .7),
                     SlideResolver.MotionOf(slides[3], 2000, 1500, 16.0 / 9)); // zoom/centro explícitos
    }

    [Fact]
    public void A_single_effect_applies_to_every_photo()
    {
        var cfg = Load(PythonStyleJson);
        cfg.Default.Effects = [EffectKind.None];
        foreach (var p in cfg.Photos) { p.Effect = null; p.Zoom = null; p.Center = null; }

        Assert.All(SlideResolver.Resolve(cfg), s => Assert.Equal(EffectKind.None, s.Effect));
    }

    [Fact]
    public void Random_order_is_repeatable()
    {
        var cfg = Load(PythonStyleJson);
        cfg.Default.Order = EffectOrder.Random;
        cfg.Photos = Enumerable.Range(0, 20).Select(i => new PhotoSpec { File = $"{i}.jpg" }).ToList();

        var a = SlideResolver.Resolve(cfg).Select(s => s.Effect).ToList();
        var b = SlideResolver.Resolve(cfg).Select(s => s.Effect).ToList();
        Assert.Equal(a, b);
        Assert.True(a.Distinct().Count() > 1);
    }

    [Theory]
    [InlineData("""{"format": "4:3"}""")]
    [InlineData("""{"bpm": 0}""")]
    [InlineData("""{"beats_per_photo": 0}""")]
    [InlineData("""{"default": {"effects": []}}""")]
    [InlineData("""{"default": {"intensity": 3}}""")]
    [InlineData("""{"photos": [{"file": ""}]}""")]
    [InlineData("""{"photos": [{"file": "a.jpg", "zoom": [1.2]}]}""")]
    [InlineData("""{"photos": [{"file": "a.jpg", "effect": "spin"}]}""")]
    [InlineData("{ isto não é json")]
    public void Invalid_config_is_rejected_with_a_clear_error(string json)
    {
        Assert.Throws<InvalidDataException>(() => Load(json));
    }

    [Fact]
    public void CreateFor_lists_only_jpegs_in_alphabetical_order()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            foreach (var f in new[] { "b.jpg", "A.JPG", "c.jpeg", "notas.txt", "d.png" })
                File.WriteAllText(Path.Combine(dir, f), "x");

            var cfg = SlideshowConfig.CreateFor(dir);
            Assert.Equal(["A.JPG", "b.jpg", "c.jpeg"], cfg.Photos.Select(p => p.File));
        }
        finally { Directory.Delete(dir, true); }
    }
}
