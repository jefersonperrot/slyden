using Slyden.Core.Config;
using Slyden.Core.Render;

namespace Slyden.App;

/// <summary>Duração própria do slide, capa antes da música e fade-in da imagem (aba Slide).</summary>
public partial class MainWindow
{
    private void InitSlideTiming()
    {
        SlideSeconds.ValueChanged += (_, _) =>
        {
            double v = Num(SlideSeconds);
            EditSlide(s => s.Seconds = v > 0 ? v : null);
            UpdateTimingHints();
        };
        CoverIntro.ValueChanged += (_, _) =>
        {
            double v = Num(CoverIntro);
            EditSlide(s => s.IntroSeconds = v > 0 ? v : null);
            UpdateTimingHints();
        };
        CoverImageFade.ValueChanged += (_, _) =>
        {
            double v = Num(CoverImageFade);
            EditSlide(s => s.ImageFadeIn = v > 0 ? v : null);
        };
    }

    private void LoadSlideTiming(PhotoSpec spec)
    {
        SlideSeconds.Value = (decimal)(spec.Seconds ?? 0);
        CoverIntro.Value = (decimal)(spec.IntroSeconds ?? 0);
        CoverImageFade.Value = (decimal)(spec.ImageFadeIn ?? 0);
        FirstSlideOptions.IsVisible = _selected == 0;
        LengthBox.IsEnabled = spec.Seconds is null; // a duração própria vale no lugar dos ciclos
        UpdateTimingHints();
    }

    /// <summary>Explica o que a duração pedida vira na prática (com música, em batidas).</summary>
    private void UpdateTimingHints()
    {
        if (Current is not { } spec) return;
        LengthBox.IsEnabled = spec.Seconds is null;
        double? beat = null;
        if (_audio is not null && _analyses.TryGetValue(_audio, out var cached))
            beat = SlideshowPlan.BeatSecondsOf(cached.Analysis, _cfg.Bpm);

        if (spec.Seconds is not { } seconds)
            SlideSecondsHint.Text = "Vazio (0) = o tempo padrão. Com música, a duração própria arredonda para a batida mais próxima.";
        else if (_audio is null)
            SlideSecondsHint.Text = "Sem música: este slide dura exatamente isso.";
        else if (beat is { } b)
        {
            int beats = SlideshowPlan.BeatsFor(seconds, b);
            SlideSecondsHint.Text = $"Com a música: {beats} batida(s), cerca de {beats * b:0.0} s (arredondado para a batida).";
        }
        else
            SlideSecondsHint.Text = "Com música, arredonda para a batida mais próxima (aparece aqui depois de tocar a prévia uma vez).";

        CoverIntroHint.Text = _audio is null
            ? "A capa antes da música só vale quando há música. A imagem aparece saindo do preto; os textos têm o fade próprio na aba Texto."
            : spec.IntroSeconds is > 0
                ? "A capa dura esse tempo sem música; a música e as fotos começam logo depois. Os textos têm o fade próprio na aba Texto."
                : "0 = a música toca desde o início. Os textos têm o fade próprio na aba Texto.";
    }
}