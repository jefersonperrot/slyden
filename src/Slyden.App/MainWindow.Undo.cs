using Avalonia.Threading;
using Slyden.Core.Config;

namespace Slyden.App;

/// <summary>Desfazer / refazer: guarda "fotografias" do projeto (configuração + música) a cada alteração.</summary>
public partial class MainWindow
{
    private sealed record UndoState(SlideshowConfig Config, string? Audio, int Selected);

    private const int UndoLimit = 100;
    // alterações em sequência rápida (arrastar um controle deslizante, digitar) valem como UM passo
    private static readonly TimeSpan Coalesce = TimeSpan.FromMilliseconds(700);

    private readonly List<UndoState> _undo = [], _redo = [];
    private UndoState? _committed;
    private DateTime _lastChange = DateTime.MinValue;
    private bool _restoring;

    private UndoState Capture() => new(_cfg.Clone(), _audio, _selected);

    /// <summary>Começa um histórico novo (ao abrir um projeto).</summary>
    private void ResetUndo()
    {
        _undo.Clear();
        _redo.Clear();
        _committed = _project is null ? null : Capture();
        _lastChange = DateTime.MinValue;
        UpdateUndoButtons();
    }

    /// <summary>Chamado depois de cada alteração feita pelo usuário (ver <see cref="MarkDirty"/>).</summary>
    private void RecordChange()
    {
        if (_restoring || _project is null || _committed is null) return;
        var now = DateTime.UtcNow;
        if (now - _lastChange > Coalesce)
        {
            _undo.Add(_committed); // o estado de ANTES desta alteração
            if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
            _redo.Clear();
        }
        _lastChange = now;
        UpdateUndoButtons();
        // o estado novo só é fotografado depois que o tratador do evento terminou de mexer nos dados
        Dispatcher.UIThread.Post(() =>
        {
            if (!_restoring && _project is not null) _committed = Capture();
        });
    }

    private void UpdateUndoButtons()
    {
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
    }

    private bool EditorActive => _project is not null && !HomeView.IsVisible && !NewProjectView.IsVisible && !SettingsView.IsVisible;

    private void Undo() => Step(_undo, _redo);

    private void Redo() => Step(_redo, _undo);

    private void Step(List<UndoState> from, List<UndoState> to)
    {
        if (!EditorActive || from.Count == 0) return;
        var target = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(Capture());
        Apply(target);
    }

    private void Apply(UndoState state)
    {
        _restoring = true;
        try
        {
            StopPlayback(refresh: false);
            var thumbs = new Dictionary<string, Avalonia.Media.Imaging.Bitmap>();
            foreach (var item in _items)
                if (item.Thumb is not null) thumbs[item.Spec.File] = item.Thumb;

            _cfg = state.Config.Clone();
            _audio = state.Audio;

            _loading = true;
            _items.Clear();
            foreach (var photo in _cfg.Photos)
            {
                var item = new SlideItem(photo);
                if (thumbs.TryGetValue(photo.File, out var thumb)) item.Thumb = thumb;
                _items.Add(item);
            }
            SyncGlobalControls();
            _loading = false;
            UpdateSummary();

            int selected = _items.Count == 0 ? -1 : Math.Clamp(state.Selected, 0, _items.Count - 1);
            _suspendSelection = true;
            try { SlideList.SelectedIndex = selected; }
            finally { _suspendSelection = false; }
            _selected = selected;
            UpdateSelectionButtons();
            LoadSlideControls();
            RefreshPreview();
            if (_items.Count == 0)
            {
                PreviewImage.Source = null;
                EmptyHint.IsVisible = true;
            }
            TimelineInfo.Text = $"{_items.Count} fotos";

            _dirty = true;
            UpdateSaveState();

            var missing = _items.Where(i => i.Thumb is null).ToList();
            if (missing.Count > 0) _ = LoadThumbnailsAsync(missing, _folder);
        }
        finally
        {
            _loading = false;
            _restoring = false;
        }
        _committed = Capture();
        _lastChange = DateTime.MinValue;
        UpdateUndoButtons();
    }
}
