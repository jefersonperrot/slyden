using SkiaSharp;
using Slyden.Core.Audio;
using Slyden.Core.Config;

namespace Slyden.Core.Render;

/// <summary>
/// Prévia animada: desenha qualquer quadro do vídeo em tamanho reduzido, com o mesmo código da exportação.
/// Use de uma thread por vez (cada chamada de <see cref="RenderFrame"/> reaproveita o mesmo bitmap).
/// </summary>
public sealed class SlideshowPreview : IDisposable
{
    private readonly string _folder;
    private readonly FontResolver _fonts;
    private readonly TextPainter _painter;
    private readonly Func<string, SKBitmap> _loadPhoto;
    private readonly Dictionary<int, Lazy<SlideRenderer>> _loaded = [];
    private readonly object _lock = new();
    private readonly SKBitmap _frame;
    private readonly SKCanvas _canvas;

    public SlideshowPlan Plan { get; }
    public int Width { get; }
    public int Height { get; }

    private SlideshowPreview(SlideshowPlan plan, string folder, int width, int height, FontResolver fonts, Func<string, SKBitmap> loadPhoto)
    {
        Plan = plan;
        _folder = folder;
        Width = width;
        Height = height;
        _fonts = fonts;
        _painter = new TextPainter(fonts);
        _loadPhoto = loadPhoto;
        _frame = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        _canvas = new SKCanvas(_frame);
    }

    /// <param name="scale">Fração do tamanho do vídeo (0,35 é leve para tocar em tempo real).</param>
    /// <param name="loadPhoto">Opcional: entrega a foto (ex.: reduzida e em cache). O bitmap devolvido passa a pertencer à prévia.</param>
    public static SlideshowPreview Create(SlideshowConfig config, string photosFolder, BeatAnalysis? analysis, double scale,
                                          int fps = 30, IReadOnlyList<string>? fontFolders = null,
                                          Func<string, SKBitmap>? loadPhoto = null)
    {
        var plan = SlideshowPlan.Build(config, fps, analysis);
        var (fullW, fullH) = VideoFormat.SizeOf(config.Format, config.Resolution);
        int width = Math.Max(2, (int)Math.Round(fullW * scale)), height = Math.Max(2, (int)Math.Round(fullH * scale));

        var folders = new List<string> { Path.Combine(AppContext.BaseDirectory, "fonts") };
        folders.AddRange(fontFolders ?? []);
        return new SlideshowPreview(plan, photosFolder, width, height, new FontResolver(folders), loadPhoto ?? ImageLoader.Load);
    }

    /// <summary>Desenha o quadro <paramref name="index"/> (0 a FrameCount - 1). O bitmap devolvido só vale até a próxima chamada.</summary>
    public SKBitmap RenderFrame(int index)
    {
        var f = Plan.Frames[Math.Clamp(index, 0, Plan.Frames.Count - 1)];

        lock (_lock)
        {
            foreach (int far in _loaded.Keys.Where(k => k < f.Slide - 1 || k > f.Slide + 2).ToList())
            {
                if (_loaded[far].IsValueCreated) _loaded[far].Value.Dispose();
                _loaded.Remove(far);
            }
        }

        Plan.Draw(_canvas, f, i => Slot(i).Value);
        Prefetch(f.Slide + 1); // a foto seguinte é carregada em segundo plano, para a troca não travar
        return _frame;
    }

    /// <summary>Deixa prontos o slide <paramref name="slide"/> e o seguinte (chame antes de começar a tocar).</summary>
    public void Warm(int slide)
    {
        _ = Slot(slide).Value;
        if (slide + 1 < Plan.Slides.Count) _ = Slot(slide + 1).Value;
    }

    private void Prefetch(int slide)
    {
        if (slide < 0 || slide >= Plan.Slides.Count) return;
        var slot = Slot(slide);
        if (!slot.IsValueCreated) Task.Run(() => { try { _ = slot.Value; } catch { /* o erro aparece quando o quadro for desenhado */ } });
    }

    private Lazy<SlideRenderer> Slot(int i)
    {
        lock (_lock)
        {
            if (!_loaded.TryGetValue(i, out var slot))
            {
                var slide = Plan.Slides[i];
                _loaded[i] = slot = new Lazy<SlideRenderer>(() => SlideRenderer.Create(
                    _loadPhoto(Path.Combine(_folder, slide.File)), slide, Width, Height, Plan.Overlays[i], _painter));
            }
            return slot;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var slot in _loaded.Values)
                if (slot.IsValueCreated) slot.Value.Dispose();
            _loaded.Clear();
        }
        _canvas.Dispose();
        _frame.Dispose();
        _fonts.Dispose();
    }
}
