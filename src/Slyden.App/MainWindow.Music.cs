using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Slyden.Core.Audio;
using Slyden.Core.Config;
using AvaloniaText = Avalonia.Controls.TextBlock;

namespace Slyden.App;

/// <summary>Músicas do projeto: lista com corte (início/fim) e ordem, cruzamento entre elas e a trilha final (<see cref="MusicBed"/>).</summary>
public partial class MainWindow
{
    /// <summary>A trilha pronta: planejamento, análise de batidas e o arquivo de áudio a tocar/exportar.</summary>
    private sealed record MusicState(string Key, MusicBed Bed, string AudioPath, bool IsTemp);

    private MusicState? _music;
    private int _musicInfoVersion;
    private readonly List<AvaloniaText> _trackInfo = [];
    private readonly List<WaveformView> _trackWaves = [];

    private static readonly Color[] TrackColors =
        [Color.Parse("#3B8BFF"), Color.Parse("#E8A33A"), Color.Parse("#4FBF8F"), Color.Parse("#C96BDB"), Color.Parse("#E5605A"), Color.Parse("#4FB8D6")];

    private void InitMusic()
    {
        AddTrackButton.Click += async (_, _) => await AddTracksAsync();
        MusicStrip.TrackClicked += GoToTrack;
        CrossfadeValue.ValueChanged += (_, _) =>
        {
            if (_loading || _project is null) return;
            _cfg.MusicCrossfade = Math.Clamp(Num(CrossfadeValue), 0, 30);
            MarkDirty();
            _ = RefreshMusicInfoAsync();
        };
    }

    /// <summary>Clique na faixa da trilha: abre a aba Padrão, rola até a música clicada e a destaca por um instante.</summary>
    private void GoToTrack(int index)
    {
        ShowTab(2);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (index < 0 || index >= TrackList.Children.Count) return;
            var row = TrackList.Children[index];
            row.BringIntoView();
            if (row is not Border border) return;
            var normal = border.Background;
            border.Background = new SolidColorBrush(Color.Parse("#3B4A66"));
            var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                border.Background = normal;
            };
            timer.Start();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    // ---------- trilha pronta (com cache) ----------

    /// <summary>Monta a trilha: analisa as batidas de cada faixa (em cache), corta, cruza e grava o áudio se for preciso.</summary>
    private async Task<MusicState> GetMusicAsync()
    {
        var tracks = _cfg.Tracks.Select(t => new MusicTrack { Path = t.Path, Start = t.Start, End = t.End }).ToList();
        if (tracks.Count == 0) throw new InvalidOperationException("O projeto não tem música.");
        foreach (var t in tracks)
            if (!File.Exists(t.Path)) throw new FileNotFoundException($"Música não encontrada: {t.Path}");

        double crossfade = _cfg.MusicCrossfade;
        string key = string.Join('|', tracks.Select(t => $"{t.Path}@{File.GetLastWriteTimeUtc(t.Path).Ticks}@{t.Start}@{t.End}")) + $"#{crossfade}";
        if (_music is { } cached && cached.Key == key && (!cached.IsTemp || File.Exists(cached.AudioPath))) return cached;

        var analyses = new Dictionary<string, BeatAnalysis>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in tracks.Select(t => t.Path).Distinct(StringComparer.OrdinalIgnoreCase))
            analyses[path] = await AnalyzeAsync(path);

        var built = await Task.Run(() =>
        {
            var bed = MusicBed.Plan(tracks, crossfade, p => analyses[p]);
            string audio = bed.IsPassThrough
                ? bed.Segments[0].Track.Path
                : bed.Render(Path.Combine(Path.GetTempPath(), $"slyden-musica-{Guid.NewGuid():N}.wav"));
            return (bed, audio);
        });

        var state = new MusicState(key, built.bed, built.audio, !built.bed.IsPassThrough);
        DeleteTempAudio(_music);
        _music = state;
        return state;
    }

    private static void DeleteTempAudio(MusicState? state)
    {
        if (state is not { IsTemp: true }) return;
        try { File.Delete(state.AudioPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void DropMusicCache()
    {
        DeleteTempAudio(_music);
        _music = null;
        _musicInfoVersion++;
    }

    // ---------- adicionar, mover, remover ----------

    private async Task AddTracksAsync()
    {
        if (_project is null) return;
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = _cfg.Tracks.Count == 0 ? "Música do vídeo" : "Adicionar música",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Áudio") { Patterns = ["*.mp3", "*.wav", "*.m4a", "*.aac", "*.flac"] }],
        });
        var paths = picked.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0) return;
        StopPlayback(refresh: true);
        foreach (var p in paths) _cfg.Tracks.Add(new MusicTrack { Path = p });
        MarkDirty();
        OnTracksChanged();
    }

    private void MoveTrack(int index, int delta)
    {
        int to = index + delta;
        if (to < 0 || to >= _cfg.Tracks.Count) return;
        (_cfg.Tracks[index], _cfg.Tracks[to]) = (_cfg.Tracks[to], _cfg.Tracks[index]);
        MarkDirty();
        OnTracksChanged();
    }

    private void RemoveTrack(int index)
    {
        if (index < 0 || index >= _cfg.Tracks.Count) return;
        _cfg.Tracks.RemoveAt(index);
        MarkDirty();
        OnTracksChanged();
    }

    private void OnTracksChanged()
    {
        RebuildTrackList();
        UpdateSummary();
        UpdateTimingHints();
        _ = RefreshMusicInfoAsync();
    }

    // ---------- lista na tela ----------

    private void RebuildTrackList()
    {
        TrackList.Children.Clear();
        _trackInfo.Clear();
        _trackWaves.Clear();
        CrossfadeRow.IsVisible = _cfg.Tracks.Count > 1;
        MusicTimeline.Children.Clear();
        MusicTimeline.ColumnDefinitions.Clear();
        MusicTimeline.IsVisible = false;
        if (_cfg.Tracks.Count == 0) MusicStrip.IsVisible = false;
        MusicSummary.Text = _cfg.Tracks.Count == 0 ? "Nenhuma música. Clique em + Adicionar música (ou use o menu Projeto)." : "";

        for (int i = 0; i < _cfg.Tracks.Count; i++)
            TrackList.Children.Add(BuildTrackRow(i));
        if (_cfg.Tracks.Count > 0) _ = RefreshMusicInfoAsync();
    }

    private Control BuildTrackRow(int index)
    {
        var track = _cfg.Tracks[index];
        bool exists = File.Exists(track.Path);
        var swatch = new Border
        {
            Width = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(TrackColors[index % TrackColors.Length]),
        };

        var name = new AvaloniaText
        {
            Text = (exists ? "" : "⚠ ") + Path.GetFileName(track.Path),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = exists ? null : new SolidColorBrush(Color.Parse("#E8A33A")),
        };
        ToolTip.SetTip(name, exists ? track.Path : "Arquivo não encontrado: " + track.Path);

        Button Small(string text, string tip, Action click, bool enabled = true)
        {
            var b = new Button { Content = text, Padding = new Thickness(8, 2), IsEnabled = enabled };
            b.Classes.Add("btn");
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => click();
            return b;
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var listen = Small("▶ Ouvir", "Toca só o trecho escolhido desta música", () => { });
        buttons.Children.Add(listen);
        buttons.Children.Add(Small("▲", "Subir (toca antes)", () => MoveTrack(index, -1), index > 0));
        buttons.Children.Add(Small("▼", "Descer (toca depois)", () => MoveTrack(index, 1), index < _cfg.Tracks.Count - 1));
        buttons.Children.Add(Small("✕", "Tirar esta música do projeto", () => RemoveTrack(index)));

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(name);
        Grid.SetColumn(buttons, 1);
        header.Children.Add(buttons);

        NumericUpDown Field(double value) => new()
        {
            Minimum = 0, Maximum = 3600, Increment = 1, FormatString = "0.#", Value = (decimal)value,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var start = Field(track.Start);
        var end = Field(track.End ?? 0);
        ToolTip.SetTip(start, "Onde a música começa a tocar (segundos do arquivo). Ajusta-se à batida mais próxima.");
        ToolTip.SetTip(end, "Onde a música termina (segundos do arquivo). 0 = vai até o fim do arquivo.");
        var wave = new WaveformView { Accent = TrackColors[index % TrackColors.Length], Height = WaveformView.ViewHeight };
        ToolTip.SetTip(wave, "Arraste as alças brancas para escolher onde a música começa e termina. Elas grudam nas batidas.");
        _trackWaves.Add(wave);
        FillWave(wave, track);
        wave.SetRange(track.Start, track.End ?? 0);
        listen.Click += (_, _) => ToggleListen(track, wave, listen);
        wave.Dragging += (s, e) => SetFields(start, end, s, e, wave.Duration);
        wave.Committed += (s, e) =>
        {
            if (_loading || _project is null) return;
            track.Start = s;
            track.End = e >= wave.Duration - 0.05 ? null : e;
            MarkDirty();
            _ = RefreshMusicInfoAsync();
        };
        start.ValueChanged += (_, _) => OnTrackRangeChanged(track, start, end, wave);
        end.ValueChanged += (_, _) => OnTrackRangeChanged(track, start, end, wave);

        // um campo por linha: no painel estreito, dois lado a lado não deixavam espaço para ver o número
        var range = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 8, RowSpacing = 6 };
        AvaloniaText Label(string t) { var l = new AvaloniaText { Text = t, VerticalAlignment = VerticalAlignment.Center }; l.Classes.Add("dim"); return l; }
        var startLabel = Label("Início (s)");
        var endLabel = Label("Fim (s), 0 = até o fim");
        Grid.SetRow(endLabel, 1);
        Grid.SetColumn(start, 1);
        Grid.SetRow(end, 1);
        Grid.SetColumn(end, 1);
        range.Children.Add(startLabel);
        range.Children.Add(start);
        range.Children.Add(endLabel);
        range.Children.Add(end);
        if (index > 0)
        {
            // música por trecho: em que foto esta música entra (as fotos de cada trecho se espalham só pela música dele)
            range.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var photoLabel = Label("Entra na foto nº (0 = auto)");
            var photo = new NumericUpDown
            {
                Minimum = 0, Maximum = 9999, Increment = 1, FormatString = "0", Value = track.StartPhoto ?? 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            ToolTip.SetTip(photo, "Número da foto em que esta música começa. As fotos de cada trecho se espalham só pela música dele, e a troca de música cai na troca de foto. 0 = automático (proporcional ao tempo).");
            photo.ValueChanged += (_, _) =>
            {
                if (_loading || _project is null) return;
                int v = (int)Num(photo);
                track.StartPhoto = v > 0 ? v : null;
                MarkDirty();
                _ = RefreshMusicInfoAsync();
            };
            Grid.SetRow(photoLabel, 2);
            Grid.SetRow(photo, 2);
            Grid.SetColumn(photo, 1);
            range.Children.Add(photoLabel);
            range.Children.Add(photo);
        }

        var info = new AvaloniaText { TextWrapping = TextWrapping.Wrap };
        info.Classes.Add("hint");
        _trackInfo.Add(info);

        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(header);
        body.Children.Add(wave);
        body.Children.Add(range);
        body.Children.Add(info);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        row.Children.Add(swatch);
        Grid.SetColumn(body, 1);
        row.Children.Add(body);
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#26262A")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Child = row,
        };
    }

    // ---------- ouvir só o trecho de uma música ----------

    private AudioPlayer? _listenPlayer;
    private WaveformView? _listenWave;
    private Button? _listenButton;
    private double _listenFrom, _listenLength;
    private Avalonia.Threading.DispatcherTimer? _listenTimer;

    private void ToggleListen(MusicTrack track, WaveformView wave, Button button) =>
        ToggleListen(track.Path, track.Start, track.End, wave, button);

    /// <summary>Toca só o trecho mostrado na onda (ou, se a onda ainda não carregou, o início/fim guardados).</summary>
    private void ToggleListen(string path, double fallbackStart, double? fallbackEnd, WaveformView wave, Button button)
    {
        bool same = _listenButton == button;
        StopListening();
        if (same) return;
        if (!File.Exists(path)) { MusicSummary.Text = "⚠ Arquivo não encontrado: " + path; return; }

        StopPlayback(refresh: true); // a prévia do vídeo e a escuta não tocam juntas
        double from = wave.Duration > 0 ? wave.Start : fallbackStart;
        double to = wave.Duration > 0 ? wave.End : (fallbackEnd ?? from + 30);
        var player = new AudioPlayer();
        if (!player.Start(path, from, out var error))
        {
            player.Dispose();
            MusicSummary.Text = "Não foi possível tocar: " + error;
            return;
        }
        (_listenPlayer, _listenWave, _listenButton) = (player, wave, button);
        (_listenFrom, _listenLength) = (from, Math.Max(0.5, to - from));
        button.Content = "■ Parar";
        _listenTimer ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _listenTimer.Tick -= OnListenTick;
        _listenTimer.Tick += OnListenTick;
        _listenTimer.Start();
    }

    private void OnListenTick(object? sender, EventArgs e)
    {
        if (_listenPlayer is not { } player) { StopListening(); return; }
        double played = player.Elapsed;
        if (played >= _listenLength) { StopListening(); return; }
        if (_listenWave is { } wave) wave.Playhead = _listenFrom + played;
    }

    private void StopListening()
    {
        _listenTimer?.Stop();
        _listenPlayer?.Dispose();
        _listenPlayer = null;
        if (_listenWave is { } wave) wave.Playhead = -1;
        if (_listenButton is { } button) button.Content = "▶ Ouvir";
        _listenWave = null;
        _listenButton = null;
    }

    /// <summary>Passa a onda e as batidas da música (se já foram analisadas) para o desenho.</summary>
    private void FillWave(WaveformView wave, MusicTrack track)
    {
        if (_peaks.TryGetValue(track.Path, out var peaks) && _analyses.TryGetValue(track.Path, out var a))
            wave.SetAudio(peaks, a.Analysis.BeatTimes, a.Analysis.DurationSeconds);
    }

    private void SetFields(NumericUpDown start, NumericUpDown end, double s, double e, double duration)
    {
        bool was = _loading;
        _loading = true;
        start.Value = (decimal)Math.Round(s, 2);
        end.Value = e >= duration - 0.05 ? 0 : (decimal)Math.Round(e, 2);
        _loading = was;
    }

    private void OnTrackRangeChanged(MusicTrack track, NumericUpDown start, NumericUpDown end, WaveformView wave)
    {
        if (_loading || _project is null) return;
        double s = Num(start), e = Num(end);
        // o fim precisa ficar depois do início (0 = até o fim do arquivo); senão o arquivo do projeto não abriria mais
        if (e > 0 && e < s + MusicBed.MinSegment)
        {
            e = s + MusicBed.MinSegment;
            bool was = _loading;
            _loading = true;
            end.Value = (decimal)e;
            _loading = was;
        }
        track.Start = s;
        track.End = e > 0 ? e : null;
        wave.SetRange(s, e);
        MarkDirty();
        _ = RefreshMusicInfoAsync();
    }

    // ---------- informações e linha do tempo ----------

    /// <summary>Recalcula o que cada música vai usar e onde cada uma toca; mostra em texto e na barra de tempo.</summary>
    private async Task RefreshMusicInfoAsync()
    {
        int version = ++_musicInfoVersion;
        if (_cfg.Tracks.Count == 0) return;
        try
        {
            var music = await GetMusicAsync();
            if (version != _musicInfoVersion || music.Bed.Segments.Count != _trackInfo.Count) return;

            var segments = music.Bed.Segments;
            int photoCount = _cfg.Photos.Count;
            int skip = photoCount > 1 && _cfg.Photos[0].IntroSeconds is > 0 ? 1 : 0;
            var firstSlides = MusicSections.FirstSlides(_cfg.Tracks, music.Bed.Analysis, photoCount - skip, skip);
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                string photos = firstSlides is null ? "" :
                    $" · fotos {(i == 0 ? 1 : firstSlides[i] + skip + 1)} a {(i + 1 < firstSlides.Length ? firstSlides[i + 1] + skip : photoCount)}";
                double full = _analyses.TryGetValue(s.Track.Path, out var a) ? a.Analysis.DurationSeconds : s.SourceEnd;
                if (i < _trackWaves.Count)
                {
                    FillWave(_trackWaves[i], s.Track);
                    _trackWaves[i].SetRange(s.SourceStart, s.SourceEnd); // o trecho já ajustado às batidas
                }
                _trackInfo[i].Text = $"Arquivo de {Clock(full)} · usa de {Clock(s.SourceStart)} a {Clock(s.SourceEnd)} ({Clock(s.Length)}) · " +
                                     $"toca de {Clock(s.Offset)} a {Clock(s.End)} da trilha{photos}";
            }
            MusicSummary.Text = segments.Count == 1
                ? $"Trilha de {Clock(music.Bed.Duration)}. Início e fim se ajustam à batida mais próxima."
                : $"Trilha de {Clock(music.Bed.Duration)} com {segments.Count} músicas em sequência, com cruzamento de {Math.Min(_cfg.MusicCrossfade, 30):0.#} s. " +
                  "Início e fim de cada uma se ajustam à batida; a música seguinte entra numa batida da anterior." +
                  (firstSlides is null
                      ? " Para dividir as fotos entre as músicas, escolha em que foto cada uma entra."
                      : " As fotos estão divididas entre as músicas: cada grupo se espalha só pela música dele.");
            DrawMusicTimeline(music.Bed);
            MusicStrip.Set(music.Bed, p => _peaks.TryGetValue(p, out var peaks) ? peaks : null, TrackColors);
            MusicStrip.IsVisible = true;
        }
        catch (Exception ex)
        {
            if (version != _musicInfoVersion) return;
            MusicSummary.Text = "⚠ " + ex.Message;
            MusicTimeline.IsVisible = false;
            MusicStrip.IsVisible = false;
        }
    }

    /// <summary>Barra com um bloco por música, proporcional à duração, para ver onde uma acaba e a outra começa.</summary>
    private void DrawMusicTimeline(MusicBed bed)
    {
        MusicTimeline.Children.Clear();
        MusicTimeline.ColumnDefinitions.Clear();
        var segments = bed.Segments;
        for (int i = 0; i < segments.Count; i++)
        {
            // cada bloco vai até onde a próxima música começa; a última, até o fim da trilha
            double width = (i + 1 < segments.Count ? segments[i + 1].Offset : bed.Duration) - segments[i].Offset;
            MusicTimeline.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(width, 0.1), GridUnitType.Star));
            var color = TrackColors[i % TrackColors.Length];
            var block = new Border
            {
                Background = new SolidColorBrush(color, 0.85),
                Height = 26,
                Margin = new Thickness(i == 0 ? 0 : 1, 0, 0, 0),
                CornerRadius = new CornerRadius(i == 0 ? 4 : 0, i == segments.Count - 1 ? 4 : 0, i == segments.Count - 1 ? 4 : 0, i == 0 ? 4 : 0),
                Child = new AvaloniaText
                {
                    Text = $"{i + 1}", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            ToolTip.SetTip(block, $"{i + 1}. {Path.GetFileNameWithoutExtension(segments[i].Track.Path)} · {Clock(segments[i].Offset)} a {Clock(segments[i].End)}");
            Grid.SetColumn(block, i);
            MusicTimeline.Children.Add(block);
        }
        MusicTimeline.IsVisible = true;
    }
}
