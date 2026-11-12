using SkiaSharp;
using Slyden.Core.Audio;
using Slyden.Core.Config;

namespace Slyden.Core.Render;

/// <summary>
/// O "roteiro" do vídeo: que fotos entram, em que quadro cada uma começa, onde cai cada fade.
/// É o mesmo para exportar o vídeo e para a prévia animada, então os dois ficam iguais.
/// </summary>
public sealed class SlideshowPlan
{
    private SlideshowPlan() { }

    /// <summary>Fotos que cabem na música (ou todas, sem música), já com padrão e exceções aplicados.</summary>
    public IReadOnlyList<ResolvedSlide> Slides { get; private init; } = [];
    internal List<List<ActiveText>> Overlays { get; private init; } = [];
    /// <summary>Instantes (s) em que cada slide começa, mais o fim do último; na música, é a posição no áudio.</summary>
    public IReadOnlyList<double> Boundaries { get; private init; } = [];
    /// <summary>Quadro em que cada slide começa, mais o quadro final.</summary>
    public int[] Edges { get; private init; } = [];
    public IReadOnlyList<FrameInfo> Frames { get; private init; } = [];
    public int Fps { get; private init; }
    public int FadeFrames { get; private init; }
    /// <summary>Duração de uma batida em segundos (referência para o fade dos textos).</summary>
    public double Beat { get; private init; }
    public double Tempo { get; private init; }
    public int BeatsPerPhoto { get; private init; }

    public double TotalSeconds => (double)Edges[^1] / Fps;
    /// <summary>Posição da música em que o vídeo começa.</summary>
    /// <summary>Posição da música em que ela começa a tocar (com capa antes da música, é onde começa depois da capa).</summary>
    public double AudioStart => Boundaries[IntroSeconds > 0 ? 1 : 0];
    /// <summary>Segundos de capa sem música no começo do vídeo (0 = a música toca desde o início).</summary>
    public double IntroSeconds { get; private init; }
    public int FrameCount => Edges[^1];

    /// <summary>Quantas batidas dura um slide que pediu <paramref name="seconds"/>: arredonda para a batida mais próxima (mínimo 1).</summary>
    public static int BeatsFor(double seconds, double beatSeconds) => Math.Max(1, (int)Math.Round(seconds / beatSeconds));

    /// <param name="analysis">Batidas da música; sem ela vale <see cref="SlideshowConfig.SecondsPerPhoto"/> ou o BPM.</param>
    public static SlideshowPlan Build(SlideshowConfig cfg, int fps, BeatAnalysis? analysis)
    {
        cfg.Validate();
        var all = SlideResolver.Resolve(cfg);
        if (all.Count == 0) throw new InvalidOperationException("Nenhuma foto para o slideshow.");

        // capa antes da música: só com música e com mais de uma foto; a capa não entra na grade de batidas
        double intro = 0;
        if (analysis is not null && all.Count > 1 && cfg.Photos[0].IntroSeconds is { } introSeconds and > 0) intro = introSeconds;
        var rest = intro > 0 ? all.Skip(1).ToList() : all.ToList();

        // quando cada slide começa (segundos), sempre numa batida; "length" faz um slide durar vários ciclos
        int units = rest.Sum(s => s.Length);
        double[] cuts;
        IReadOnlyList<int> lengths = rest.Select(s => s.Length).ToList();
        double beat, tempo;
        double? fixedFadeSeconds = null;
        int perPhoto;
        if (analysis is not null)
        {
            beat = BeatSecondsOf(analysis, cfg.Bpm);
            tempo = analysis.Tempo;
            CutPlan plan;
            if (rest.Any(s => s.Seconds is not null))
            {
                // slides com duração própria: cada slide ocupa um intervalo de batidas (um intervalo por slide)
                var fixedBeats = rest.Select(s => s.Seconds is { } sec ? BeatsFor(sec, beat) : (int?)null).ToList();
                plan = CutPlanner.PlanMixed(analysis, fixedBeats, lengths,
                                            cfg.MusicMode == MusicMode.FitMusic && cfg.BeatsPerPhoto is null, cfg.BeatsPerPhoto);
                lengths = rest.Select(_ => 1).ToList();
            }
            else
            {
                // "ajustar à música" só vale no ritmo automático; com N batidas por foto escolhido, o usuário fixou o ritmo
                plan = (cfg.MusicMode == MusicMode.FitMusic && cfg.BeatsPerPhoto is null
                            ? CutPlanner.PlanToFit(analysis, units) : null)
                       ?? CutPlanner.Plan(analysis, units, cfg.BeatsPerPhoto);
            }
            cuts = plan.CutTimes;
            perPhoto = plan.BeatsPerPhoto;
        }
        else
        {
            double unit;
            if (cfg.SecondsPerPhoto is { } seconds)
            {
                // tempo fixo (telão): "batida" = 1 s, então fade_beats dos textos também conta em segundos
                perPhoto = 1;
                tempo = 60;
                beat = 1;
                unit = seconds;
                fixedFadeSeconds = Math.Min(cfg.FadeSeconds ?? 1.0, seconds / 2);
            }
            else
            {
                perPhoto = cfg.BeatsPerPhoto ?? 4;
                tempo = cfg.Bpm;
                beat = 60.0 / cfg.Bpm;
                unit = perPhoto * beat;
            }
            // sem música a duração própria do slide vale exatamente
            var starts = new double[rest.Count + 1];
            for (int i = 0; i < rest.Count; i++) starts[i + 1] = starts[i] + (rest[i].Seconds ?? rest[i].Length * unit);
            cuts = starts;
            lengths = rest.Select(_ => 1).ToList();
        }

        var boundaries = BoundariesOf(cuts, lengths).ToList();
        int count = boundaries.Count - 1;
        if (count < 1) throw new InvalidOperationException("A música é curta demais ou não tem batidas detectáveis.");
        var slides = rest.Take(count).ToList();
        if (intro > 0)
        {
            // a capa ocupa o começo; o "instante" dela na música é virtual (antes de a música começar)
            boundaries.Insert(0, boundaries[0] - intro);
            slides.Insert(0, all[0]);
        }

        var edges = boundaries.Select(c => (int)Math.Round((c - boundaries[0]) * fps)).ToArray();
        int fadeFrames = (int)Math.Round((fixedFadeSeconds ?? cfg.Default.FadeBeats * beat) * fps);
        var frames = Timeline.Frames(edges, slides.Select(s => s.Transition).ToList(), fadeFrames).ToList();

        return new SlideshowPlan
        {
            Slides = slides, Overlays = OverlayPlan.Build(slides), Boundaries = boundaries, Edges = edges,
            Frames = frames, Fps = fps, FadeFrames = fadeFrames, Beat = beat, Tempo = tempo, BeatsPerPhoto = perPhoto,
            IntroSeconds = intro,
        };
    }
    /// <summary>
    /// Instantes em que cada slide começa (e o fim do último). Um slide com length 2 ocupa dois intervalos da grade de batidas.
    /// Para no primeiro slide que não couber na música.
    /// </summary>
    internal static IReadOnlyList<double> BoundariesOf(IReadOnlyList<double> cuts, IReadOnlyList<int> lengths)
    {
        if (cuts.Count == 0) return [];
        var result = new List<double> { cuts[0] };
        int used = 0;
        foreach (int length in lengths)
        {
            used += length;
            if (used >= cuts.Count) break;
            result.Add(cuts[used]);
        }
        return result;
    }

    /// <summary>Desenha um quadro (slide atual e, se houver fade, o próximo por cima). <paramref name="get"/> entrega o desenhista de cada slide.</summary>
    internal void Draw(SKCanvas canvas, FrameInfo f, Func<int, SlideRenderer> get)
    {
        canvas.Clear(SKColors.Black);
        double imageAlpha = 1;
        if (Slides[f.Slide].ImageFadeIn > 0)
            imageAlpha = Math.Clamp(f.Frame / (Slides[f.Slide].ImageFadeIn * Fps), 0, 1);
        get(f.Slide).Draw(canvas, f.T, new TextTiming(f.Frame, f.Frames, Beat, Fps, Edges, f.Slide), imageAlpha);
        if (f.FadeAlpha > 0)
        {
            int next = f.Slide + 1;
            using var layer = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Round(f.FadeAlpha * 255)) };
            canvas.SaveLayer(layer);
            get(next).Draw(canvas, 0, new TextTiming(0, Edges[next + 1] - Edges[next], Beat, Fps, Edges, next));
            canvas.Restore();
        }
    }

    /// <summary>Duração típica de uma batida da música (mediana dos intervalos).</summary>
    public static double BeatSecondsOf(BeatAnalysis analysis, double fallbackBpm) =>
        Median(analysis.BeatTimes.Zip(analysis.BeatTimes.Skip(1), (a, b) => b - a).ToArray(), 60.0 / fallbackBpm);

    private static double Median(double[] values, double fallback)
    {
        if (values.Length == 0) return fallback;
        var s = values.Order().ToArray();
        return s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2;
    }
}
