using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SkiaSharp;
using Slyden.Core.Config;
using Slyden.Core.Render;
using SlideText = Slyden.Core.Config.TextBlock;

namespace Slyden.App;

public abstract class Notifying : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class SlideItem(PhotoSpec spec) : Notifying
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
            if (Spec.Seconds is { } sec) tags.Add($"{sec:0.#}s");
            if (Spec.IntroSeconds is > 0) tags.Add("♪ depois");
            if (Spec.Texts is { Count: > 0 }) tags.Add("Aa");
            if (Spec.Effect is not null) tags.Add(MainWindow.EffectName(Spec.Effect.Value));
            return string.Join("  ", tags);
        }
    }

    public void RefreshTags() => Changed(nameof(Tags));
}

public sealed class TextItem(SlideText text) : Notifying
{
    public SlideText Text { get; } = text;
    public string Label => string.IsNullOrWhiteSpace(Text.Text) ? "(vazio)" : Text.Text.Replace("\r", "").Replace("\n", " ");
    public void Refresh() => Changed(nameof(Label));
}

/// <summary>Primeiro cartão da grade de projetos: cria um projeto novo.</summary>
public sealed class NewProjectCard : Notifying
{
    private double _cardWidth = 280;
    public double CardWidth { get => _cardWidth; set { _cardWidth = value; Changed(nameof(CardWidth)); } }
}

public sealed class ProjectItem(ProjectEntry entry) : Notifying
{
    private bool _confirming;
    private Bitmap? _thumb;
    private double _cardWidth = 280;
    public double CardWidth { get => _cardWidth; set { _cardWidth = value; Changed(nameof(CardWidth)); } }
    public Bitmap? Thumb { get => _thumb; set { _thumb = value; Changed(nameof(Thumb)); } }
    public string PresetName { get; init; } = "";
    public string Edited => $"editado em {Entry.Project.Modified.ToLocalTime():dd/MM/yyyy HH:mm}" +
                            (PresetName.Length > 0 ? $" · {PresetName}" : "");
    public ProjectEntry Entry { get; } = entry;
    public string Name => Entry.Project.Name;
    public string Details =>
        $"{Entry.Project.Config.Photos.Count} fotos · {Entry.Project.Config.Format} · {Entry.Project.Config.Resolution}p";
    public string Folder => Entry.Project.PhotosFolder.Length > 0 ? Entry.Project.PhotosFolder : "Sem pasta de fotos";
    public bool Confirming { get => _confirming; set { _confirming = value; Changed(nameof(DeleteLabel)); } }
    public string DeleteLabel => _confirming ? "Confirmar exclusão" : "Excluir";
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

    private static readonly string[] FormatLabels = ["16:9 (YouTube)", "9:16 (Reels, Stories)", "1:1 (feed)"];
    private static readonly string[] FormatValues = ["16:9", "9:16", "1:1"];
    private static readonly string[] ResolutionLabels = ["720p HD", "1080p Full HD", "1440p 2K", "2160p 4K"];

    private const int PreviewMaxSide = 1920;
    private const int PreviewCacheSize = 6;

    private readonly ProjectStore _store = new(ProjectStore.DefaultRoot);
    private ProjectFile? _project;
    private SlideshowConfig _cfg = new();
    private string _folder = "";
    private string? _audio;
    private bool _dirty;

    private readonly ObservableCollection<SlideItem> _items = [];
    private readonly ObservableCollection<TextItem> _texts = [];
    private int _selected = -1;
    private bool _loading;
    private int _generation;
    private bool _exporting;
    private CancellationTokenSource? _exportCts;

    private readonly object _cacheLock = new();
    private readonly Dictionary<string, SKBitmap> _cache = [];
    private readonly Queue<string> _cacheOrder = new();

    public MainWindow()
    {
        InitializeComponent();
        SlideList.ItemsSource = _items;
        TextList.ItemsSource = _texts;

        BuildChips(EffectChips, ["Padrão", .. Effects.Select(EffectName)], multi: false, OnSlideEffect);
        BuildChips(TransitionChips, ["Padrão", "Corte", "Fade"], multi: false, OnSlideTransition);
        BuildChips(DefaultEffectChips, Effects.Select(EffectName).ToArray(), multi: true, OnDefaultEffects);
        BuildChips(DefaultTransitionChips, ["Corte", "Fade"], multi: false, OnDefaultTransition);
        BuildChips(BackgroundChips, ["Padrão", "Desfocado", "Preto", "Cor"], multi: false, OnSlideBackground);
        BuildChips(DefaultBackgroundChips, ["Desfocado", "Preto", "Cor"], multi: false, OnDefaultBackground);
        foreach (var hex in new[] { "#FFFFFF", "#F3EBDD", "#C9C9CE", "#6B6B73", "#1B2A41", "#7A1F2B", "#D9A5A5", "#3E5C4B" })
        {
            var swatch = new Button
            {
                Width = 26, Height = 26, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(0),
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex)),
                BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#55555B")),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Tag = hex,
            };
            swatch.Click += (_, _) => SetBackgroundColor(hex);
            ColorSwatches.Children.Add(swatch);
        }
        BackgroundColorView.ColorChanged += (_, e) =>
        {
            if (_loading) return;
            var c = e.NewColor;
            SetBackgroundColor($"#{c.R:X2}{c.G:X2}{c.B:X2}");
        };
        BackgroundColorBox.TextChanged += (_, _) =>
        {
            var text = BackgroundColorBox.Text?.Trim() ?? "";
            if (!HexColor.TryParse(text, out var c) || text.Length != 7) return;
            ColorPreview.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(c.R, c.G, c.B));
            EditDefault(() => _cfg.Default.BackgroundColor = text.ToUpperInvariant());
        };
        BuildChips(ExportFormatChips, FormatLabels, multi: false, _ => UpdateSummary());
        BuildChips(ExportResolutionChips, ResolutionLabels, multi: false, _ => UpdateSummary());
        BuildChips(NewFormatChips, FormatLabels, multi: false, _ => UpdateNewSizeInfo());
        BuildChips(NewResolutionChips, ResolutionLabels, multi: false, _ => UpdateNewSizeInfo());

        PlayButton.Click += async (_, _) => await TogglePlayAsync();
        InitMenu();
        InitSlideTiming();
        UpdateEditorActions();
        ProjectList.SizeChanged += (_, _) => UpdateCardWidth();
        HomeSearch.TextChanged += (_, _) => ApplyHomeFilter();
        HomeSort.SelectionChanged += (_, _) => { if (IsLoaded) ApplyHomeFilter(); };
        HomeView.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) UpdateEditorActions(); };
        NewProjectView.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) UpdateEditorActions(); };
        NewProjectButton.Click += (_, _) => ShowNewProject();
        NewFolderButton.Click += async (_, _) => await ChooseNewFolderAsync();
        NewProjectFolderButton.Click += async (_, _) => await ChooseProjectFolderAsync();
        NewCreateButton.Click += (_, _) => CreateProject();
        NewCancelButton.Click += (_, _) => { NewProjectView.IsVisible = false; HomeView.IsVisible = true; };
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        SelectAllButton.Click += (_, _) => ToggleSelectAll();
        ProjectList.DoubleTapped += (_, _) => { if (ProjectList.SelectedItem is ProjectItem p) OpenProject(p.Entry.Project); };
        SaveButton.Click += (_, _) => Save();
        RemoveMusicButton.Click += (_, _) => RemoveMusic();
        UndoButton.Click += (_, _) => Undo();
        RedoButton.Click += (_, _) => Redo();
        ExportButton.Click += async (_, _) => { ShowTab(3); await ExportOrCancelAsync(); };
        ExportButton2.Click += async (_, _) => await ExportOrCancelAsync();
        CancelExportButton.Click += (_, _) => CancelExport();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Save(); e.Handled = true; }
            else if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control) { Undo(); e.Handled = true; }
            else if ((e.Key == Key.Y && e.KeyModifiers == KeyModifiers.Control)
                     || (e.Key == Key.Z && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)))
            { Redo(); e.Handled = true; }
        };

        Fmt169.Click += (_, _) => SetFormat("16:9");
        Fmt916.Click += (_, _) => SetFormat("9:16");
        Fmt11.Click += (_, _) => SetFormat("1:1");

        TabSlide.Click += (_, _) => ShowTab(0);
        TabText.Click += (_, _) => ShowTab(1);
        TabDefault.Click += (_, _) => ShowTab(2);
        TabExport.Click += (_, _) => ShowTab(3);

        SlideList.AddHandler(PointerPressedEvent, OnTimelinePressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        SlideList.AddHandler(PointerMovedEvent, OnTimelineMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        SlideList.AddHandler(PointerReleasedEvent, (_, _) => _pressedIndex = -1, RoutingStrategies.Tunnel, handledEventsToo: true);
        SlideList.SelectionChanged += (_, _) =>
        {
            if (_suspendSelection) return;
            if (_playing) StopPlayback(refresh: false); // o usuário clicou numa foto: para a prévia
            _selected = SlideList.SelectedIndex;
            UpdateSelectionButtons();
            LoadSlideControls();
            RefreshPreview();
        };
        ScaleSlider.ValueChanged += (_, _) =>
        {
            ScaleValue.Text = $"{ScaleSlider.Value:0}%";
            EditSlide(s => s.Scale = ScaleSlider.Value / 100);
        };
        DefaultShadow.ValueChanged += (_, _) =>
        {
            DefaultShadowValue.Text = $"{DefaultShadow.Value:0}%";
            EditDefault(() => _cfg.Default.Shadow = DefaultShadow.Value / 100);
        };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.None) return;
            if (!EditorActive || !PlayButton.IsEnabled) return;
            if (FocusManager?.GetFocusedElement() is TextBox) return;
            e.Handled = true;
            _ = TogglePlayAsync();
        }, RoutingStrategies.Tunnel);
        DefaultScale.ValueChanged += (_, _) =>
        {
            DefaultScaleValue.Text = $"{DefaultScale.Value:0}%";
            EditDefault(() => _cfg.Default.Scale = DefaultScale.Value / 100);
        };
        SlideList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete) { RemovePhoto(); e.Handled = true; }
        };
        MotionSlider.ValueChanged += (_, _) => RefreshPreview();

        MoveLeftButton.Click += (_, _) => MovePhoto(-1);
        MoveRightButton.Click += (_, _) => MovePhoto(1);
        RemovePhotoButton.Click += (_, _) => RemovePhoto();
        AddPhotosButton.Click += async (_, _) => await AddPhotosAsync();

        IntensitySlider.ValueChanged += (_, _) => EditSlide(s => s.Intensity = IntensitySlider.Value / 100);
        LengthBox.SelectionChanged += (_, _) => EditSlide(s => s.Length = LengthBox.SelectedIndex + 1);
        DimSlider.ValueChanged += (_, _) => EditSlide(s => s.Dim = DimSlider.Value / 100);
        ResetSlideButton.Click += (_, _) => ResetSlide();
        CoverButton.Click += (_, _) => CreateCover();

        DefaultIntensity.ValueChanged += (_, _) => EditDefault(() => _cfg.Default.Intensity = DefaultIntensity.Value / 100);
        BeatsBox.SelectionChanged += (_, _) => EditDefault(() => _cfg.BeatsPerPhoto = BeatsBox.SelectedIndex switch { 1 => 2, 2 => 4, 3 => 8, _ => null });
        MusicModeBox.SelectionChanged += (_, _) =>
            EditDefault(() => _cfg.MusicMode = MusicModeBox.SelectedIndex == 1 ? MusicMode.FitSlides : MusicMode.FitMusic);
        FadeInSlider.ValueChanged += (_, _) =>
        {
            FadeInValue.Text = Seconds(FadeInSlider.Value);
            EditDefault(() => _cfg.AudioFadeIn = FadeInSlider.Value);
        };
        FadeOutSlider.ValueChanged += (_, _) =>
        {
            FadeOutValue.Text = Seconds(FadeOutSlider.Value);
            EditDefault(() => _cfg.AudioFadeOut = FadeOutSlider.Value);
        };
        SecondsBox.SelectionChanged += (_, _) =>
        {
            SecondsRow.IsVisible = SecondsBox.SelectedIndex == 1;
            EditDefault(() => _cfg.SecondsPerPhoto = SecondsBox.SelectedIndex == 1 ? SecondsValue.Value is { } v ? (double)v : 5 : null);
        };
        SecondsValue.ValueChanged += (_, e) =>
        {
            if (SecondsBox.SelectedIndex != 1 || e.NewValue is not { } v || v <= 0) return;
            EditDefault(() => _cfg.SecondsPerPhoto = (double)v);
        };

        TextList.SelectionChanged += (_, _) => LoadTextEditor();
        AddTextButton.Click += (_, _) => AddText();
        RemoveTextButton.Click += (_, _) => RemoveText();
        TextContent.TextChanged += (_, _) => EditText(t => t.Text = TextContent.Text ?? "");
        InitTextTools();
        TextSize.ValueChanged += (_, _) => EditText(t => t.Size = TextSize.Value / 100);
        TextAlign.SelectionChanged += (_, _) => EditText(t => t.Align = (Slyden.Core.Config.TextAlign)Math.Max(0, TextAlign.SelectedIndex));
        TextX.ValueChanged += (_, _) => EditText(t => t.X = TextX.Value / 100);
        TextY.ValueChanged += (_, _) => EditText(t => t.Y = TextY.Value / 100);
        TextSpan.SelectionChanged += (_, _) => EditText(t => t.Span = TextSpan.SelectedIndex + 1);

        TextEditor.IsEnabled = false;
        ShowHome();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        StopPlayback(refresh: false);
        if (_project is not null && _dirty) Save();
        base.OnClosing(e);
    }

    // ---------- projetos ----------

    private void ShowHome()
    {
        StopPlayback(refresh: false);
        SettingsView.IsVisible = false; // as Configurações ficam por cima da lista; sem isto o botão "Projetos" parecia não fazer nada
        if (_project is not null && _dirty) Save();
        RefreshProjectList();
        HomeView.IsVisible = true;
    }

    private List<ProjectItem> _homeAll = [];
    private readonly Dictionary<string, Bitmap> _homeThumbs = [];

    private static string ThumbKey(ProjectEntry e) => e.Path + "|" + e.Project.Modified.Ticks;

    private void RefreshProjectList()
    {
        _homeAll = _store.List().Select(e => new ProjectItem(e) { PresetName = _library.Find(e.Project.PresetId)?.Name ?? "" }).ToList();
        foreach (var item in _homeAll)
            if (_homeThumbs.TryGetValue(ThumbKey(item.Entry), out var cached)) item.Thumb = cached;
        ApplyHomeFilter();
        _ = LoadHomeThumbsAsync(_homeAll.Where(i => i.Thumb is null).ToList());
    }

    /// <summary>Busca e ordenação da grade (sem reler os arquivos).</summary>
    private void ApplyHomeFilter()
    {
        string query = HomeSearch.Text?.Trim() ?? "";
        var shown = _homeAll.Where(p => query.Length == 0 || p.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        shown = HomeSort.SelectedIndex == 1
            ? shown.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            : shown.OrderByDescending(p => p.Entry.Project.Modified);
        var cards = new List<object> { new NewProjectCard() };
        cards.AddRange(shown);
        ProjectList.ItemsSource = cards;
        UpdateCardWidth();
        NoProjects.IsVisible = shown.Count() == 0;
        NoProjects.Text = _homeAll.Count == 0 ? "Nenhum projeto ainda. Clique em Novo projeto." : "Nenhum projeto com esse nome.";
    }

    /// <summary>Miniatura do projeto = a primeira foto dele.</summary>
    private async Task LoadHomeThumbsAsync(List<ProjectItem> items)
    {
        foreach (var item in items)
        {
            var project = item.Entry.Project;
            if (project.Config.Photos.Count == 0) continue;
            string file = Path.Combine(project.PhotosFolder, project.Config.Photos[0].File);
            Bitmap? thumb = await Task.Run(() =>
            {
                try
                {
                    using var sk = SlidePreview.Thumbnail(file, 560);
                    return ToBitmap(sk, 85);
                }
                catch { return null; }
            });
            if (thumb is null) continue;
            _homeThumbs[ThumbKey(item.Entry)] = thumb;
            item.Thumb = thumb;
        }
    }

    /// <summary>Distribui as colunas de modo que a grade vá de uma borda à outra (alinhada ao título e ao botão).</summary>
    private void UpdateCardWidth()
    {
        double available = ProjectList.Bounds.Width; // a lista avança 16 px para a direita para compensar a margem do último cartão
        if (available <= 0) return;
        const double minWidth = 280, gap = 16;
        int columns = Math.Max(1, (int)(available / (minWidth + gap)));
        double width = Math.Floor(available / columns - gap);
        foreach (var card in ProjectList.ItemsSource?.OfType<object>() ?? [])
        {
            if (card is ProjectItem p) p.CardWidth = width;
            else if (card is NewProjectCard n) n.CardWidth = width;
        }
    }

    private void OnNewCardClick(object? sender, RoutedEventArgs e) => ShowNewProject();

    private void UpdateEditorActions() =>
        EditorActions.IsVisible = _project is not null && !HomeView.IsVisible && !NewProjectView.IsVisible;
    private string _newFolder = "";
    private string _newProjectFolder = "";

    private async Task ChooseProjectFolderAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Pasta onde gravar o projeto", AllowMultiple = false });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } path) return;
        _newProjectFolder = path;
        NewProjectFolderText.Text = path;
    }

    private void ShowNewProject()
    {
        _newProjectFolder = "";
        NewProjectFolderText.Text = "Pasta padrão do programa";
        HomeMessage.Text = "";
        NewMessage.Text = "";
        NewName.Text = "";
        _newFolder = "";
        NewFolderText.Text = "Nenhuma pasta escolhida";
        bool was = _loading;
        _loading = true;
        NewPresetBox.ItemsSource = _library.Presets.Select(p => p.Id == _library.DefaultId ? p.Name + "  ★" : p.Name).ToList();
        NewPresetBox.SelectedIndex = Math.Max(0, _library.Presets.IndexOf(_library.Default));
        ApplyNewPresetChips();
        _loading = was;
        UpdateNewSizeInfo();
        HomeView.IsVisible = false;
        NewProjectView.IsVisible = true;
        NewName.Focus();
    }

    private void ApplyNewPresetChips()
    {
        var p = _library.Presets[Math.Clamp(NewPresetBox.SelectedIndex, 0, _library.Presets.Count - 1)];
        SetChips(NewFormatChips, [Math.Max(0, Array.IndexOf(FormatValues, p.Format))]);
        SetChips(NewResolutionChips, [Math.Max(0, Array.IndexOf(VideoFormat.Resolutions, p.Resolution))]);
    }

    private void UpdateNewSizeInfo()
    {
        var format = FormatValues[Checked(NewFormatChips).FirstOrDefault()];
        int resolution = VideoFormat.Resolutions[Checked(NewResolutionChips).FirstOrDefault(1)];
        var (w, h) = VideoFormat.SizeOf(format, resolution);
        NewSizeInfo.Text = $"O vídeo terá {w}×{h} pixels.";
    }

    private async Task ChooseNewFolderAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Pasta com as fotos do projeto", AllowMultiple = false });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } path) return;
        _newFolder = path;
        NewFolderText.Text = path;
        if (string.IsNullOrWhiteSpace(NewName.Text) && Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } name)
            NewName.Text = name;
    }

    private void CreateProject()
    {
        string name = NewName.Text?.Trim() ?? "";
        if (name.Length == 0) { NewMessage.Text = "Dê um nome ao projeto."; return; }

        var config = _newFolder.Length > 0 ? SlideshowConfig.CreateFor(_newFolder) : new SlideshowConfig();
        if (_newFolder.Length > 0 && config.Photos.Count == 0)
        {
            NewMessage.Text = "Essa pasta não tem fotos .jpg. Escolha outra ou crie o projeto sem pasta.";
            return;
        }
        var preset = _library.Presets[Math.Clamp(NewPresetBox.SelectedIndex, 0, _library.Presets.Count - 1)];
        preset.ApplyTo(config); // efeitos, fundo, música... do preset escolhido
        config.Format = FormatValues[Checked(NewFormatChips).FirstOrDefault()];
        config.Resolution = VideoFormat.Resolutions[Checked(NewResolutionChips).FirstOrDefault(1)];

        var project = new ProjectFile { Name = name, PhotosFolder = _newFolder, Config = config, PresetId = preset.Id };
        try
        {
            if (_newProjectFolder.Length > 0)
            {
                Directory.CreateDirectory(_newProjectFolder);
                project.FilePath = ProjectStore.NewPathIn(_newProjectFolder, name);
            }
            _store.Save(project);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NewMessage.Text = "Não consegui gravar o projeto nessa pasta: " + ex.Message;
            return;
        }
        NewProjectView.IsVisible = false;
        OpenProject(project);
    }

    private void OnProjectAction(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ProjectItem item } button) return;
        HomeMessage.Text = "";
        switch (button.Tag as string)
        {
            case "open":
                OpenProject(item.Entry.Project);
                break;
            case "rename":
                _ = RenameProjectAsync(item.Entry.Project);
                break;
            case "duplicate":
                _store.DuplicateFile(item.Entry.Path);
                RefreshProjectList();
                break;
            case "delete":
                if (!item.Confirming) { item.Confirming = true; break; }
                _store.DeleteFile(item.Entry.Path);
                RefreshProjectList();
                break;
        }
    }

    private void OpenProject(ProjectFile project)
    {
        if (project.PhotosFolder.Length > 0 && !Directory.Exists(project.PhotosFolder))
        {
            HomeMessage.Text = $"A pasta de fotos não foi encontrada: {project.PhotosFolder}";
            return;
        }

        StopPlayback(refresh: false);
        _project = project;
        _cfg = project.Config;
        _folder = project.PhotosFolder;
        _audio = project.AudioPath is { } a && File.Exists(a) ? a : null;
        _dirty = false;
        ClearCache();

        _loading = true;
        _items.Clear();
        foreach (var photo in _cfg.Photos) _items.Add(new SlideItem(photo));
        SyncGlobalControls();
        _loading = false;

        PreviewImage.Source = null;
        EmptyHint.IsVisible = true;
        HomeView.IsVisible = false;
        UpdateSaveState();
        UpdateSummary();
        ExportStatus.Text = "";
        SlideList.SelectedIndex = _items.Count > 0 ? 0 : -1;
        _selected = SlideList.SelectedIndex;
        LoadSlideControls();
        RefreshPreview();
        ResetUndo();
        _ = LoadThumbnailsAsync(_items.ToList(), _folder);
    }

    private static string Seconds(double s) => s == 0 ? "0 s" : $"{s:0.#} s";

    private void SyncGlobalControls()
    {
        SetChips(DefaultEffectChips, _cfg.Default.Effects.Select(e => Array.IndexOf(Effects, e)).Where(i => i >= 0).ToArray());
        SetChips(DefaultTransitionChips, [(int)_cfg.Default.Transition]);
        DefaultIntensity.Value = _cfg.Default.Intensity * 100;
        DefaultScale.Value = _cfg.Default.Scale * 100;
        DefaultScaleValue.Text = $"{DefaultScale.Value:0}%";
        DefaultShadow.Value = _cfg.Default.Shadow * 100;
        DefaultShadowValue.Text = $"{DefaultShadow.Value:0}%";
        SetChips(DefaultBackgroundChips, [(int)_cfg.Default.Background]);
        BackgroundColorBox.Text = _cfg.Default.BackgroundColor;
        if (HexColor.TryParse(_cfg.Default.BackgroundColor, out var bg))
        {
            var color = Avalonia.Media.Color.FromRgb(bg.R, bg.G, bg.B);
            ColorPreview.Background = new Avalonia.Media.SolidColorBrush(color);
            BackgroundColorView.Color = color;
        }
        BeatsBox.SelectedIndex = _cfg.BeatsPerPhoto switch { 2 => 1, 4 => 2, 8 => 3, _ => 0 };
        SecondsBox.SelectedIndex = _cfg.SecondsPerPhoto is null ? 0 : 1;
        SecondsRow.IsVisible = _cfg.SecondsPerPhoto is not null;
        if (_cfg.SecondsPerPhoto is { } secs) SecondsValue.Value = (decimal)secs;
        MusicModeBox.SelectedIndex = _cfg.MusicMode == MusicMode.FitSlides ? 1 : 0;
        FadeInSlider.Value = _cfg.AudioFadeIn;
        FadeOutSlider.Value = _cfg.AudioFadeOut;
        FadeInValue.Text = Seconds(_cfg.AudioFadeIn);
        FadeOutValue.Text = Seconds(_cfg.AudioFadeOut);
        Fmt169.IsChecked = _cfg.Format == "16:9";
        Fmt916.IsChecked = _cfg.Format == "9:16";
        Fmt11.IsChecked = _cfg.Format == "1:1";
        SyncExportChips();
    }

    private void Save()
    {
        if (_project is null) return;
        _project.PhotosFolder = _folder;
        _project.AudioPath = _audio;
        _project.Config = _cfg;
        try
        {
            _store.Save(_project);
            _dirty = false;
        }
        catch (Exception ex)
        {
            SlideInfo.Text = "Não foi possível salvar: " + ex.Message;
        }
        UpdateSaveState();
    }

    private void MarkDirty()
    {
        if (_project is null) return;
        StopPlayback(refresh: false); // a prévia tocada mostraria a versão antiga
        RecordChange();
        _dirty = true;
        UpdateSaveState();
    }

    private void UpdateSaveState()
    {
        ProjectName.Text = _project is null ? "Nenhum projeto aberto" : _project.Name + (_dirty ? " •" : "");
        SaveState.Text = _project is null ? "" : _dirty ? "alterações não salvas" : "salvo";
    }

    // ---------- abas e chips ----------

    private void ShowTab(int index)
    {
        TabSlide.IsChecked = index == 0; TabText.IsChecked = index == 1;
        TabDefault.IsChecked = index == 2; TabExport.IsChecked = index == 3;
        PaneSlide.IsVisible = index == 0; PaneText.IsVisible = index == 1;
        PaneDefault.IsVisible = index == 2; PaneExport.IsVisible = index == 3;
        UpdateTextSelection();
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

    // ---------- fotos ----------

    private async Task LoadThumbnailsAsync(List<SlideItem> items, string folder)
    {
        foreach (var item in items)
        {
            if (folder != _folder) return; // trocou de projeto
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

    private List<int> SelectedIndices() =>
        SlideList.SelectedItems?.OfType<SlideItem>().Select(i => _items.IndexOf(i)).Where(i => i >= 0).Order().ToList() ?? [];

    private void UpdateSelectionButtons()
    {
        int count = SelectedIndices().Count;
        RemovePhotoButton.Content = count > 1 ? $"Remover {count} fotos" : "Remover foto";
        SelectAllButton.Content = _items.Count > 0 && count == _items.Count ? "Desmarcar todas" : "Selecionar todas";
    }

    private void ToggleSelectAll()
    {
        if (_items.Count == 0) return;
        if (SelectedIndices().Count == _items.Count) SlideList.SelectedItems?.Clear();
        else SlideList.SelectAll();
    }

    private void RemovePhoto()
    {
        var indices = SelectedIndices();
        if (indices.Count == 0) return;
        int first = indices[0];
        foreach (int at in Enumerable.Reverse(indices))
        {
            _cfg.Photos.RemoveAt(at);
            _items.RemoveAt(at);
        }
        MarkDirty();
        TimelineInfo.Text = $"{_items.Count} fotos";
        int next = Math.Min(first, _items.Count - 1);
        SlideList.SelectedIndex = next;
        _selected = next;
        UpdateSelectionButtons();
        LoadSlideControls();
        if (next < 0)
        {
            PreviewImage.Source = null;
            EmptyHint.IsVisible = true;
            SlideInfo.Text = "Projeto sem fotos. Use + Fotos ou arraste fotos para cá.";
        }
        else
        {
            RefreshPreview();
            SlideInfo.Text = indices.Count > 1
                ? $"{indices.Count} fotos removidas do projeto (os arquivos continuam no computador)."
                : SlideInfo.Text;
        }
    }

    private void MovePhoto(int direction)
    {
        var chosen = SelectedIndices();
        if (chosen.Count == 0) return;
        if (direction < 0 && chosen[0] == 0) return;
        if (direction > 0 && chosen[^1] == _items.Count - 1) return;
        ApplyReorder(chosen, direction < 0 ? chosen[0] - 1 : chosen[^1] + 2);
    }

    /// <summary>Leva as fotos nas posições <paramref name="indices"/> para antes de <paramref name="insertBefore"/> e mantém a seleção nelas.</summary>
    private void ApplyReorder(IReadOnlyList<int> indices, int insertBefore)
    {
        _suspendSelection = true;
        IReadOnlyList<int> now;
        try
        {
            now = ListReorder.MoveBlock(_cfg.Photos, indices, insertBefore);
            ListReorder.MoveBlock(_items, indices, insertBefore);
            SlideList.SelectedItems?.Clear();
            foreach (int i in now) SlideList.SelectedItems?.Add(_items[i]);
        }
        finally { _suspendSelection = false; }
        if (now.Count == 0) return;

        _selected = SlideList.SelectedIndex;
        MarkDirty();
        UpdateSelectionButtons();
        LoadSlideControls();
        RefreshPreview();
        SlideList.ScrollIntoView(_items[now[0]]);
        SlideInfo.Text = now.Count == 1
            ? $"Foto movida para a posição {now[0] + 1}."
            : $"{now.Count} fotos movidas para a posição {now[0] + 1}.";
    }

    // ---------- arrastar para reordenar ----------

    private int _pressedIndex = -1;
    private Point _pressPoint;
    private PointerPressedEventArgs? _pressEvent;
    private IReadOnlyList<int> _selectionAtPress = [];
    private List<int>? _dragIndices;
    private double _dragX;
    private bool _suspendSelection;
    private DispatcherTimer? _scrollTimer;

    private void OnTimelinePressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedIndex = -1;
        if (!e.GetCurrentPoint(SlideList).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>() is { } container)
        {
            _pressedIndex = SlideList.IndexFromContainer(container);
            _pressEvent = e;
            _pressPoint = e.GetPosition(SlideList);
            _selectionAtPress = SelectedIndices(); // antes do clique mudar a seleção
        }
    }

    private void OnTimelineMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedIndex < 0 || _dragIndices is not null) return;
        if (!e.GetCurrentPoint(SlideList).Properties.IsLeftButtonPressed) { _pressedIndex = -1; return; }
        var p = e.GetPosition(SlideList);
        if (Math.Abs(p.X - _pressPoint.X) < 8 && Math.Abs(p.Y - _pressPoint.Y) < 8) return;
        if (_pressEvent is { } pressed) _ = BeginDragAsync(pressed, p.X);
    }

    private async Task BeginDragAsync(PointerPressedEventArgs pressed, double startX)
    {
        _dragIndices = _selectionAtPress.Contains(_pressedIndex) ? _selectionAtPress.ToList() : [_pressedIndex];
        _pressedIndex = -1;
        _dragX = startX;
        _scrollTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _scrollTimer.Tick -= AutoScroll;
        _scrollTimer.Tick += AutoScroll;
        _scrollTimer.Start();

        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText("slyden-slides"));
        try { await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Move); }
        finally { EndDrag(); }
    }

    private void EndDrag()
    {
        _dragIndices = null;
        _pressedIndex = -1;
        DropMarker.IsVisible = false;
        _scrollTimer?.Stop();
    }

    /// <summary>Rola a linha do tempo quando se arrasta perto da borda (projetos com centenas de fotos).</summary>
    private void AutoScroll(object? sender, EventArgs e)
    {
        if (_dragIndices is null) return;
        var viewer = SlideList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (viewer is null) return;
        double width = SlideList.Bounds.Width;
        double dx = _dragX < 60 ? -28 : _dragX > width - 60 ? 28 : 0;
        if (dx == 0) return;
        double max = Math.Max(0, viewer.Extent.Width - viewer.Viewport.Width);
        viewer.Offset = new Vector(Math.Clamp(viewer.Offset.X + dx, 0, max), viewer.Offset.Y);
    }

    private int InsertIndexAt(Point p)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (SlideList.ContainerFromIndex(i) is not { } c || c.TranslatePoint(new Point(0, 0), SlideList) is not { } topLeft) continue;
            if (p.X < topLeft.X + c.Bounds.Width / 2) return i;
        }
        return _items.Count;
    }

    private void ShowDropMarker(int insertBefore)
    {
        double x = 0;
        if (_items.Count > 0)
        {
            int at = Math.Min(insertBefore, _items.Count - 1);
            if (SlideList.ContainerFromIndex(at) is { } c && c.TranslatePoint(new Point(0, 0), SlideList) is { } topLeft)
                x = insertBefore < _items.Count ? topLeft.X - 3 : topLeft.X + c.Bounds.Width + 1;
        }
        Canvas.SetLeft(DropMarker, x);
        DropMarker.IsVisible = true;
    }

    private bool OverTimeline(Point p) => new Rect(SlideList.Bounds.Size).Contains(p);

    private async Task AddPhotosAsync()
    {
        if (_project is null) return;
        IStorageFolder? start = _folder.Length > 0 ? await StorageProvider.TryGetFolderFromPathAsync(_folder) : null;
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Incluir fotos no projeto",
            AllowMultiple = true,
            SuggestedStartLocation = start,
            FileTypeFilter = [new FilePickerFileType("Fotos JPG") { Patterns = ["*.jpg", "*.jpeg"] }],
        });
        AddPhotoPaths(picked.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    private static bool IsPhoto(string path) =>
        path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

    /// <summary>Nome gravado no projeto: relativo à pasta do projeto quando a foto está dentro dela, senão o caminho completo.</summary>
    private string ProjectPathOf(string full)
    {
        if (_folder.Length == 0) return full;
        string root = Path.GetFullPath(_folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return Path.GetFullPath(full).StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(_folder, full)
            : full;
    }

    private void AddPhotoPaths(IEnumerable<string> paths)
    {
        if (_project is null) return;
        var added = new List<SlideItem>();
        int duplicates = 0;
        foreach (var full in paths.Where(IsPhoto).Order(StringComparer.OrdinalIgnoreCase))
        {
            string name = ProjectPathOf(full);
            if (_cfg.Photos.Any(p => string.Equals(p.File, name, StringComparison.OrdinalIgnoreCase))) { duplicates++; continue; }
            var spec = new PhotoSpec { File = name };
            _cfg.Photos.Add(spec);
            var item = new SlideItem(spec);
            _items.Add(item);
            added.Add(item);
        }

        if (added.Count > 0)
        {
            MarkDirty();
            TimelineInfo.Text = $"{_items.Count} fotos";
            SlideList.SelectedItem = added[0];
            _ = LoadThumbnailsAsync(added, _folder);
        }
        SlideInfo.Text = added.Count > 0
            ? $"{added.Count} foto(s) incluída(s)." + (duplicates > 0 ? $" {duplicates} já estava(m) no projeto." : "")
            : duplicates > 0 ? "Essas fotos já estão no projeto." : "Nenhuma foto .jpg encontrada.";
    }

    // ---------- arrastar e soltar ----------

    private bool CanDrop => _project is not null && !HomeView.IsVisible && !NewProjectView.IsVisible;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (_dragIndices is not null) // reordenando dentro da linha do tempo
        {
            var p = e.GetPosition(SlideList);
            _dragX = p.X;
            bool over = OverTimeline(p);
            e.DragEffects = over ? DragDropEffects.Move : DragDropEffects.None;
            if (over) ShowDropMarker(InsertIndexAt(p)); else DropMarker.IsVisible = false;
            e.Handled = true;
            return;
        }
        e.DragEffects = CanDrop && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (_dragIndices is not null)
        {
            var p = e.GetPosition(SlideList);
            if (OverTimeline(p)) ApplyReorder(_dragIndices, InsertIndexAt(p));
            DropMarker.IsVisible = false;
            e.Handled = true;
            return;
        }
        if (!CanDrop) return;
        var paths = new List<string>();
        foreach (var item in e.DataTransfer.TryGetFiles() ?? [])
        {
            if (item.TryGetLocalPath() is not { } local) continue;
            if (Directory.Exists(local))
                paths.AddRange(Directory.EnumerateFiles(local).Where(IsPhoto));
            else paths.Add(local);
        }
        AddPhotoPaths(paths);
        e.Handled = true;
    }

    // ---------- música, formato, resumo ----------

    private async Task ChooseMusicAsync()
    {
        if (_project is null) return;
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Música do vídeo",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Áudio") { Patterns = ["*.mp3", "*.wav", "*.m4a", "*.aac", "*.flac"] }],
        });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } path) return;
        StopPlayback(refresh: true);
        _audio = path;
        MarkDirty();
        UpdateSummary();
    }

    private void OnRemoveMusicTimeout(object? sender, EventArgs e) => ResetRemoveMusicConfirmation();

    private bool _confirmingRemoveMusic;
    private DispatcherTimer? _removeMusicTimer;

    private void ResetRemoveMusicConfirmation()
    {
        _confirmingRemoveMusic = false;
        _removeMusicTimer?.Stop();
        RemoveMusicButton.Content = "Remover música";
    }

    /// <summary>Primeiro clique pede confirmação (volta ao normal em 4 s); o segundo remove.</summary>
    private void RemoveMusic()
    {
        if (_audio is null) return;
        if (!_confirmingRemoveMusic)
        {
            _confirmingRemoveMusic = true;
            RemoveMusicButton.Content = "Confirmar remoção?";
            _removeMusicTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _removeMusicTimer.Tick -= OnRemoveMusicTimeout;
            _removeMusicTimer.Tick += OnRemoveMusicTimeout;
            _removeMusicTimer.Start();
            return;
        }
        ResetRemoveMusicConfirmation();
        StopPlayback(refresh: true);
        _audio = null;
        MarkDirty();
        UpdateSummary();
    }

    private void SetFormat(string format)
    {
        _cfg.Format = format;
        Fmt169.IsChecked = format == "16:9"; Fmt916.IsChecked = format == "9:16"; Fmt11.IsChecked = format == "1:1";
        SyncExportChips();
        MarkDirty();
        UpdateSummary();
        RefreshPreview();
    }

    /// <summary>Formato e resolução escolhidos para ESTA exportação (começam iguais aos do projeto).</summary>
    private (string Format, int Resolution) ExportChoice() =>
        (FormatValues[Checked(ExportFormatChips).FirstOrDefault()],
         VideoFormat.Resolutions[Checked(ExportResolutionChips).FirstOrDefault(1)]);

    private void SyncExportChips()
    {
        bool was = _loading;
        _loading = true;
        SetChips(ExportFormatChips, [Math.Max(0, Array.IndexOf(FormatValues, _cfg.Format))]);
        SetChips(ExportResolutionChips, [Math.Max(0, Array.IndexOf(VideoFormat.Resolutions.ToArray(), _cfg.Resolution))]);
        _loading = was;
    }

    private void UpdateSummary()
    {
        RemoveMusicButton.IsVisible = _audio is not null;
        SecondsWarning.IsVisible = _audio is not null && _cfg.SecondsPerPhoto is not null;
        var (format, resolution) = ExportChoice();
        var (w, h) = VideoFormat.SizeOf(format, resolution);
        ExportSummary.Text = $"Vai exportar em {format} ({w}×{h}). " +
            (_audio is null ? "Sem música." : $"Música: {Path.GetFileName(_audio)}.");
        RhythmInfo.Text = _audio is null
            ? (_cfg.SecondsPerPhoto is { } s ? $"sem música · {s} s por foto" : $"sem música · {_cfg.Bpm:0} BPM")
            : $"música: {Path.GetFileName(_audio)}";
        TimelineInfo.Text = $"{_items.Count} fotos";
    }

    // ---------- edição do slide ----------

    private PhotoSpec? Current => _selected >= 0 && _selected < _cfg.Photos.Count ? _cfg.Photos[_selected] : null;

    private void LoadSlideControls()
    {
        if (Current is not { } spec)
        {
            _texts.Clear();
            TextEditor.IsEnabled = false;
            return;
        }
        _loading = true;
        SetChips(EffectChips, [spec.Effect is { } e ? 1 + Array.IndexOf(Effects, e) : 0]);
        SetChips(TransitionChips, [spec.Transition is { } t ? 1 + (int)t : 0]);
        IntensitySlider.Value = (spec.Intensity ?? _cfg.Default.Intensity) * 100;
        LengthBox.SelectedIndex = Math.Clamp((spec.Length ?? 1) - 1, 0, 3);
        DimSlider.Value = (spec.Dim ?? 0) * 100;
        ScaleSlider.Value = (spec.Scale ?? _cfg.Default.Scale) * 100;
        ScaleValue.Text = $"{ScaleSlider.Value:0}%";
        SetChips(BackgroundChips, [spec.Background is { } bg ? 1 + (int)bg : 0]);
        LoadSlideTiming(spec);
        _loading = false;
        ReloadTexts(select: 0);
        SlideInfo.Text = $"Foto {_selected + 1} de {_cfg.Photos.Count} · {Path.GetFileName(spec.File)}";
    }

    private void EditSlide(Action<PhotoSpec> edit)
    {
        if (_loading || Current is not { } spec) return;
        edit(spec);
        _items[_selected].RefreshTags();
        MarkDirty();
        RefreshPreview();
    }

    private void OnSlideEffect(int[] on) =>
        EditSlide(s => s.Effect = on[0] == 0 ? null : Effects[on[0] - 1]);

    private void OnSlideTransition(int[] on) =>
        EditSlide(s => s.Transition = on[0] == 0 ? null : (TransitionKind)(on[0] - 1));

    /// <summary>Escolher uma cor já seleciona o fundo "Cor" (senão a escolha não teria efeito visível).</summary>
    private void SetBackgroundColor(string hex)
    {
        BackgroundColorBox.Text = hex; // dispara o TextChanged, que grava a cor
        EditDefault(() => _cfg.Default.Background = BackgroundKind.Color);
        SetChips(DefaultBackgroundChips, [(int)BackgroundKind.Color]);
    }

    private void OnSlideBackground(int[] on) =>
        EditSlide(s => s.Background = on[0] == 0 ? null : (BackgroundKind)(on[0] - 1));

    private void OnDefaultBackground(int[] on) =>
        EditDefault(() => _cfg.Default.Background = (BackgroundKind)on[0]);

    private void ResetSlide()
    {
        if (Current is not { } spec) return;
        spec.Effect = null; spec.Intensity = null; spec.Transition = null; spec.Length = null; spec.Dim = null;
        spec.Scale = null; spec.Background = null;
        spec.Seconds = null; spec.IntroSeconds = null; spec.ImageFadeIn = null;
        _items[_selected].RefreshTags();
        MarkDirty();
        LoadSlideControls();
        RefreshPreview();
    }

    private void CreateCover()
    {
        if (Current is not { } spec) return;
        spec.Length = 2;
        spec.Dim ??= 0.4;
        if (spec.Texts is null or { Count: 0 })
        {
            // capa de exemplo: três textos centralizados, um abaixo do outro, entrando em sequência com fade suave
            // (se o preset tem uma fonte padrão, a capa inteira usa ela)
            string font = ActivePreset.TextStyle?.Font ?? "Georgia";
            spec.Texts =
            [
                new SlideText { Text = "Subtítulo", Font = font, Size = 0.07, Y = 0.40, Delay = 0.3, FadeIn = 1.0, FadeOut = 0.8 },
                new SlideText { Text = "Título", Font = font, Bold = true, Size = 0.12, Color = "#FFF1CC", Y = 0.54,
                                Span = 2, Delay = 0.9, FadeIn = 1.2, FadeOut = 0.8 },
                new SlideText { Text = "Texto", Font = font, Size = 0.04, Y = 0.68, Delay = 1.5, FadeIn = 1.0, FadeOut = 0.8 },
            ];
        }
        _items[_selected].RefreshTags();
        MarkDirty();
        LoadSlideControls();
        ShowTab(1);
        RefreshPreview();
        SlideInfo.Text = "Capa criada. Troque os textos na aba Texto. Use ◀ para levar esta foto ao início.";
    }

    // ---------- padrão ----------

    private void EditDefault(Action edit)
    {
        if (_loading) return;
        edit();
        MarkDirty();
        UpdateSummary();
        RefreshPreview();
    }

    private void OnDefaultEffects(int[] on) =>
        EditDefault(() => _cfg.Default.Effects = on.Select(i => Effects[i]).ToList());

    private void OnDefaultTransition(int[] on) =>
        EditDefault(() => _cfg.Default.Transition = (TransitionKind)on[0]);

    // ---------- textos ----------

    private void ReloadTexts(int select)
    {
        bool was = _loading;
        _loading = true;
        _texts.Clear();
        foreach (var t in Current?.Texts ?? []) _texts.Add(new TextItem(t));
        if (_texts.Count > 0) TextList.SelectedIndex = Math.Clamp(select, 0, _texts.Count - 1);
        _loading = was;
        LoadTextEditor();
    }

    private SlideText? CurrentText => TextList.SelectedItem is TextItem item ? item.Text : null;

    private void LoadTextEditor()
    {
        var text = CurrentText;
        TextEditor.IsEnabled = text is not null;
        if (text is null) { UpdateTextSelection(); return; }
        if (_loading) return;
        _loading = true;
        TextContent.Text = text.Text;
        SelectFont(text.Font);
        TextSize.Value = text.Size * 100;
        TextAlign.SelectedIndex = (int)text.Align;
        TextX.Value = text.X * 100;
        TextY.Value = text.Y * 100;
        TextSpan.SelectedIndex = Math.Clamp(text.Span - 1, 0, 2);
        TextAnim.SelectedIndex = text.Animation == TextAnimation.None ? 0 : 1;
        TextAnimFields.IsEnabled = text.Animation != TextAnimation.None;
        TextDelay.Value = (decimal)text.Delay;
        TextFadeIn.Value = (decimal)(text.FadeIn ?? Math.Round(text.FadeBeats * 0.5, 1));
        TextFadeOut.Value = (decimal)(text.FadeOut ?? Math.Round(text.FadeBeats * 0.5, 1));
        TextHold.Value = (decimal)(text.Duration ?? 0);
        _loading = false;
        UpdateTextSelection();
    }

    private void AddText()
    {
        if (Current is not { } spec) return;
        (spec.Texts ??= []).Add(ActivePreset.NewText("Novo texto"));
        _items[_selected].RefreshTags();
        MarkDirty();
        ReloadTexts(select: spec.Texts.Count - 1);
        RefreshPreview();
    }

    private void RemoveText()
    {
        if (Current is not { } spec || spec.Texts is null || CurrentText is not { } text) return;
        int at = TextList.SelectedIndex;
        spec.Texts.Remove(text);
        if (spec.Texts.Count == 0) spec.Texts = null;
        _items[_selected].RefreshTags();
        MarkDirty();
        ReloadTexts(select: at);
        RefreshPreview();
    }

    /// <summary>Edita o texto selecionado sem recarregar a lista nem o editor (senão o campo perde o foco a cada letra).</summary>
    private void EditText(Action<SlideText> edit)
    {
        if (_loading || CurrentText is not { } text) return;
        edit(text);
        (TextList.SelectedItem as TextItem)?.Refresh();
        MarkDirty();
        RefreshPreview();
    }

    // ---------- prévia ----------

    private async void RefreshPreview()
    {
        if (_cfg.Photos.Count == 0 || _selected < 0 || _playing || _preparing) return;
        int generation = ++_generation;
        await Task.Delay(40); // junta várias mudanças seguidas (arrastar um controle, digitar)
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
        UpdateTextSelection();
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

    private async Task ExportOrCancelAsync()
    {
        if (_exporting) { CancelExport(); return; }
        StopPlayback(refresh: false);
        await ExportAsync();
    }

    private void CancelExport()
    {
        if (_exportCts is null) return;
        _exportCts.Cancel();
        ExportStatus.Text = "Cancelando…";
    }

    /// <summary>"45 s", "3 min 20 s" ou "1 h 05 min".</summary>
    internal static string Remaining(double seconds)
    {
        int s = (int)Math.Ceiling(Math.Max(0, seconds));
        if (s < 60) return $"{s} s";
        if (s < 3600) return $"{s / 60} min {s % 60:00} s";
        return $"{s / 3600} h {s % 3600 / 60:00} min";
    }

    private async Task ExportAsync()
    {
        if (_exporting) return;
        if (_cfg.Photos.Count == 0) { ExportStatus.Text = "O projeto não tem fotos."; return; }

        var exportChoice = ExportChoice();
        IStorageFolder? start = null;
        if (_project?.FilePath is { } projectFile && Path.GetDirectoryName(projectFile) is { } projectDir
            && !string.Equals(projectDir, ProjectStore.DefaultRoot, StringComparison.OrdinalIgnoreCase))
            start = await StorageProvider.TryGetFolderFromPathAsync(projectDir);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salvar vídeo",
            SuggestedStartLocation = start,
            SuggestedFileName = $"{_project?.Name ?? "slyden"}-{exportChoice.Format.Replace(':', 'x')}-{exportChoice.Resolution}p.mp4",
            DefaultExtension = "mp4",
            FileTypeChoices = [new FilePickerFileType("Vídeo MP4") { Patterns = ["*.mp4"] }],
        });
        if (file?.TryGetLocalPath() is not { } output) return;

        _exporting = true;
        _exportCts = new CancellationTokenSource();
        var token = _exportCts.Token;
        ExportButton.Content = "Cancelar exportação";
        ExportButton2.IsEnabled = false;
        CancelExportButton.IsVisible = true;
        ExportProgress.IsVisible = true;
        ExportProgress.Value = 0;
        ExportStatus.Text = "Renderizando…";

        var exportConfig = _cfg.Clone();
        (exportConfig.Format, exportConfig.Resolution) = exportChoice;
        var request = new RenderRequest(_folder, output, exportConfig, _audio);
        var watch = Stopwatch.StartNew();
        var progress = new Progress<RenderProgress>(p =>
        {
            if (token.IsCancellationRequested) return;
            ExportProgress.Value = p.Fraction * 100;
            string eta = "";
            // só estima depois de um pouco de trabalho feito; antes disso a conta oscila demais
            if (p.Fraction >= 0.03 && watch.Elapsed.TotalSeconds >= 5)
                eta = $" · faltam ~{Remaining(watch.Elapsed.TotalSeconds * (1 - p.Fraction) / p.Fraction)}";
            ExportStatus.Text = $"Renderizando… foto {p.Slide} de {p.Slides} ({p.Fraction * 100:0}%){eta}";
        });

        try
        {
            var summary = await Task.Run(() => SlideshowRenderer.Render(request, progress, token), token);
            ExportStatus.Text = $"Pronto: {output} ({summary.DurationSeconds:0} s de vídeo, {summary.Slides} fotos, exportado em {Remaining(watch.Elapsed.TotalSeconds)})." +
                (summary.Warnings.Count > 0 ? "\n" + string.Join("\n", summary.Warnings.Select(w => "Aviso: " + w)) : "");
        }
        catch (OperationCanceledException)
        {
            ExportStatus.Text = "Exportação cancelada. Nenhum arquivo foi criado.";
        }
        catch (Exception ex)
        {
            ExportStatus.Text = "Não foi possível exportar: " + ex.Message;
        }
        finally
        {
            _exporting = false;
            _exportCts.Dispose();
            _exportCts = null;
            ExportButton.Content = "Exportar";
            ExportButton2.IsEnabled = true;
            CancelExportButton.IsVisible = false;
            ExportProgress.IsVisible = false;
        }
    }
}
