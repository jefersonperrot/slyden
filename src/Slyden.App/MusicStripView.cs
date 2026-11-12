using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Slyden.Core.Audio;

namespace Slyden.App;

/// <summary>
/// Faixa da trilha de música (a trilha inteira numa largura só): onda, batidas e a divisão entre as músicas.
/// Clicar numa música avisa quem usa a faixa (a tela vai para a lista de músicas).
/// </summary>
public sealed class MusicStripView : Control
{
    private sealed record Block(string Name, double Start, double End, Color Color);

    private readonly List<Block> _blocks = [];
    private float[] _peaks = [];
    private double[] _beats = [];
    private double _duration;
    private int _hover = -1;

    /// <summary>Índice da música clicada (0 = primeira).</summary>
    public event Action<int>? TrackClicked;

    public MusicStripView()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public void Set(MusicBed bed, Func<string, float[]?> peaksOf, IReadOnlyList<Color> colors)
    {
        _blocks.Clear();
        _duration = bed.Duration;
        _beats = bed.Analysis.BeatTimes;
        var segments = bed.Segments;
        for (int i = 0; i < segments.Count; i++)
        {
            double end = i + 1 < segments.Count ? segments[i + 1].Offset : bed.Duration;
            _blocks.Add(new Block(Path.GetFileNameWithoutExtension(segments[i].Track.Path), segments[i].Offset, end, colors[i % colors.Count]));
        }

        // onda da trilha: o volume de cada música, deslocado para o lugar onde ela toca
        _peaks = new float[Math.Max(1, (int)Math.Ceiling(_duration * Waveform.PerSecond))];
        foreach (var s in segments)
        {
            if (peaksOf(s.Track.Path) is not { } peaks) continue;
            int from = (int)(s.SourceStart * Waveform.PerSecond), to = Math.Min(peaks.Length, (int)(s.SourceEnd * Waveform.PerSecond));
            for (int i = from; i < to; i++)
            {
                int at = (int)((s.Offset + (i - from) / (double)Waveform.PerSecond) * Waveform.PerSecond);
                if (at >= 0 && at < _peaks.Length) _peaks[at] = Math.Max(_peaks[at], peaks[i]);
            }
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width, 48);

    private double XOf(double seconds) => _duration <= 0 ? 0 : seconds / _duration * Bounds.Width;

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#17171A")), null, new Rect(0, 0, w, h), 4, 4);
        if (_duration <= 0 || _blocks.Count == 0 || w < 2) return;

        // fundo de cada música, mais forte na que está sob o mouse
        for (int i = 0; i < _blocks.Count; i++)
        {
            var b = _blocks[i];
            var fill = new SolidColorBrush(b.Color, i == _hover ? 0.38 : 0.22);
            context.DrawRectangle(fill, null, new Rect(XOf(b.Start), 0, Math.Max(1, XOf(b.End) - XOf(b.Start)), h));
        }

        // onda
        int columns = (int)w;
        for (int x = 0; x < columns; x++)
        {
            double t = (x + 0.5) / columns * _duration;
            int i0 = (int)(x / (double)columns * _duration * Waveform.PerSecond);
            int i1 = Math.Max(i0 + 1, (int)((x + 1) / (double)columns * _duration * Waveform.PerSecond));
            float peak = 0;
            for (int i = i0; i < Math.Min(i1, _peaks.Length); i++) peak = Math.Max(peak, _peaks[i]);
            var color = _blocks.FirstOrDefault(b => t >= b.Start && t < b.End)?.Color ?? _blocks[^1].Color;
            double bh = Math.Max(1, peak * (h - 18));
            context.DrawRectangle(new SolidColorBrush(color, 0.9), null, new Rect(x, 14 + (h - 14 - bh) / 2, 1, bh));
        }

        // batidas: marcas finas embaixo (se estiverem muito juntas, mostra só algumas)
        if (_beats.Length > 1)
        {
            double spacing = w * (_beats[^1] - _beats[0]) / _duration / (_beats.Length - 1);
            int step = Math.Max(1, (int)Math.Ceiling(3.0 / Math.Max(spacing, 0.01)));
            var tick = new SolidColorBrush(Colors.White, 0.55);
            for (int i = 0; i < _beats.Length; i += step)
                context.DrawRectangle(tick, null, new Rect(Math.Clamp(XOf(_beats[i]), 0, w - 1), h - 7, 1, 7));
        }

        // divisão entre as músicas e o nome de cada uma
        var white = new SolidColorBrush(Colors.White);
        for (int i = 0; i < _blocks.Count; i++)
        {
            var b = _blocks[i];
            double x0 = XOf(b.Start), x1 = XOf(b.End);
            if (i > 0) context.DrawRectangle(white, null, new Rect(Math.Clamp(x0 - 1, 0, w - 2), 0, 2, h));
            double room = x1 - x0 - 8;
            if (room < 14) continue;
            var text = new FormattedText($"{i + 1} · {b.Name}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                         Typeface.Default, 11, white)
            { MaxTextWidth = room, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            context.DrawText(text, new Point(x0 + 5, 1));
        }
    }

    private int BlockAt(double x)
    {
        if (_duration <= 0) return -1;
        double t = x / Math.Max(1, Bounds.Width) * _duration;
        for (int i = 0; i < _blocks.Count; i++)
            if (t >= _blocks[i].Start && t < _blocks[i].End) return i;
        return _blocks.Count - 1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int hover = BlockAt(e.GetPosition(this).X);
        if (hover == _hover) return;
        _hover = hover;
        if (hover >= 0)
            ToolTip.SetTip(this, $"{hover + 1}. {_blocks[hover].Name} · clique para editar esta música");
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        int i = BlockAt(e.GetPosition(this).X);
        if (i >= 0) TrackClicked?.Invoke(i);
    }
}
