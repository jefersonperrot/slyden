using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Slyden.Core.Config;
using Slyden.Core.Render;
using SlideText = Slyden.Core.Config.TextBlock;

namespace Slyden.App;

/// <summary>Ferramentas de texto: lista de fontes do computador, arrastar o texto na prévia, centralizar e voltar ao padrão.</summary>
public partial class MainWindow
{
    private readonly ObservableCollection<string> _fontNames = [];
    private SlideText? _dragText;
    private double _dragOffsetX, _dragOffsetY;

    private void InitTextTools()
    {
        foreach (var name in FontResolver.ListFamilies()) _fontNames.Add(name);
        TextFont.ItemsSource = _fontNames;
        TextFont.SelectionChanged += (_, _) =>
        {
            if (TextFont.SelectedItem is string family) EditText(t => t.Font = family);
        };

        CenterTextXButton.Click += (_, _) => CenterText(vertical: false);
        CenterTextButton.Click += (_, _) => CenterText(vertical: true);

        PreviewHit.PointerPressed += OnPreviewPressed;
        PreviewHit.PointerMoved += OnPreviewMoved;
        PreviewHit.PointerReleased += (_, e) => EndTextDrag(e);
        PreviewHit.PointerCaptureLost += (_, _) => _dragText = null;

        // clique duplo no controle deslizante: volta ao valor padrão dele
        ResetOnDoubleClick(TextX, 50);
        ResetOnDoubleClick(TextY, 50);
        ResetOnDoubleClick(TextSize, 8);
        ResetOnDoubleClick(ScaleSlider, 100);
        ResetOnDoubleClick(IntensitySlider, 15);
        ResetOnDoubleClick(DefaultScale, 100);
        ResetOnDoubleClick(DefaultIntensity, 15);
        ResetOnDoubleClick(DefaultShadow, 50);
        ResetOnDoubleClick(FadeInSlider, 0);
        ResetOnDoubleClick(FadeOutSlider, 1.5);
    }

    private static void ResetOnDoubleClick(Slider slider, double value) =>
        slider.AddHandler(DoubleTappedEvent, (_, _) => slider.Value = value, RoutingStrategies.Bubble, handledEventsToo: true);

    /// <summary>Mostra a fonte do texto na lista; se não estiver instalada neste computador, ela entra no topo mesmo assim.</summary>
    private void SelectFont(string family)
    {
        if (!_fontNames.Any(n => string.Equals(n, family, StringComparison.OrdinalIgnoreCase)))
            _fontNames.Insert(0, family);
        TextFont.SelectedItem = _fontNames.First(n => string.Equals(n, family, StringComparison.OrdinalIgnoreCase));
    }

    private void CenterText(bool vertical)
    {
        EditText(t =>
        {
            t.Align = Slyden.Core.Config.TextAlign.Center;
            t.X = 0.5;
            if (vertical) t.Y = 0.5;
        });
        LoadTextEditor();
    }

    // ---------- arrastar o texto na prévia ----------

    private bool TryNormalized(PointerEventArgs e, out double x, out double y)
    {
        x = y = 0;
        var size = PreviewImage.Bounds.Size;
        if (PreviewImage.Source is null || size.Width <= 0 || size.Height <= 0) return false;
        var p = e.GetPosition(PreviewImage);
        x = p.X / size.Width;
        y = p.Y / size.Height;
        return true;
    }

    private void OnPreviewPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_playing || !e.GetCurrentPoint(PreviewHit).Properties.IsLeftButtonPressed) return;
        if (Current is not { Texts: { Count: > 0 } texts } || _selected < 0) return;
        if (!TryNormalized(e, out double nx, out double ny)) return;

        IReadOnlyList<(double Left, double Top, double Right, double Bottom)> bounds;
        try { bounds = SlidePreview.TextBounds(_cfg, _selected); }
        catch { return; }

        const double margin = 0.015; // um pouco de folga para acertar textos pequenos
        int hit = -1;
        for (int i = texts.Count - 1; i >= 0; i--) // o de cima primeiro
        {
            var b = bounds[i];
            if (b.Right <= b.Left) continue;
            if (nx >= b.Left - margin && nx <= b.Right + margin && ny >= b.Top - margin && ny <= b.Bottom + margin)
            {
                hit = i;
                break;
            }
        }
        if (hit < 0) return;

        ShowTab(1);
        TextList.SelectedIndex = hit;
        _dragText = texts[hit];
        _dragOffsetX = nx - _dragText.X; // segura o texto pelo ponto em que foi clicado, sem ele "pular"
        _dragOffsetY = ny - _dragText.Y;
        e.Pointer.Capture(PreviewHit);
        PreviewHit.Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    private void OnPreviewMoved(object? sender, PointerEventArgs e)
    {
        if (_dragText is null) return;
        if (!TryNormalized(e, out double nx, out double ny)) return;

        double x = Math.Clamp(nx - _dragOffsetX, 0, 1), y = Math.Clamp(ny - _dragOffsetY, 0, 1);
        // "ímã" no centro do quadro, para alinhar sem esforço
        if (Math.Abs(x - 0.5) < 0.012) x = 0.5;
        if (Math.Abs(y - 0.5) < 0.012) y = 0.5;
        _dragText.X = x;
        _dragText.Y = y;

        bool was = _loading;
        _loading = true;
        TextX.Value = x * 100;
        TextY.Value = y * 100;
        _loading = was;

        MarkDirty();
        RefreshPreview();
    }

    private void EndTextDrag(PointerReleasedEventArgs e)
    {
        if (_dragText is null) return;
        _dragText = null;
        e.Pointer.Capture(null);
        PreviewHit.Cursor = Cursor.Default;
    }
}
