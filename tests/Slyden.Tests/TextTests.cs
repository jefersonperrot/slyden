using SkiaSharp;
using Slyden.Core.Config;
using Slyden.Core.Render;

namespace Slyden.Tests;

public class TextPainterTests : IDisposable
{
    private const int W = 400, H = 225;
    private readonly FontResolver _fonts = new();
    private readonly TextPainter _painter;

    public TextPainterTests() => _painter = new TextPainter(_fonts);
    public void Dispose() => _fonts.Dispose();

    private static TextBlock Block(string text, Action<TextBlock>? tweak = null)
    {
        var b = new TextBlock { Text = text, Font = SKTypeface.Default.FamilyName, Size = 0.1, Shadow = false };
        tweak?.Invoke(b);
        return b;
    }

    private SKBitmap Draw(TextBlock block, double alpha = 1, SKColor? background = null)
    {
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(background ?? SKColors.Black);
        using var canvas = new SKCanvas(bmp);
        _painter.Draw(canvas, block, W, H, alpha);
        return bmp;
    }

    /// <summary>Caixa que envolve os pixels "acesos" (texto branco sobre fundo preto).</summary>
    private static (int MinX, int MinY, int MaxX, int MaxY, int Count) Lit(SKBitmap bmp, Func<SKColor, bool>? isLit = null)
    {
        isLit ??= c => c.Red > 100;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
                if (isLit(bmp.GetPixel(x, y)))
                {
                    count++;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
        return (minX, minY, maxX, maxY, count);
    }

    [Fact]
    public void Left_align_starts_at_the_anchor()
    {
        using var bmp = Draw(Block("MMMM", b => { b.Align = TextAlign.Left; b.X = 0.1; }));
        Assert.InRange(Lit(bmp).MinX, W * 0.1 - 6, W * 0.1 + 12);
    }

    [Fact]
    public void Right_align_ends_at_the_anchor()
    {
        using var bmp = Draw(Block("MMMM", b => { b.Align = TextAlign.Right; b.X = 0.9; }));
        Assert.InRange(Lit(bmp).MaxX, W * 0.9 - 12, W * 0.9 + 6);
    }

    [Fact]
    public void Center_align_is_centered_on_the_anchor()
    {
        using var bmp = Draw(Block("MMMM", b => { b.Align = TextAlign.Center; b.X = 0.3; }));
        var r = Lit(bmp);
        Assert.InRange((r.MinX + r.MaxX) / 2.0, W * 0.3 - 8, W * 0.3 + 8);
    }

    [Fact]
    public void Y_moves_the_block_vertically()
    {
        using var up = Draw(Block("MMMM", b => b.Y = 0.25));
        using var down = Draw(Block("MMMM", b => b.Y = 0.75));
        var a = Lit(up); var c = Lit(down);
        Assert.InRange(((c.MinY + c.MaxY) - (a.MinY + a.MaxY)) / 2.0, H * 0.5 - 6, H * 0.5 + 6);
    }

    [Fact]
    public void Alpha_scales_the_text_opacity()
    {
        using var bmp = Draw(Block("MMMM"), alpha: 0.5);
        int max = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++) max = Math.Max(max, bmp.GetPixel(x, y).Red);
        Assert.InRange(max, 124, 132);
    }

    [Fact]
    public void Color_with_alpha_channel_is_respected()
    {
        using var bmp = Draw(Block("MMMM", b => b.Color = "#FF000080"));
        var pixel = Enumerable.Range(0, W).SelectMany(x => Enumerable.Range(0, H).Select(y => bmp.GetPixel(x, y)))
                              .MaxBy(c => c.Red);
        Assert.InRange(pixel.Red, 124, 132);
        Assert.Equal(0, pixel.Green);
    }

    [Fact]
    public void Zero_alpha_or_empty_text_draws_nothing()
    {
        using var invisible = Draw(Block("MMMM"), alpha: 0);
        using var empty = Draw(Block("   "));
        Assert.Equal(0, Lit(invisible).Count);
        Assert.Equal(0, Lit(empty).Count);
    }

    [Fact]
    public void A_word_too_wide_is_shrunk_to_fit_the_max_width()
    {
        using var bmp = Draw(Block(new string('W', 40), b => { b.Size = 0.2; b.MaxWidth = 0.5; }));
        var r = Lit(bmp);
        double width = r.MaxX - r.MinX;
        Assert.True(width <= W * 0.5 + 4, $"largura {width}");
        Assert.True(width >= W * 0.4, $"largura {width}");
    }

    [Fact]
    public void Long_text_wraps_into_several_lines_inside_the_max_width()
    {
        using var one = Draw(Block("Wedding"));
        using var many = Draw(Block("Wedding day Maria Joao Estacao Iguacu", b => b.MaxWidth = 0.4));
        var a = Lit(one); var m = Lit(many);

        Assert.True(m.MaxY - m.MinY > (a.MaxY - a.MinY) * 1.8);
        Assert.True(m.MaxX - m.MinX <= W * 0.4 + 4);
    }

    [Fact]
    public void Explicit_line_break_makes_two_lines_and_line_spacing_is_respected()
    {
        double fontPx = 0.1 * H;
        using var one = Draw(Block("A"));
        using var two = Draw(Block("A\nA"));
        using var wide = Draw(Block("A\nA", b => b.LineSpacing = 2));
        double h1 = Lit(one).MaxY - Lit(one).MinY;
        double h2 = Lit(two).MaxY - Lit(two).MinY;
        double h3 = Lit(wide).MaxY - Lit(wide).MinY;

        Assert.True(h2 - h1 >= fontPx * 0.9, $"2ª linha deve ficar abaixo da 1ª: {h1} -> {h2}");
        Assert.True(h3 - h2 >= fontPx * 0.9, $"line_spacing 2 deve afastar as linhas: {h2} -> {h3}");
    }

    [Fact]
    public void Shadow_darkens_the_photo_around_the_text()
    {
        var gray = new SKColor(128, 128, 128);
        using var with = Draw(Block("MMMM", b => b.Shadow = true), background: gray);
        using var without = Draw(Block("MMMM", b => b.Shadow = false), background: gray);

        Assert.True(Lit(with, c => c.Red < 118).Count > 0);
        Assert.Equal(0, Lit(without, c => c.Red < 118).Count);
    }

    [Fact]
    public void Outline_draws_a_dark_edge_around_the_letters()
    {
        var gray = new SKColor(128, 128, 128);
        using var with = Draw(Block("MMMM", b => b.Outline = 0.06), background: gray);
        using var without = Draw(Block("MMMM"), background: gray);

        Assert.True(Lit(with, c => c.Red < 100).Count > 0);
        Assert.Equal(0, Lit(without, c => c.Red < 100).Count);
    }

    [Fact]
    public void Accents_are_drawn_without_errors()
    {
        using var bmp = Draw(Block("João Estação Ação"));
        Assert.True(Lit(bmp).Count > 100);
    }

    [Fact]
    public void Wrap_splits_by_words_and_keeps_blank_lines()
    {
        using var font = new SKFont(SKTypeface.Default, 20);
        Assert.Single(TextPainter.Wrap("um dois três", font, 10_000));
        Assert.Equal(["um", "dois", "três"], TextPainter.Wrap("um dois três", font, 1));
        Assert.Equal(["a", "", "b"], TextPainter.Wrap("a\n\nb", font, 10_000));
    }
}

public class FontResolverTests
{
    [Fact]
    public void Unknown_font_falls_back_and_warns_once()
    {
        using var fonts = new FontResolver();
        var warnings = new List<string>();

        var face = fonts.Resolve("Fonte Que Nao Existe 123", false, false, warnings);
        fonts.Resolve("Fonte Que Nao Existe 123", false, false, warnings);

        Assert.NotNull(face);
        Assert.Single(warnings);
        Assert.Contains("Fonte Que Nao Existe 123", warnings[0]);
    }

    [Fact]
    public void An_installed_font_is_found_without_warning()
    {
        using var fonts = new FontResolver();
        var warnings = new List<string>();

        var face = fonts.Resolve(SKTypeface.Default.FamilyName, false, false, warnings);

        Assert.Empty(warnings);
        Assert.Equal(SKTypeface.Default.FamilyName, face.FamilyName, ignoreCase: true);
    }

    [Fact]
    public void Missing_folder_is_ignored()
    {
        using var fonts = new FontResolver([Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid())]);
        Assert.NotNull(fonts.Resolve(SKTypeface.Default.FamilyName, false, false));
    }
}

public class OverlayPlanTests
{
    private static ResolvedSlide Slide(params TextBlock[] texts) =>
        new("x.jpg", EffectKind.None, 0.15, TransitionKind.Cut, Texts: texts);

    [Fact]
    public void A_text_with_span_appears_in_that_many_consecutive_slides()
    {
        var title = new TextBlock { Text = "Título", Span = 3 };
        var plan = OverlayPlan.Build([Slide(), Slide(title), Slide(), Slide(), Slide()]);

        Assert.Equal([0, 1, 1, 1, 0], plan.Select(p => p.Count));
        Assert.True(plan[1][0].First && !plan[1][0].Last);
        Assert.True(!plan[2][0].First && !plan[2][0].Last);
        Assert.True(!plan[3][0].First && plan[3][0].Last);
    }

    [Fact]
    public void Span_past_the_end_is_clipped_and_closes_on_the_last_slide()
    {
        var plan = OverlayPlan.Build([Slide(new TextBlock { Text = "x", Span = 10 }), Slide()]);
        Assert.True(plan[1][0].Last);
    }

    [Fact]
    public void Fade_in_on_the_first_slide_and_fade_out_on_the_last()
    {
        var only = new ActiveText(new TextBlock(), First: true, Last: true);
        Assert.Equal(0, OverlayPlan.AlphaOf(only, frame: 0, frames: 60, fadeFrames: 8));
        Assert.Equal(0.5, OverlayPlan.AlphaOf(only, 4, 60, 8), 9);
        Assert.Equal(1, OverlayPlan.AlphaOf(only, 30, 60, 8));
        Assert.Equal(0.5, OverlayPlan.AlphaOf(only, 55, 60, 8), 9);
        Assert.Equal(0, OverlayPlan.AlphaOf(only, 59, 60, 8));
    }

    [Fact]
    public void A_middle_slide_of_a_span_keeps_the_text_fully_visible()
    {
        var middle = new ActiveText(new TextBlock(), First: false, Last: false);
        Assert.All(new[] { 0, 1, 30, 58, 59 }, f => Assert.Equal(1, OverlayPlan.AlphaOf(middle, f, 60, 8)));
    }

    [Fact]
    public void Animation_none_is_always_opaque()
    {
        var t = new ActiveText(new TextBlock { Animation = TextAnimation.None }, true, true);
        Assert.Equal(1, OverlayPlan.AlphaOf(t, 0, 60, 8));
        Assert.Equal(1, OverlayPlan.AlphaOf(t, 59, 60, 8));
    }

    [Fact]
    public void Fade_frames_follow_the_beat_length()
    {
        var timing = new TextTiming(0, 100, BeatSeconds: 0.5, Fps: 30);
        Assert.Equal(8, timing.FadeFrames(new TextBlock { FadeBeats = 0.5 })); // 0,25 s = 7,5 -> 8 quadros
        Assert.Equal(1, timing.FadeFrames(new TextBlock { FadeBeats = 0.001 }));
    }
}

public class SlideTextRenderingTests
{
    private const int W = 192, H = 108;

    [Fact]
    public void Dim_darkens_the_photo()
    {
        var bmp = new SKBitmap(300, 200, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(SKColors.White);
        using var slide = new SlideRenderer(bmp, new Motion(1, 1, .5, .5, .5, .5), W, H, dim: 0.5);
        using var frame = new SKBitmap(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(frame);

        slide.Draw(canvas, 0);
        Assert.InRange(frame.GetPixel(W / 2, H / 2).Red, 124, 132);
    }

    [Fact]
    public void Text_fades_in_over_the_slide()
    {
        using var fonts = new FontResolver();
        var block = new TextBlock { Text = "MMMM", Font = SKTypeface.Default.FamilyName, Size = 0.3, Shadow = false, Color = "#FFFFFF" };
        var bmp = new SKBitmap(300, 200, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(SKColors.Black);
        using var slide = new SlideRenderer(bmp, new Motion(1, 1, .5, .5, .5, .5), W, H, 0,
            [new ActiveText(block, First: true, Last: true)], new TextPainter(fonts));
        using var frame = new SKBitmap(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(frame);

        int Brightest()
        {
            int max = 0;
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) max = Math.Max(max, frame.GetPixel(x, y).Red);
            return max;
        }

        canvas.Clear(SKColors.Black); slide.Draw(canvas, 0, new TextTiming(0, 60, 0.5, 30));
        Assert.Equal(0, Brightest());                    // primeiro quadro: texto ainda invisível
        canvas.Clear(SKColors.Black); slide.Draw(canvas, .5, new TextTiming(30, 60, 0.5, 30));
        Assert.True(Brightest() > 240);                  // meio: texto inteiro
        canvas.Clear(SKColors.Black); slide.Draw(canvas, 1, new TextTiming(59, 60, 0.5, 30));
        Assert.Equal(0, Brightest());                    // último quadro: já saiu
    }
}

public class CoverAndTextConfigTests
{
    private static SlideshowConfig Load(string json)
    {
        string path = Path.GetTempFileName();
        try { File.WriteAllText(path, json); return SlideshowConfig.Load(path); }
        finally { File.Delete(path); }
    }

    private const string Cover = """
        {
         "photos": [
          {"file": "capa.jpg", "length": 2, "dim": 0.35, "effect": "none", "texts": [
            {"text": "Wedding day", "font": "Segoe Script", "size": 0.07, "align": "left", "x": 0.08, "y": 0.35},
            {"text": "Maria e João", "font": "Georgia", "bold": true, "size": 0.1, "color": "#FFE9B0", "y": 0.5, "span": 2},
            {"text": "11/11/2026", "size": 0.04, "align": "right", "x": 0.92, "y": 0.9, "animation": "none"}
          ]},
          {"file": "b.jpg"}
         ]
        }
        """;

    [Fact]
    public void Cover_with_three_texts_is_read_from_json()
    {
        var cfg = Load(Cover);
        var cover = cfg.Photos[0];

        Assert.Equal(2, cover.Length);
        Assert.Equal(0.35, cover.Dim);
        Assert.Equal(3, cover.Texts!.Count);
        Assert.Equal("Segoe Script", cover.Texts[0].Font);
        Assert.Equal(TextAlign.Left, cover.Texts[0].Align);
        Assert.Equal(TextAlign.Right, cover.Texts[2].Align);
        Assert.True(cover.Texts[1].Bold);
        Assert.Equal(2, cover.Texts[1].Span);
        Assert.Equal(TextAnimation.None, cover.Texts[2].Animation);
        Assert.Equal("Maria e João", cover.Texts[1].Text);
    }

    [Fact]
    public void Texts_survive_save_and_load()
    {
        var cfg = Load(Cover);
        string path = Path.GetTempFileName();
        try
        {
            cfg.Save(path);
            var again = SlideshowConfig.Load(path);
            Assert.Equal("Maria e João", again.Photos[0].Texts![1].Text);
            Assert.Equal("#FFE9B0", again.Photos[0].Texts![1].Color);
            Assert.Equal(2, again.Photos[0].Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Resolver_carries_length_dim_and_texts()
    {
        var slides = SlideResolver.Resolve(Load(Cover));
        Assert.Equal(2, slides[0].Length);
        Assert.Equal(0.35, slides[0].Dim);
        Assert.Equal(3, slides[0].Texts!.Count);
        Assert.Equal(1, slides[1].Length);
        Assert.Equal(0, slides[1].Dim);
        Assert.Empty(slides[1].Texts!);
    }

    [Theory]
    [InlineData("""{"photos":[{"file":"a.jpg","texts":[{"text":"x","size":2}]}]}""")]
    [InlineData("""{"photos":[{"file":"a.jpg","texts":[{"text":"x","color":"vermelho"}]}]}""")]
    [InlineData("""{"photos":[{"file":"a.jpg","texts":[{"text":"x","color":"#12345"}]}]}""")]
    [InlineData("""{"photos":[{"file":"a.jpg","texts":[{"text":"x","x":1.5}]}]}""")]
    [InlineData("""{"photos":[{"file":"a.jpg","texts":[{"text":"x","span":0}]}]}""")]
    [InlineData("""{"photos":[{"file":"a.jpg","texts":[{"text":"x","align":"justify"}]}]}""")]
    [InlineData("""{"photos":[{"file":"a.jpg","length":0}]}""")]
    [InlineData("""{"photos":[{"file":"a.jpg","dim":1.5}]}""")]
    public void Invalid_text_settings_are_rejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => Load(json));
    }

    [Theory]
    [InlineData("#FFFFFF", 255, 255, 255, 255)]
    [InlineData("#FFE9B0", 255, 233, 176, 255)]
    [InlineData("#00000080", 0, 0, 0, 128)]
    [InlineData(" #0a0B0c ", 10, 11, 12, 255)]
    public void Hex_colors_are_parsed(string text, int r, int g, int b, int a)
    {
        Assert.True(HexColor.TryParse(text, out var c));
        Assert.Equal((r, g, b, a), ((int)c.R, (int)c.G, (int)c.B, (int)c.A));
    }

    [Theory]
    [InlineData("")]
    [InlineData("FFFFFF")]
    [InlineData("#FFF")]
    [InlineData("#GGGGGG")]
    [InlineData(null)]
    public void Bad_hex_colors_are_refused(string? text) => Assert.False(HexColor.TryParse(text, out _));
}

public class CoverTimingTests
{
    [Fact]
    public void A_slide_with_length_2_takes_two_beat_intervals()
    {
        double[] cuts = [10, 12, 14, 16, 18, 20];
        var b = SlideshowRenderer.Boundaries(cuts, [2, 1, 1]);
        Assert.Equal([10, 14, 16, 18], b);
    }

    [Fact]
    public void Slides_that_do_not_fit_in_the_music_are_dropped()
    {
        double[] cuts = [0, 2, 4, 6];           // 3 intervalos
        var b = SlideshowRenderer.Boundaries(cuts, [2, 2, 1]);
        Assert.Equal([0, 4], b);                // só o primeiro (2 intervalos) cabe
    }

    [Fact]
    public void No_cuts_means_no_slides()
    {
        Assert.Empty(SlideshowRenderer.Boundaries([], [1, 1]));
        Assert.Single(SlideshowRenderer.Boundaries([5], [1]));   // só o ponto inicial: 0 slides
    }
}
