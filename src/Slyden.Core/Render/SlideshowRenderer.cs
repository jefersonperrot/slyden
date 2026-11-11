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
        var (width, height) = VideoFormat.SizeOf(cfg.Format, cfg.Resolution);
        int fps = request.Fps;

        var requested = SlideResolver.Resolve(cfg);
        if (requested.Count == 0) throw new InvalidOperationException("Nenhuma foto para o slideshow.");
        foreach (var s in requested)
            if (!File.Exists(Path.Combine(request.PhotosFolder, s.File)))
                throw new FileNotFoundException($"Foto não encontrada: {s.File}", Path.Combine(request.PhotosFolder, s.File));

        // fontes: confere já, antes do trabalho pesado, e junta os avisos
        var fontFolders = new List<string> { Path.Combine(AppContext.BaseDirectory, "fonts") };
        fontFolders.AddRange(request.FontFolders ?? []);
        using var fonts = new FontResolver(fontFolders);
        var warnings = new List<string>();
        foreach (var block in requested.SelectMany(s => s.Texts ?? []))
            fonts.Resolve(block.Font, block.Bold, block.Italic, warnings);
        warnings = warnings.Distinct().ToList();

        var analysis = request.AudioPath is { } audio ? BeatAnalyzer.Analyze(AudioDecoder.DecodeMono(audio)) : null;
        var plan = SlideshowPlan.Build(cfg, fps, analysis);
        var slides = plan.Slides;
        var edges = plan.Edges;
        double total = plan.TotalSeconds;
        var painter = new TextPainter(fonts);

        string partial = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(request.OutputPath))!,
                                      Path.GetFileNameWithoutExtension(request.OutputPath) + ".partial.mp4");
        try
        {
            using (var encoder = new FfmpegEncoder(FfmpegLocator.Find(),
                       EncoderArguments(width, height, fps, request.AudioPath, plan.AudioStart, total, partial)))
            using (var frame = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque)))
            using (var canvas = new SKCanvas(frame))
            {
                var loaded = new Dictionary<int, SlideRenderer>();
                SlideRenderer Get(int i)
                {
                    if (!loaded.TryGetValue(i, out var r))
                        loaded[i] = r = SlideRenderer.Create(
                            ImageLoader.Load(Path.Combine(request.PhotosFolder, slides[i].File)),
                            slides[i], width, height, plan.Overlays[i], painter);
                    return r;
                }

                try
                {
                    int done = 0, totalFrames = edges[^1], lastSlide = -1;
                    foreach (var f in plan.Frames)
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

                        plan.Draw(canvas, f, Get);

                        encoder.WriteFrame(frame.GetPixelSpan());
                        done++;
                        if (done % 15 == 0 || done == totalFrames)
                            progress?.Report(new RenderProgress(f.Slide + 1, slides.Count, (double)done / totalFrames));
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
        return new RenderSummary(total, slides.Count, plan.Tempo, plan.BeatsPerPhoto, warnings);
    }

    internal static IReadOnlyList<double> Boundaries(IReadOnlyList<double> cuts, IReadOnlyList<int> lengths) =>
        SlideshowPlan.BoundariesOf(cuts, lengths);

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
}
