using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Slyden.App;

/// <summary>
/// Com música, a linha do tempo das fotos usa a mesma escala da faixa da trilha: cada foto tem a largura do tempo
/// que ocupa e fica exatamente sob a sua parte da música. O zoom escolhe quantos pixels vale um segundo.
/// </summary>
public partial class MainWindow
{
    private const double PhotoCardWidth = 150;  // largura de uma foto fora da escala do tempo (sem música)
    private const double CardChrome = 8;        // borda (2+2) + espaço (4) de cada cartão: a foto + isto = a largura do tempo
    private double _pps;                        // pixels por segundo escolhidos no zoom; 0 = automático
    private double[] _layoutBounds = [];        // instante em que cada foto começa, mais o fim da última
    private bool _scrollHooked, _syncingZoom;

    private void InitTimelineSync()
    {
        TimelineZoom.ValueChanged += (_, _) =>
        {
            if (_syncingZoom) return;
            _pps = TimelineZoom.Value;
            ApplyTimelineLayout();
        };
        ZoomFitButton.Click += (_, _) =>
        {
            if (_layoutBounds.Length < 2) return;
            double span = _layoutBounds[^1] - Math.Min(0, _layoutBounds[0]);
            _pps = Math.Clamp((SlideList.Bounds.Width - 24) / Math.Max(1, span), TimelineZoom.Minimum, TimelineZoom.Maximum);
            ApplyTimelineLayout();
        };
    }

    /// <summary>Zoom automático: a foto mais curta fica com ~56 px, para dar para ver e clicar.</summary>
    private double AutoPixelsPerSecond()
    {
        var shortest = Enumerable.Range(0, _layoutBounds.Length - 1).Select(i => _layoutBounds[i + 1] - _layoutBounds[i]).Where(d => d > 0.2).DefaultIfEmpty(2).Min();
        return Math.Clamp((56 + CardChrome) / shortest, 8, 120);
    }

    private void ApplyTimelineLayout()
    {
        var b = _layoutBounds;
        if (b.Length < 2 || _cfg.Tracks.Count == 0)
        {
            foreach (var item in _items) item.CardWidth = PhotoCardWidth;
            if (SlideList.ItemsPanelRoot is { } plain) plain.Margin = default;
            MusicStrip.PixelsPerSecond = 0;
            ZoomBox.IsVisible = false;
            return;
        }

        double origin = Math.Min(0, b[0]);
        double pps = _pps > 0 ? _pps : AutoPixelsPerSecond();
        int count = Math.Min(b.Length - 1, _items.Count);
        for (int i = 0; i < count; i++) _items[i].CardWidth = Math.Max(6, (b[i + 1] - b[i]) * pps - CardChrome);
        for (int i = count; i < _items.Count; i++) _items[i].CardWidth = PhotoCardWidth; // fotos que não couberam na música

        EnsureScrollHook();
        if (SlideList.ItemsPanelRoot is { } panel) panel.Margin = new Thickness((b[0] - origin) * pps, 0, 0, 0);
        MusicStrip.OriginSeconds = origin;
        MusicStrip.PixelsPerSecond = pps;

        _syncingZoom = true;
        TimelineZoom.Value = Math.Clamp(pps, TimelineZoom.Minimum, TimelineZoom.Maximum);
        _syncingZoom = false;
        ZoomBox.IsVisible = true;
    }

    /// <summary>A faixa da trilha acompanha a rolagem da linha do tempo das fotos.</summary>
    private void EnsureScrollHook()
    {
        if (_scrollHooked) return;
        var viewer = SlideList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (viewer is null) return;
        viewer.ScrollChanged += (_, _) => MusicStrip.ScrollX = viewer.Offset.X;
        _scrollHooked = true;
    }
}
