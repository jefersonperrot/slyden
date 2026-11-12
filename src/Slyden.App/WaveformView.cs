using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Slyden.Core.Audio;

namespace Slyden.App;

/// <summary>Onda de uma música com duas alças (início e fim do corte) que se arrastam e "grudam" nas batidas.</summary>
public sealed class WaveformView : Control
{
    public const double ViewHeight = 60;
    private const double GrabDistance = 12;

    private float[] _peaks = [];
    private double[] _beats = [];
    private double _duration, _start, _end;
    private int _drag; // 0 = nada, 1 = início, 2 = fim

    private double _playhead = -1;

    /// <summary>Posição de escuta em segundos do arquivo; negativo = sem marcador.</summary>
    public double Playhead
    {
        get => _playhead;
        set { _playhead = value; InvalidateVisual(); }
    }

    public Color Accent { get; set; } = Color.Parse("#3B8BFF");
    public double Start => _start;
    public double End => _end;
    public double Duration => _duration;

    /// <summary>Durante o arrastar (início, fim), em segundos do arquivo.</summary>
    public event Action<double, double>? Dragging;
    /// <summary>Quando o usuário solta a alça.</summary>
    public event Action<double, double>? Committed;

    public WaveformView()
    {
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
    }

    public void SetAudio(float[] peaks, double[] beats, double duration)
    {
        (_peaks, _beats, _duration) = (peaks, beats, duration);
        if (_end <= 0 || _end > duration) _end = duration;
        InvalidateVisual();
    }

    /// <summary>Define o trecho usado; <paramref name="end"/> 0 ou além do arquivo = até o fim.</summary>
    public void SetRange(double start, double end)
    {
        if (_drag != 0) return; // não briga com o arrastar em andamento
        _start = Math.Max(0, start);
        _end = end <= 0 || (_duration > 0 && end > _duration) ? _duration : end;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 240 : availableSize.Width, ViewHeight);

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#1B1B1F")), null, new Rect(0, 0, w, h), 4, 4);
        if (_duration <= 0 || _peaks.Length == 0 || w < 2)
        {
            return;
        }

        var bar = new SolidColorBrush(Accent);
        int columns = (int)w;
        for (int x = 0; x < columns; x++)
        {
            int i0 = (int)(x / (double)columns * _duration * Waveform.PerSecond);
            int i1 = Math.Max(i0 + 1, (int)((x + 1) / (double)columns * _duration * Waveform.PerSecond));
            float peak = 0;
            for (int i = i0; i < Math.Min(i1, _peaks.Length); i++) peak = Math.Max(peak, _peaks[i]);
            double bh = Math.Max(1, peak * (h - 8));
            context.DrawRectangle(bar, null, new Rect(x, (h - bh) / 2, 1, bh));
        }

        double xs = XOf(_start), xe = XOf(_end);
        var shade = new SolidColorBrush(Color.Parse("#B0141417")); // o que fica fora do corte
        context.DrawRectangle(shade, null, new Rect(0, 0, xs, h));
        context.DrawRectangle(shade, null, new Rect(xe, 0, Math.Max(0, w - xe), h));

        if (_playhead >= 0)
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#FFD24A")), null, new Rect(Math.Clamp(XOf(_playhead) - 0.75, 0, w - 1.5), 0, 1.5, h));

        var handle = new SolidColorBrush(Colors.White);
        foreach (double x in new[] { xs, xe })
        {
            context.DrawRectangle(handle, null, new Rect(Math.Clamp(x - 1, 0, w - 2), 0, 2, h));
            context.DrawRectangle(handle, null, new Rect(Math.Clamp(x - 4, 0, w - 8), 0, 8, 10), 2, 2);
        }
    }

    private double XOf(double seconds) => _duration <= 0 ? 0 : Math.Clamp(seconds / _duration, 0, 1) * Bounds.Width;

    private double SecondsAt(double x) => Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1) * _duration;

    private double SnapToBeat(double seconds)
    {
        if (_beats.Length == 0) return seconds;
        double best = seconds, distance = double.MaxValue;
        foreach (double b in _beats)
        {
            double d = Math.Abs(b - seconds);
            if (d < distance) (best, distance) = (b, d);
        }
        return best;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_duration <= 0) return;
        double x = e.GetPosition(this).X;
        double toStart = Math.Abs(x - XOf(_start)), toEnd = Math.Abs(x - XOf(_end));
        if (Math.Min(toStart, toEnd) > GrabDistance) return;
        _drag = toStart <= toEnd ? 1 : 2;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag == 0) return;
        double t = SnapToBeat(SecondsAt(e.GetPosition(this).X));
        if (_drag == 1) _start = Math.Clamp(t, 0, Math.Max(0, _end - MusicBed.MinSegment));
        else _end = Math.Clamp(t, Math.Min(_duration, _start + MusicBed.MinSegment), _duration);
        InvalidateVisual();
        Dragging?.Invoke(_start, _end);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == 0) return;
        _drag = 0;
        e.Pointer.Capture(null);
        Committed?.Invoke(_start, _end);
    }
}
