using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkiaSharp;
using Slyden.Core.Config;
using Slyden.Core.Render;
using SlideText = Slyden.Core.Config.TextBlock;

namespace Slyden.App;

public sealed class SlideItem(PhotoSpec spec) : INotifyPropertyChanged
{
    private Bitmap? _thumb;
    public PhotoSpec Spec { get; } = spec;
    public string Name => Path.GetFileNameWithoutExtension(Spec.File);
    public Bitmap? Thumb { get => _thumb; set { _thumb = value; Changed(nameof(Thumb)); } }

    public string Tags
    {
        get
        {
            var tags = new List<string>();
            if (Spec.Length is > 1) tags.Add($"×{Spec.Length}");
            if (Spec.Texts is { Count: > 0 }) tags.Add("Aa");
            if (Spec.Effect is not null) tags.Add(MainWindow.EffectName(Spec.Effect.Value));
            return string.Join("  ", tags);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public void RefreshTags() => Changed(nameof(Tags));
}

public partial class MainWindow : Window
{
    private static readonly EffectKind[] Effects =
        [EffectKind.None, EffectKind.ZoomIn, EffectKind.ZoomOut, EffectKind.PanLr, EffectKind.PanRl, EffectKind.TiltUp, EffectKind.TiltDown];

    public static string EffectName(EffectKind kind) => kind switch
    {
        EffectKind.None => "Nenhum", EffectKind.ZoomIn => "Zoom in", EffectKind.ZoomOut => "Zoom out",
        EffectKind.PanLr => "Pan ←→", EffectKind.PanRl => "Pan →←", EffectKind.TiltUp => "Subir", _ => "Descer",
    };

    private const int PreviewMaxSide = 1920;
    private const int PreviewCacheSize = 6;

    private SlideshowConfig _cfg = new();
    private string _folder = "";
    private string? _audio;
    private readonly ObservableCollection<SlideItem> _items = [];
    private int _selected = -1;
    private bool _loading;
    private int _generation;
    private bool _exporting;

    private readonly object _cacheLock = new();
    private readonly Dictionary<string, SKBitmap> _cache = [];
    private readonly Queue<string> _cacheOrder = new();

    public MainWindow()
    {
        InitializeComponent();
        SlideList.ItemsSource = _items;

        BuildChips(EffectChips, ["Padrão", .. Effects.Select(EffectName)], multi: false, OnSlideEffect);
        BuildChips(TransitionChips, ["Padrão", "Corte", "Fade"], multi: false, OnSlideTransition);
        BuildChips(DefaultEffectChips, Effects.Select(EffectName).ToArray(), multi: true, OnDefaultEffects);
        BuildChips(DefaultTransitionChips, ["Corte", "Fade"], multi: false, OnDefaultTransition);
        SetChips(DefaultEffectChips, [1, 2, 3, 4]);
        SetChips(DefaultTransitionChips, [0]);

        OpenFolderButton.Click += async (_, _) => await OpenFolderAsync();
        MusicButton.Click += async (_, _) => await ChooseMusicAsync();
        ExportButton.Click += async (_, _) => { ShowTab(3); await ExportAsync(); };
        ExportButton2.Click += async (_, _) => await ExportAsync();

        Fmt169.Click += (_, _) => SetFormat("16:9");
        Fmt916.Click += (_, _) => SetFormat("9:16");
        Fmt11.Click += (_, _) => SetFormat("1:1");

        TabSlide.Click += (_, _) => ShowTab(0);
        TabText.Click += (_, _) => ShowTab(1);
        TabDefault.Click += (_, _) => ShowTab(2);
        TabExport.Click += (_, _) => ShowTab(3);

        SlideList.SelectionChanged += (_, _) => { _selected = SlideList.SelectedIndex; LoadSlideControls(); RefreshPreview(); };
        MotionSlider.ValueChanged += (_, _) => RefreshPreview();

        IntensitySlider.ValueChanged += (_, _) => EditSlide(s => s.Intensity = IntensitySlider.Value / 100);
        LengthBox.SelectionChanged += (_, _) => EditSlide(s => s.Length = LengthBox.SelectedIndex + 1);
        DimSlider.ValueChanged += (_, _) => EditSlide(s => s.Dim = DimSlider.Value / 100);
        ResetSlideButton.Click += (_, _) => ResetSlide();

        DefaultIntensity.ValueChanged += (_, _) => EditDefault(() => _cfg.Default.Intensity = DefaultIntensity.Value / 100);
        BeatsBox.SelectionChanged += (_, _) => EditDefault(() => _cfg.BeatsPerPhoto = BeatsBox.SelectedIndex switch { 1 => 2, 2 => 4, 3 => 8, _ => null });
        SecondsBox.SelectionChanged += (_, _) => EditDefault(() => _cfg.SecondsPerPhoto = SecondsBox.SelectedIndex switch { 1 => 5, 2 => 8, 3 => 10, _ => null });

        TextList.SelectionChanged += (_, _) => LoadTextEditor();
        AddTextButton.Click += (_, _) => AddText();
        RemoveTextButton.Click += (_, _) => RemoveText();
        TextContent.TextChanged += (_, _) => EditText(t => t.Text = TextContent.Text ?? "", refreshList: true);
        TextFont.TextChanged += (_, _) => EditText(t => t.Font = string.IsNullOrWhiteSpace(TextFont.Text) ? "Arial" : TextFont.Text!.Trim());
        TextSize.ValueChanged += (_, _) => EditText(t => t.Size = TextSize.Value / 100);
        TextAlign.SelectionChanged += (_, _) => EditText(t => t.Align = (Slyden.Core.Config.TextAlign)Math.Max(0, TextAlign.SelectedIndex));
        TextX.ValueChanged += (_, _) => EditText(t => t.X = TextX.Value / 100);
        TextY.ValueChanged += (_, _) => EditText(t => t.Y = TextY.Value / 100);
        TextSpan.SelectionChanged += (_, _) => EditText(t => t.Span = TextSpan.SelectedIndex + 1);

        TextEditor.IsEnabled = false;
        UpdateSummary();
    }

    // ---------- abas e chips ----------

    private void ShowTab(int index)
    {
        TabSlide.IsChecked = index == 0; TabText.IsChecked = index == 1;
        TabDefault.IsChecked = index == 2; TabExport.IsChecked = index == 3;
        PaneSlide.IsVisible = index == 0; PaneText.IsVisible = index == 1;
        PaneDefault.IsVisible = index == 2; PaneExport.IsVisible = index == 3;
    }

    private void BuildChips(WrapPanel panel, IReadOnlyList<string> labels, bool multi, Action<int[]> changed)
    {
        for (int i = 0; i < labels.Count; i++)
        {
            var chip = new ToggleButton { Content = labels[i], Tag = i };
            chip.Classes.Add("chip");
            chip.Click += (_, _) =>
            {
                if (!multi)
                {
                    foreach (var other in panel.Children.OfType<ToggleButton>()) other.IsChecked = other == chip;
                }
                else if (!panel.Children.OfType<ToggleButton>().Any(c => c.IsChecked == true))
                {
                    chip.IsChecked = true; // pelo menos um efeito
                }
                if (!_loading) changed(Checked(panel));
            };
            panel.Children.Add(chip);
        }
    }

    private static int[] Checked(WrapPanel panel) =>
        panel.Children.OfType<ToggleButton>().Where(c => c.IsChecked == true).Select(c => (int)c.Tag!).ToArray();

    private static void SetChips(WrapPanel panel, int[] on)
    {
        foreach (var chip in panel.Children.OfType<ToggleButton>()) chip.IsChecked = on.Contains((int)chip.Tag!);
    }

    // ---------- projeto ----------

    private async Task OpenFolderAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Pasta com as fotos", AllowMultiple = false });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } path) return;

        var fresh = SlideshowConfig.CreateFor(path);
        if (fresh.Photos.Count == 0)
        {
            SlideInfo.Text = "Essa pasta não tem fotos .jpg.";
            return;
        }
        fresh.Format = _cfg.Format;
        _cfg = fresh;
        _folder = path;
        ClearCache();

        _loading = true;
        _items.Clear();
        foreach (var photo in _cfg.Photos) _items.Add(new SlideItem(photo));
        _loading = false;

        ProjectName.Text = Path.GetFileName(path.TrimEnd('\\', '/'));
        TimelineInfo.Text = $"{_items.Count} fotos";
        UpdateSummary();
        SlideList.SelectedIndex = 0;
        _ = LoadThumbnailsAsync(_items.ToList(), path);
    }

    private async Task LoadThumbnailsAsync(List<SlideItem> items, string folder)
    {
        foreach (var item in items)
        {
            if (folder != _folder) return; // trocou de pasta
            Bitmap? thumb = await Task.Run(() =>
            {
                try
                {
                    using var sk = SlidePreview.Thumbnail(Path.Combine(folder, item.Spec.File), 300);
                    return ToBitmap(sk, 80);
                }
                catch { return null; }
            });
            if (thumb is not null) item.Thumb = thumb;
        }
    }

    private async Task ChooseMusicAsync()
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Música do vídeo",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Áudio") { Patterns = ["*.mp3", "*.wav", "*.m4a", "*.aac", "*.flac"] }],
        });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } path) return;
        _audio = path;
        UpdateSummary();
    }

    private void SetFormat(string format)
    {
        _cfg.Format = format;
        Fmt169.IsChecked = format == "16:9"; Fmt916.IsChecked = format == "9:16"; Fmt11.IsChecked = format == "1:1";
        UpdateSummary();
        RefreshPreview();
    }

    private void UpdateSummary()
    {
        var (w, h) = VideoFormat.SizeOf(_cfg.Format);
        ExportSummary.Text = $"Formato {_cfg.Format} ({w}×{h}). " +
            (_audio is null ? "Sem música." : $"Música: {Path.GetFileName(_audio)}.");
        RhythmInfo.Text = _audio is null
            ? (_cfg.SecondsPerPhoto is { } s ? $"sem música · {s} s por foto" : $"sem música · {_cfg.Bpm:0} BPM")
            : $"música: {Path.GetFileName(_audio)}";
    }

    // ---------- edição do slide ----------

    private PhotoSpec? Current => _selected >= 0 && _selected < _cfg.Photos.Count ? _cfg.Photos[_selected] : null;

    private void LoadSlideControls()
    {
        if (Current is not { } spec) { SlideInfo.Text = "Abra uma pasta com fotos para começar"; return; }
        _loading = true;
        SetChips(EffectChips, [spec.Effect is { } e ? 1 + Array.IndexOf(Effects, e) : 0]);
        SetChips(TransitionChips, [spec.Transition is { } t ? 1 + (int)t : 0]);
        IntensitySlider.Value = (spec.Intensity ?? _cfg.Default.Intensity) * 100;
        LengthBox.SelectedIndex = Math.Clamp((spec.Length ?? 1) - 1, 0, 3);
        DimSlider.Value = (spec.Dim ?? 0) * 100;
        RefreshTextList(select: 0);
        _loading = false;
        SlideInfo.Text = $"Foto {_selected + 1} de {_cfg.Photos.Count} · {spec.File}";
    }

    private void EditSlide(Action<PhotoSpec> edit)
    {
        if (_loading || Current is not { } spec) return;
        edit(spec);
        _items[_selected].RefreshTags();
        RefreshPreview();
    }

    private void OnSlideEffect(int[] on) =>
        EditSlide(s => s.Effect = on[0] == 0 ? null : Effects[on[0] - 1]);

    private void OnSlideTransition(int[] on) =>
        EditSlide(s => s.Transition = on[0] == 0 ? null : (TransitionKind)(on[0] - 1));

    private void ResetSlide()
    {
        if (Current is not { } spec) return;
        spec.Effect = null; spec.Intensity = null; spec.Transition = null; spec.Length = null; spec.Dim = null;
        _items[_selected].RefreshTags();
        LoadSlideControls();
        RefreshPreview();
    }

    // ---------- padrão ----------

    private void EditDefault(Action edit)
    {
        if (_loading) return;
        edit();
        UpdateSummary();
        RefreshPreview();
    }

    private void OnDefaultEffects(int[] on) =>
        EditDefault(() => _cfg.Default.Effects = on.Select(i => Effects[i]).ToList());

    private void OnDefaultTransition(int[] on) =>
        EditDefault(() => _cfg.Default.Transition = (TransitionKind)on[0]);

    // ---------- textos ----------

    private void RefreshTextList(int select)
    {
        bool was = _loading;
        _loading = true;
        var texts = Current?.Texts ?? [];
        TextList.ItemsSource = texts.Select(t => string.IsNullOrWhiteSpace(t.Text) ? "(vazio)" : t.Text.Replace("\n", " ")).ToList();
        if (texts.Count > 0) TextList.SelectedIndex = Math.Clamp(select, 0, texts.Count - 1);
        _loading = was;
        LoadTextEditor();
    }

    private SlideText? CurrentText =>
        Current?.Texts is { } list && TextList.SelectedIndex >= 0 && TextList.SelectedIndex < list.Count
            ? list[TextList.SelectedIndex] : null;

    private void LoadTextEditor()
    {
        var text = CurrentText;
        TextEditor.IsEnabled = text is not null;
        if (text is null) return;
        bool was = _loading;
        _loading = true;
        TextContent.Text = text.Text;
        TextFont.Text = text.Font;
        TextSize.Value = text.Size * 100;
        TextAlign.SelectedIndex = (int)text.Align;
        TextX.Value = text.X * 100;
        TextY.Value = text.Y * 100;
        TextSpan.SelectedIndex = Math.Clamp(text.Span - 1, 0, 2);
        _loading = was;
    }

    private void AddText()
    {
        if (Current is not { } spec) return;
        (spec.Texts ??= []).Add(new SlideText { Text = "Novo texto", Font = "Georgia", Size = 0.08 });
        _items[_selected].RefreshTags();
        RefreshTextList(select: spec.Texts.Count - 1);
        RefreshPreview();
    }

    private void RemoveText()
    {
        if (Current is not { } spec || spec.Texts is null || CurrentText is not { } text) return;
        int at = TextList.SelectedIndex;
        spec.Texts.Remove(text);
        if (spec.Texts.Count == 0) spec.Texts = null;
        _items[_selected].RefreshTags();
        RefreshTextList(select: at);
        RefreshPreview();
    }

    private void EditText(Action<SlideText> edit, bool refreshList = false)
    {
        if (_loading || CurrentText is not { } text) return;
        edit(text);
        if (refreshList) RefreshTextList(select: TextList.SelectedIndex);
        RefreshPreview();
    }

    // ---------- prévia ----------

    private async void RefreshPreview()
    {
        if (_cfg.Photos.Count == 0 || _selected < 0) return;
        int generation = ++_generation;
        await Task.Delay(40); // junta várias mudanças seguidas (arrastar um controle)
        if (generation != _generation) return;

        var snapshot = _cfg.Clone();
        int index = _selected;
        double t = MotionSlider.Value;
        string folder = _folder;

        Bitmap? bitmap = null;
        string? error = null;
        await Task.Run(() =>
        {
            try
            {
                using var frame = SlidePreview.Frame(snapshot, folder, index, t, 0.5, null, LoadPhoto);
                bitmap = ToBitmap(frame, 92);
            }
            catch (Exception ex) { error = ex.Message; }
        });
        if (generation != _generation) return;

        if (error is not null) { SlideInfo.Text = error; return; }
        PreviewImage.Source = bitmap;
        EmptyHint.IsVisible = false;
    }

    private static Bitmap ToBitmap(SKBitmap source, int quality)
    {
        using var image = SKImage.FromBitmap(source);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }

    /// <summary>Foto reduzida para a prévia (poucas em memória). Quem chama recebe uma cópia que passa a ser dele.</summary>
    private SKBitmap LoadPhoto(string path)
    {
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(path, out var cached))
            {
                var full = ImageLoader.Load(path);
                int longSide = Math.Max(full.Width, full.Height);
                if (longSide > PreviewMaxSide)
                {
                    double scale = (double)PreviewMaxSide / longSide;
                    var small = full.Resize(
                        new SKImageInfo(Math.Max(1, (int)(full.Width * scale)), Math.Max(1, (int)(full.Height * scale)),
                                        SKColorType.Bgra8888, SKAlphaType.Premul),
                        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                    full.Dispose();
                    full = small ?? throw new InvalidOperationException("Não foi possível reduzir a foto.");
                }
                _cache[path] = cached = full;
                _cacheOrder.Enqueue(path);
                while (_cacheOrder.Count > PreviewCacheSize)
                {
                    string old = _cacheOrder.Dequeue();
                    if (_cache.Remove(old, out var gone)) gone.Dispose();
                }
            }
            return cached.Copy();
        }
    }

    private void ClearCache()
    {
        lock (_cacheLock)
        {
            foreach (var b in _cache.Values) b.Dispose();
            _cache.Clear();
            _cacheOrder.Clear();
        }
    }

    // ---------- exportar ----------

    private async Task ExportAsync()
    {
        if (_exporting) return;
        if (_cfg.Photos.Count == 0) { ExportStatus.Text = "Abra uma pasta com fotos primeiro."; return; }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salvar vídeo",
            SuggestedFileName = $"{ProjectName.Text}-{_cfg.Format.Replace(':', 'x')}.mp4",
            DefaultExtension = "mp4",
            FileTypeChoices = [new FilePickerFileType("Vídeo MP4") { Patterns = ["*.mp4"] }],
        });
        if (file?.TryGetLocalPath() is not { } output) return;

        _exporting = true;
        ExportButton.IsEnabled = ExportButton2.IsEnabled = false;
        ExportProgress.IsVisible = true;
        ExportProgress.Value = 0;
        ExportStatus.Text = "Renderizando…";

        var request = new RenderRequest(_folder, output, _cfg.Clone(), _audio);
        var progress = new Progress<RenderProgress>(p =>
        {
            ExportProgress.Value = p.Fraction * 100;
            ExportStatus.Text = $"Renderizando… foto {p.Slide} de {p.Slides}";
        });

        try
        {
            var summary = await Task.Run(() => SlideshowRenderer.Render(request, progress));
            ExportStatus.Text = $"Pronto: {output} ({summary.DurationSeconds:0} s, {summary.Slides} fotos)." +
                (summary.Warnings.Count > 0 ? "\n" + string.Join("\n", summary.Warnings.Select(w => "Aviso: " + w)) : "");
        }
        catch (Exception ex)
        {
            ExportStatus.Text = "Não foi possível exportar: " + ex.Message;
        }
        finally
        {
            _exporting = false;
            ExportButton.IsEnabled = ExportButton2.IsEnabled = true;
            ExportProgress.IsVisible = false;
        }
    }
}
