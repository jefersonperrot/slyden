using SkiaSharp;
using Slyden.Core.Audio;
using Slyden.Core.Config;

namespace Slyden.Core.Render;

/// <param name="PhotosFolder">Pasta onde estão os arquivos citados em <see cref="SlideshowConfig.Photos"/>.</param>
/// <param name="AudioPath">Música; sem ela, usa o <see cref="SlideshowConfig.Bpm"/> e o vídeo sai mudo.</param>
/// <param name="Analysis">Batidas do áudio, se já foram calculadas (ex.: da trilha montada por <see cref="MusicBed"/>).</param>
/// <param name="FontFolders">Pastas com fontes (.ttf/.otf) além das instaladas no sistema; a pasta "fonts" ao lado do programa é sempre usada.</param>
public sealed record RenderRequest(
    string PhotosFolder, string OutputPath, SlideshowConfig Config, string? AudioPath = null, int Fps = 30,
    IReadOnlyList<string>? FontFolders = null, VideoEncoding? Encoding = null, BeatAnalysis? Analysis = null);

/// <summary>Como o vídeo é comprimido. O padrão é o de sempre (H.264, CRF 20, rápido).</summary>
/// <param name="Codec">libx264 (H.264, toca em tudo) ou libx265 (H.265/HEVC, arquivo menor).</param>
/// <param name="Crf">Qualidade constante: menor = melhor e maior; 0 = sem perdas (só H.264).</param>
/// <param name="Preset">Esforço do codificador: de ultrafast a veryslow; mais lento = arquivo menor na mesma qualidade.</param>
public sealed record VideoEncoding(string Codec = "libx264", int Crf = 20, string Preset = "veryfast")
{
    public static VideoEncoding Default { get; } = new();

    /// <summary>
    /// Perfis medidos com VMAF contra um mestre sem perdas (projeto de casamento, 1080p, 60 s): os três ficam em
    /// VMAF ~95 (diferença muito difícil de notar) e mudam só no tamanho do arquivo e no tempo de exportação.
    /// Rápida 23,6 MB/16 s · Equilibrada 15,4 MB/31 s · Compacta 9,0 MB/43 s (só a codificação).
    /// </summary>
    public static VideoEncoding For(ExportQuality quality) => quality switch
    {
        ExportQuality.Fast => new("libx264", 18, "veryfast"),
        ExportQuality.Compact => new("libx265", 22, "fast"),
        _ => new("libx264", 22, "medium"),
    };
}

/// <summary>Quanto esforço o programa dedica a reduzir o arquivo (a qualidade visível é a mesma nos três).</summary>
public enum ExportQuality { Fast, Balanced, Compact }

public sealed record RenderProgress(int Slide, int Slides, double Fraction);

/// <param name="Warnings">Coisas que não impedem o vídeo mas o usuário deve saber (ex.: fonte não encontrada).</param>
public sealed record RenderSummary(
    double DurationSeconds, int Slides, double Tempo, int BeatsPerPhoto, IReadOnlyList<string> Warnings);

public static class SlideshowRenderer
{

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

        var analysis = request.AudioPath is { } audio ? request.Analysis ?? BeatAnalyzer.Analyze(AudioDecoder.DecodeMono(audio)) : null;
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
                       EncoderArguments(width, height, fps, request.AudioPath, plan.AudioStart, total, partial,
                                          cfg.AudioFadeIn, cfg.AudioFadeOut, request.Encoding ?? VideoEncoding.Default, plan.IntroSeconds)))
            {
                // Vários quadros são desenhados ao mesmo tempo (um por núcleo) e um único fio os entrega ao
                // ffmpeg na ordem certa. Os quadros em andamento ocupam "espaços" (bitmaps) reutilizados.
                int workers = Math.Clamp(Environment.ProcessorCount - 1, 1, 8);
                int slotCount = workers + 2;
                var frames = new SKBitmap[slotCount];
                var canvases = new SKCanvas[slotCount];
                for (int i = 0; i < slotCount; i++)
                {
                    frames[i] = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
                    canvases[i] = new SKCanvas(frames[i]);
                }
                var free = new System.Collections.Concurrent.BlockingCollection<int>();
                for (int i = 0; i < slotCount; i++) free.Add(i);
                var pending = new System.Collections.Concurrent.BlockingCollection<(Task<int> Drawn, int Slide)>();
                var all = new List<Task<int>>();
                Exception? writerError = null;
                int written = 0;
                int totalFrames = edges[^1];
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);

                var writer = Task.Run(() =>
                {
                    try
                    {
                        foreach (var (drawn, slideIndex) in pending.GetConsumingEnumerable())
                        {
                            int slot = drawn.GetAwaiter().GetResult();
                            encoder.WriteFrame(frames[slot].GetPixelSpan());
                            int n = Interlocked.Increment(ref written);
                            free.Add(slot);
                            if (n % 15 == 0 || n == totalFrames)
                                progress?.Report(new RenderProgress(slideIndex + 1, slides.Count, (double)n / totalFrames));
                        }
                    }
                    catch (Exception ex)
                    {
                        writerError = ex;
                        stop.Cancel(); // acorda o laço principal, que espera um espaço livre
                    }
                });

                var gate = new object();
                var loaded = new Dictionary<int, SlideRenderer>();
                SlideRenderer Get(int i)
                {
                    lock (gate)
                    {
                        if (!loaded.TryGetValue(i, out var r))
                            loaded[i] = r = SlideRenderer.Create(
                                ImageLoader.Load(Path.Combine(request.PhotosFolder, slides[i].File)),
                                slides[i], width, height, plan.Overlays[i], painter);
                        return r;
                    }
                }

                try
                {
                    int lastSlide = -1;
                    foreach (var f in plan.Frames)
                    {
                        int slot;
                        try { slot = free.Take(stop.Token); }
                        catch (OperationCanceledException) when (writerError is not null) { break; }
                        if (f.Slide != lastSlide)
                        {
                            lastSlide = f.Slide;
                            // libera as fotos que nenhum quadro em andamento ou futuro usa mais, e já carrega a próxima
                            lock (gate)
                            {
                                int done = Volatile.Read(ref written);
                                foreach (var k in loaded.Keys.Where(k => edges[k + 1] <= done).ToList())
                                {
                                    loaded[k].Dispose();
                                    loaded.Remove(k);
                                }
                            }
                            if (f.Slide + 1 < slides.Count) Get(f.Slide + 1);
                        }

                        var frame = f;
                        var task = Task.Run(() =>
                        {
                            plan.Draw(canvases[slot], frame, Get);
                            return slot;
                        });
                        all.Add(task);
                        pending.Add((task, f.Slide));
                    }
                }
                finally
                {
                    pending.CompleteAdding();
                    if (cancellation.IsCancellationRequested || writerError is not null)
                        encoder.Dispose(); // para o ffmpeg e solta o envio que estiver travado
                    writer.Wait();
                    try { Task.WaitAll(all.ToArray()); } catch (AggregateException) { }
                    foreach (var r in loaded.Values) r.Dispose();
                    foreach (var c in canvases) c.Dispose();
                    foreach (var b in frames) b.Dispose();
                }
                cancellation.ThrowIfCancellationRequested();
                if (writerError is not null) throw new InvalidOperationException(writerError.Message, writerError);
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
                                                 double total, string output,
                                                 double fadeInSeconds, double fadeOutSeconds, VideoEncoding encoding,
                                                 double audioDelay = 0)
    {
        var args = new List<string>
        {
            "-y", "-loglevel", "error",
            "-f", "rawvideo", "-pix_fmt", "bgra", "-s", $"{width}x{height}", "-r", fps.ToString(), "-i", "-",
        };
        if (audio is not null)
        {
            var filters = new List<string>();
            // com capa antes da música, o áudio é atrasado: os fades valem para o trecho de música (tempo do áudio)
            double musicTotal = Math.Max(0.1, total - audioDelay);
            double fadeIn = Math.Min(fadeInSeconds, musicTotal), fadeOut = Math.Min(fadeOutSeconds, musicTotal);
            if (fadeIn > 0) filters.Add($"afade=t=in:st=0:d={Inv(fadeIn)}");
            if (fadeOut > 0) filters.Add($"afade=t=out:st={Inv(musicTotal - fadeOut)}:d={Inv(fadeOut)}");
            if (audioDelay > 0) filters.Add($"adelay={(int)Math.Round(audioDelay * 1000)}:all=1");
            args.AddRange(["-ss", Inv(audioStart), "-t", Inv(musicTotal), "-i", audio]);
            if (filters.Count > 0) args.AddRange(["-af", string.Join(',', filters)]);
            args.AddRange(["-c:a", "aac", "-b:a", "192k", "-shortest"]);
        }
        else
        {
            args.Add("-an");
        }
        args.AddRange(["-c:v", encoding.Codec, "-crf", encoding.Crf.ToString(), "-preset", encoding.Preset, "-pix_fmt", "yuv420p"]);
        if (encoding.Codec == "libx265") args.AddRange(["-tag:v", "hvc1"]); // o QuickTime/iPhone/Mac só reconhecem HEVC com esta etiqueta
        args.Add(output);
        return args;
    }

    private static string Inv(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
