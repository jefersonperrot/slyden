using Slyden.Core.Config;
using Slyden.Core.Render;

namespace Slyden.Tests;

public class TextAnimationAndDefaultsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sld_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    // um slide de 120 quadros (4 s a 30 fps), com o texto presente nele todo
    private static double Alpha(TextBlock block, int frame)
    {
        var text = new ActiveText(block, First: true, Last: true, StartSlide: 0, EndSlide: 0);
        return OverlayPlan.AlphaOf(text, new TextTiming(frame, 120, 0.5, 30, [0, 120], 0));
    }

    [Fact]
    public void Text_waits_for_its_delay_then_fades_in()
    {
        var t = new TextBlock { Delay = 1, FadeIn = 1, FadeOut = 0 };
        Assert.Equal(0, Alpha(t, 0));
        Assert.Equal(0, Alpha(t, 29));
        Assert.Equal(0.5, Alpha(t, 45), 6); // 1,5 s: metade do fade
        Assert.Equal(1, Alpha(t, 60));
        Assert.Equal(1, Alpha(t, 119));
    }

    [Fact]
    public void Text_stays_for_its_duration_then_fades_out_and_disappears()
    {
        var t = new TextBlock { Delay = 0.5, FadeIn = 0.5, FadeOut = 0.5, Duration = 2 }; // some em 2,5 s
        Assert.Equal(1, Alpha(t, 45));              // 1,5 s: visível
        Assert.Equal(0.5, Alpha(t, 68), 1);              // 2,27 s: perto da metade do fade de saída
        Assert.Equal(0, Alpha(t, 80));              // 2,67 s: já saiu
        Assert.Equal(0, Alpha(t, 119));
    }

    [Fact]
    public void Without_explicit_times_the_old_beat_based_fade_still_applies()
    {
        var t = new TextBlock { FadeBeats = 0.5 }; // 0,5 batida de 0,5 s = 0,25 s ≈ 7,5 quadros
        Assert.Equal(0, Alpha(t, 0));
        Assert.Equal(1, Alpha(t, 30));
        Assert.True(Alpha(t, 119) < 0.01);
    }

    [Fact]
    public void Text_that_spans_two_slides_keeps_one_continuous_timeline()
    {
        // dois slides de 60 quadros; texto com 1 s de fade-in e sem fade-out, atravessando os dois
        var block = new TextBlock { FadeIn = 1, FadeOut = 0 };
        int[] edges = [0, 60, 120];
        double At(int slide, int frame) => OverlayPlan.AlphaOf(
            new ActiveText(block, First: slide == 0, Last: slide == 1, StartSlide: 0, EndSlide: 1),
            new TextTiming(frame, 60, 0.5, 30, edges, slide));
        Assert.Equal(1, At(0, 30), 6);   // 1 s: fade completo
        Assert.Equal(1, At(1, 0), 6);    // começo do segundo slide: continua visível, sem recomeçar o fade
        Assert.Equal(1, At(1, 59), 6);
    }

    [Fact]
    public void Animation_none_is_always_visible_even_with_a_delay()
    {
        var t = new TextBlock { Animation = TextAnimation.None, Delay = 2 };
        Assert.Equal(1, Alpha(t, 0));
    }

    [Fact]
    public void Animation_fields_survive_save_and_load_and_are_validated()
    {
        var cfg = new SlideshowConfig
        {
            Photos = [new PhotoSpec { File = "a.jpg", Texts = [new TextBlock { Text = "x", Delay = 1.5, FadeIn = 2, FadeOut = 0.5, Duration = 3 }] }],
        };
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "c.json");
        cfg.Save(path);
        var back = SlideshowConfig.Load(path).Photos[0].Texts![0];
        Assert.Equal((1.5, 2.0, 0.5, 3.0), (back.Delay, back.FadeIn, back.FadeOut, back.Duration));

        cfg.Photos[0].Texts![0].Delay = -1;
        Assert.Throws<InvalidDataException>(cfg.Validate);
    }

    [Fact]
    public void User_defaults_are_saved_loaded_and_applied_to_new_projects()
    {
        var store = new UserDefaultsStore(Path.Combine(_dir, "defaults.json"));
        Assert.False(store.Exists);
        Assert.Equal("16:9", store.Load().Format); // sem arquivo: padrão de fábrica

        var source = new SlideshowConfig { Format = "9:16", Resolution = 2160, MusicMode = MusicMode.FitSlides, AudioFadeIn = 2 };
        source.Default.Scale = 0.8;
        source.Default.BackgroundColor = "#112233";
        var defaults = store.Load();
        defaults.CaptureFrom(source);
        store.Save(defaults);

        var fresh = new SlideshowConfig();
        store.Load().ApplyTo(fresh);
        Assert.Equal(("9:16", 2160, MusicMode.FitSlides, 2.0), (fresh.Format, fresh.Resolution, fresh.MusicMode, fresh.AudioFadeIn));
        Assert.Equal(0.8, fresh.Default.Scale);
        Assert.Equal("#112233", fresh.Default.BackgroundColor);

        store.Reset();
        Assert.False(store.Exists);
        Assert.Equal(1.0, store.Load().Default.Scale);
    }

    [Fact]
    public void Default_text_style_is_used_for_new_texts_without_copying_content_or_position()
    {
        var defaults = new UserDefaults();
        Assert.Equal("Georgia", defaults.NewText("a").Font); // de fábrica

        defaults.CaptureTextStyle(new TextBlock
        {
            Text = "Maria", Font = "Courier New", Size = 0.2, Color = "#FF0000", Bold = true, X = 0.1, Y = 0.9, Span = 3, FadeIn = 2,
        });
        var created = defaults.NewText("Novo");
        Assert.Equal(("Novo", "Courier New", 0.2, "#FF0000", true, 2.0), (created.Text, created.Font, created.Size, created.Color, created.Bold, created.FadeIn));
        Assert.Equal((0.5, 0.5, 1), (created.X, created.Y, created.Span));
    }

    [Fact]
    public void A_corrupted_defaults_file_falls_back_to_factory_settings()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "defaults.json");
        File.WriteAllText(path, "{ isto não é json");
        Assert.Equal(1080, new UserDefaultsStore(path).Load().Resolution);
    }
}

