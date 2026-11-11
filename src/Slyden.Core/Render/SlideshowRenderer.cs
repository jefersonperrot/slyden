using SkiaSharp;
using Slyden.Core.Audio;
using Slyden.Core.Config;

namespace Slyden.Core.Render;

/// <param name="PhotosFolder">Pasta onde estão os arquivos citados em <see cref="SlideshowConfig.Photos"/>.</param>
/// <param name="AudioPath">Música; sem ela, usa o <see cref="SlideshowConfig.Bpm"/> e o vídeo sai mudo.</param>
/// <param name="FontFolders">Pastas com fontes (.ttf/.otf) além das instaladas no sistema; a pasta "fonts" ao lado do programa é sempre usada.</param>
public sealed record RenderRequest(
    string PhotosFolder, string OutputPath, SlideshowConfig Config, string? AudioPath = null, int Fps = 30,
    IReadOnlyList<string>? FontFolders = null);

public sealed record RenderProgress(int Slide, int Slides, double Fraction);

/// <param name="Warnings">Coisas que não impedem o vídeo mas o usuário deve saber (ex.: fonte não encontrada).</param>
public sealed record RenderSummary(
    double DurationSeconds, int Slides, double Tempo, int BeatsPerPhoto, IReadOnlyList<string> Warnings);

public static class SlideshowRenderer
{
    private const double AudioFadeOutSeconds = 1.5;

    public static RenderSummary Render(RenderRequest request, IProgress<RenderProgress>? progress = null,
                                       CancellationToken cancellation = default)
    {
        var cfg = request.Config;
        cfg.Validate();
        var (width, height) = VideoFormat.SizeOf(cfg.Format);
        int fps = request.Fps;

        var slides = SlideResolver.Resolve(cfg);
        if (slides.Count == 0) throw new InvalidOperationException("Nenhuma foto para o slideshow.");
        foreach (var s in slides)
            if (!File.Exists(Path.Combine(request.PhotosFolder, s.File)))
                throw new FileNotFoundException($"Foto não encontrada: {s.File}", Path.Combine(request.PhotosFolder, s.File));

        // fontes: confere já, antes do trabalho pesado, e junta os avisos
        var fontFolders = new List<string> { Path.Combine(AppContext.BaseDirectory, "fonts") };
        fontFolders.AddRange(request.FontFolders ?? []);
        using var fonts = new FontResolver(fontFolders);
        var warnings = new List<string>();
        foreach (var block in slides.SelectMany(s => s.Texts ?? []))
            fonts.Resolve(block.Font, block.Bold, block.Italic, warnings);
        warnings = warnings.Distinct().ToList();

        // quando cada slide começa (segundos), sempre numa batida; "length" faz um slide durar vários ciclos
        int units = slides.Sum(s => s.Length);
        double[] cuts;
        double beat, tempo;
        double? fixedFadeSeconds = null;
        int perPhoto;
        if (request.AudioPath is { } audio)
        {
            var analysis = BeatAnalyzer.Analyze(AudioDecoder.DecodeMono(audio));
            var plan = CutPlanner.Plan(analysis, units, cfg.BeatsPerPhoto);
            cuts = plan.CutTimes;
            perPhoto = plan.BeatsPerPhoto;
            tempo = analysis.Tempo;
            beat = Median(analysis.BeatTimes.Zip(analysis.BeatTimes.Skip(1), (a, b) => b - a).ToArray(), 60.0 / cfg.Bpm);
        }
        else if (cfg.SecondsPerPhoto is { } seconds)
        {
            // tempo fixo (telão): "batida" = 1 s, então fade_beats dos textos também conta em segundos
            perPhoto = 1;
            tempo = 60;
            beat = 1;
            cuts = Enumerable.Range(0, units + 1).Select(i => i * seconds).ToArray();
            fixedFadeSeconds = Math.Min(cfg.FadeSeconds ?? 1.0, seconds / 2);
        }
        else
        {
            perPhoto = cfg.BeatsPerPhoto ?? 4;
            tempo = cfg.Bpm;
            beat = 60.0 / cfg.Bpm;
            cuts = Enumerable.Range(0, units + 1).Select(i => i * perPhoto * beat).ToArray();
        }

        var boundaries = Boundaries(cuts, slides.Select(s => s.Length).ToList());
        int count = boundaries.Count - 1;
        if (count < 1) throw new InvalidOperationException("A música é curta demais ou não tem batidas detectáveis.");
        slides = slides.Take(count).ToList();
        var overlays = OverlayPlan.Build(slides);

        var edges = boundaries.Select(c => (int)Math.Round((c - boundaries[0]) * fps)).ToArray();
        double total = (double)edges[^1] / fps;
        int fadeFrames = (int)Math.Round((fixedFadeSeconds ?? cfg.Default.FadeBeats * beat) * fps);
        var transitions = slides.Select(s => s.Transition).ToList();
        var painter = new TextPainter(fonts);

        string partial = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(request.OutputPath))!,
                                      Path.GetFileNameWithoutExtension(request.OutputPath) + ".partial.mp4");
        try
        {
            using (var encoder = new FfmpegEncoder(FfmpegLocator.Find(),
                       EncoderArguments(width, height, fps, request.AudioPath, boundaries[0], total, partial)))
            using (var frame = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque)))
            using (var canvas = new SKCanvas(frame))
            {
                var loaded = new Dictionary<int, SlideRenderer>();
                SlideRenderer Get(int i)
                {
                    if (!loaded.TryGetValue(i, out var r))
                        loaded[i] = r = SlideRenderer.Create(
                            ImageLoader.Load(Path.Combine(request.PhotosFolder, slides[i].File)),
                            slides[i], width, height, overlays[i], painter);
                    return r;
                }

                try
                {
                    int done = 0, totalFrames = edges[^1], lastSlide = -1;
                    foreach (var f in Timeline.Frames(edges, transitions, fadeFrames))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (f.Slide != lastSlide)
                        {
                            foreach (var old in loaded.Keys.Where(k => k < f.Slide).ToList())
                            {
                                loaded[old].Dispose();
                                loaded.Remove(old);
                            }
                            lastSlide = f.Slide;
                        }

                        canvas.Clear(SKColors.Black);
                        Get(f.Slide).Draw(canvas, f.T, new TextTiming(f.Frame, f.Frames, beat, fps));
                        if (f.FadeAlpha > 0)
                        {
                            int next = f.Slide + 1;
                            using var layer = new SKPaint { Color = new SKColor(255, 255, 255, (byte)Math.Round(f.FadeAlpha * 255)) };
                            canvas.SaveLayer(layer);
                            Get(next).Draw(canvas, 0, new TextTiming(0, edges[next + 1] - edges[next], beat, fps));
                            canvas.Restore();
                        }

                        encoder.WriteFrame(frame.GetPixelSpan());
                        done++;
                        if (done % 15 == 0 || done == totalFrames)
                            progress?.Report(new RenderProgress(f.Slide + 1, count, (double)done / totalFrames));
                    }
                }
                finally
                {
                    foreach (var r in loaded.Values) r.Dispose();
                }
                encoder.Finish();
            }
            File.Move(partial, request.OutputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial); // cancelamento ou erro: não deixa vídeo pela metade
        }
        return new RenderSummary(total, count, tempo, perPhoto, warnings);
    }

    /// <summary>
    /// Instantes em que cada slide começa (e o fim do último). Um slide com length 2 ocupa dois intervalos da grade de batidas.
    /// Para no primeiro slide que não couber na música.
    /// </summary>
    internal static IReadOnlyList<double> Boundaries(IReadOnlyList<double> cuts, IReadOnlyList<int> lengths)
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

    private static List<string> EncoderArguments(int width, int height, int fps, string? audio, double audioStart,
                                                 double total, string output)
    {
        var args = new List<string>
        {
            "-y", "-loglevel", "error",
            "-f", "rawvideo", "-pix_fmt", "bgra", "-s", $"{width}x{height}", "-r", fps.ToString(), "-i", "-",
        };
        if (audio is not null)
        {
            double fade = Math.Min(AudioFadeOutSeconds, total);
            args.AddRange(["-ss", Inv(audioStart), "-t", Inv(total), "-i", audio,
                           "-af", $"afade=t=out:st={Inv(total - fade)}:d={Inv(fade)}",
                           "-c:a", "aac", "-b:a", "192k", "-shortest"]);
        }
        else
        {
            args.Add("-an");
        }
        args.AddRange(["-c:v", "libx264", "-crf", "20", "-preset", "medium", "-pix_fmt", "yuv420p", output]);
        return args;
    }

    private static string Inv(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static double Median(double[] values, double fallback)
    {
        if (values.Length == 0) return fallback;
        var s = values.Order().ToArray();
        return s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2;
    }
}
