using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using Slyden.Core.Audio;
using Slyden.Core.Config;
using Slyden.Core.Render;

namespace Slyden.App;

/// <summary>Prévia animada: toca o slideshow na própria tela (efeitos, transições, textos e música).</summary>
public partial class MainWindow
{
    private const int PlayFps = 30;
    private const double PlayLongSide = 720; // a prévia é desenhada em ~720 px para rodar em tempo real

    private bool _playing, _preparing, _frameBusy;
    private SlideshowPreview? _preview, _deferredDispose;
    private AudioPlayer? _player;
    private readonly Stopwatch _clock = new();
    private int _playStartFrame, _lastPlayFrame = -1, _playSlide = -1;
    // música atrasada (capa antes da música): a música só entra quando o relógio chega ao instante _audioDelayAt
    private int _audioOriginFrame;      // quadro do vídeo em que a música está em Elapsed = 0
    private double _audioDelayAt = -1;  // segundos do relógio em que a música deve começar (-1 = já começou ou não há)
    private string? _pendingAudio;
    private double _pendingAudioPos;
    private WriteableBitmap? _playBitmap;
    private byte[] _copyBuffer = [];
    private DispatcherTimer? _playTimer;
    private readonly Dictionary<string, (DateTime Stamp, BeatAnalysis Analysis)> _analyses = [];
    private readonly Dictionary<string, float[]> _peaks = []; // onda de cada música (mesma chave de _analyses)

    private async Task TogglePlayAsync()
    {
        if (_playing) { StopPlayback(refresh: true); return; }
        if (_preparing) return;
        await StartPlaybackAsync();
    }

    private async Task StartPlaybackAsync()
    {
        if (_project is null || _cfg.Photos.Count == 0) return;
        StopListening();
        _preparing = true;
        PlayButton.IsEnabled = false;

        var snapshot = _cfg.Clone();
        string folder = _folder;
        string? audio = _audio;
        int startSlide = Math.Max(0, _selected);
        var (w, h) = VideoFormat.SizeOf(snapshot.Format, snapshot.Resolution);
        double scale = Math.Min(1, PlayLongSide / Math.Max(w, h));

        SlideshowPreview? preview = null;
        string? mixPath = null;
        try
        {
            BeatAnalysis? analysis = null;
            if (audio is not null)
            {
                SlideInfo.Text = _cfg.Tracks.Count > 1 ? "Analisando e montando as músicas…" : "Analisando a música…";
                var music = await GetMusicAsync();
                audio = music.AudioPath;
                analysis = music.Bed.Analysis;
            }
            SlideInfo.Text = "Preparando a prévia…";
            preview = await Task.Run(() =>
            {
                var p = SlideshowPreview.Create(snapshot, folder, analysis, scale, PlayFps, null, LoadPhoto);
                p.Warm(Math.Min(startSlide, p.Plan.Slides.Count - 1));
                return p;
            });
            if (snapshot.Sounds.Count > 0)
            {
                // com sons por cima a prévia toca o MESMO áudio da exportação (música + sons, música abaixando)
                SlideInfo.Text = "Montando o áudio…";
                var previewPlan = preview.Plan;
                string? musicFile = audio;
                mixPath = await Task.Run(() =>
                {
                    var placed = AudioMix.Place(snapshot, previewPlan);
                    if (placed.Count == 0) return null;
                    string wav = Path.Combine(Path.GetTempPath(), $"slyden-previa-{Guid.NewGuid():N}.wav");
                    AudioMix.RenderWav(AudioMix.Spec(snapshot, previewPlan, musicFile, placed, firstInput: 0), wav);
                    return wav;
                });
            }
        }
        catch (Exception ex)
        {
            SlideInfo.Text = "Não foi possível tocar a prévia: " + ex.Message;
            DeletePreviewMix(mixPath);
            preview?.Dispose();
            _preparing = false;
            PlayButton.IsEnabled = true;
            return;
        }

        var plan = preview.Plan;
        _preview = preview;
        _playStartFrame = startSlide < plan.Slides.Count ? plan.Edges[startSlide] : 0;
        _lastPlayFrame = -1;
        _playSlide = -1;
        _playing = true;
        _preparing = false;
        PlayButton.IsEnabled = true;
        PlayButton.Content = "■  Parar";
        MotionSlider.IsEnabled = false;

        _audioDelayAt = -1;
        _pendingAudio = null;
        _audioOriginFrame = _playStartFrame;
        DeletePreviewMix(_previewMix);
        _previewMix = mixPath;
        if (mixPath is not null)
        {
            // o áudio montado já tem a capa (silêncio), a música e os sons: toca do ponto do vídeo em que a prévia começou
            _player = new AudioPlayer();
            if (!_player.Start(mixPath, (double)_playStartFrame / PlayFps, out var mixError))
            {
                _player.Dispose();
                _player = null;
                SlideInfo.Text = "Prévia sem som: " + mixError;
            }
        }
        else if (audio is not null)
        {
            double startTime = (double)_playStartFrame / PlayFps;
            if (plan.IntroSeconds > 0 && startTime < plan.IntroSeconds - 1e-6)
            {
                // começou dentro da capa: o relógio anda sozinho e a música entra quando a capa termina
                _pendingAudio = audio;
                _pendingAudioPos = plan.AudioStart;
                _audioOriginFrame = plan.Edges[1];
                _audioDelayAt = (plan.Edges[1] - _playStartFrame) / (double)PlayFps;
            }
            else
            {
                _player = new AudioPlayer();
                if (!_player.Start(audio, plan.AudioStart + startTime - plan.IntroSeconds, out var audioError))
                {
                    _player.Dispose();
                    _player = null;
                    SlideInfo.Text = "Prévia sem som: " + audioError;
                }
            }
        }

        _clock.Restart();
        _playTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(12) };
        _playTimer.Tick -= OnPlayTick;
        _playTimer.Tick += OnPlayTick;
        _playTimer.Start();
    }

    private string? _previewMix; // áudio montado da prévia (arquivo temporário)

    private static void DeletePreviewMix(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private readonly Dictionary<string, double> _soundLength = []; // duração dos sons por cima (só a onda, sem análise de batidas)

    /// <summary>Onda e duração de um arquivo curto (som por cima); não analisa batidas.</summary>
    private async Task<(float[] Peaks, double Length)?> LoadSoundWaveAsync(string path)
    {
        if (_peaks.TryGetValue(path, out var cached) && _soundLength.TryGetValue(path, out var length)) return (cached, length);
        if (!File.Exists(path)) return null;
        try
        {
            var (peaks, len) = await Task.Run(() =>
            {
                var samples = AudioDecoder.DecodeMono(path);
                return (Waveform.Peaks(samples, BeatAnalyzer.SampleRate), samples.Length / (double)BeatAnalyzer.SampleRate);
            });
            _peaks[path] = peaks;
            _soundLength[path] = len;
            return (peaks, len);
        }
        catch (Exception) { return null; }
    }

    private async Task<BeatAnalysis> AnalyzeAsync(string path)
    {
        var stamp = File.GetLastWriteTimeUtc(path);
        if (_analyses.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Analysis;
        var (analysis, peaks) = await Task.Run(() =>
        {
            var samples = AudioDecoder.DecodeMono(path);
            return (BeatAnalyzer.Analyze(samples), Waveform.Peaks(samples, BeatAnalyzer.SampleRate));
        });
        _analyses[path] = (stamp, analysis);
        _peaks[path] = peaks;
        return analysis;
    }

    private void OnPlayTick(object? sender, EventArgs e)
    {
        if (!_playing || _preview is not { } preview) return;

        // o relógio é a música (quando há): assim imagem e som não se afastam, mesmo que a tela perca quadros
        if (_pendingAudio is not null && _clock.Elapsed.TotalSeconds >= _audioDelayAt)
        {
            var next = new AudioPlayer();
            if (next.Start(_pendingAudio, _pendingAudioPos, out var delayedError)) _player = next;
            else { next.Dispose(); SlideInfo.Text = "Prévia sem som: " + delayedError; }
            _pendingAudio = null;
        }
        int frame = _player is not null
            ? _audioOriginFrame + (int)(_player.Elapsed * PlayFps)
            : _playStartFrame + (int)(_clock.Elapsed.TotalSeconds * PlayFps);
        if (frame >= preview.Plan.FrameCount)
        {
            StopPlayback(refresh: true);
            SlideInfo.Text = "Prévia concluída.";
            return;
        }
        if (frame == _lastPlayFrame || _frameBusy) return;
        _lastPlayFrame = frame;
        _ = RenderPlayFrameAsync(preview, frame);
    }

    private async Task RenderPlayFrameAsync(SlideshowPreview preview, int frame)
    {
        _frameBusy = true;
        try
        {
            var bitmap = await Task.Run(() => preview.RenderFrame(frame));
            if (!_playing || preview != _preview) return;

            ShowFrame(bitmap);
            int slide = preview.Plan.Frames[frame].Slide;
            if (slide != _playSlide)
            {
                _playSlide = slide;
                FollowSlide(slide, preview.Plan.Slides.Count);
            }
            PlayTime.Text = $"{Clock(frame / (double)PlayFps)} / {Clock(preview.Plan.TotalSeconds)}";
        }
        catch (Exception ex)
        {
            StopPlayback(refresh: false);
            SlideInfo.Text = "Prévia interrompida: " + ex.Message;
        }
        finally
        {
            _frameBusy = false;
            if (_deferredDispose is not null)
            {
                _deferredDispose.Dispose();
                _deferredDispose = null;
            }
        }
    }

    private static string Clock(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"m\:ss");

    private void ShowFrame(SKBitmap bitmap)
    {
        if (_playBitmap is null || _playBitmap.PixelSize.Width != bitmap.Width || _playBitmap.PixelSize.Height != bitmap.Height)
        {
            _playBitmap = new WriteableBitmap(new PixelSize(bitmap.Width, bitmap.Height), new Vector(96, 96),
                                              PixelFormat.Bgra8888, AlphaFormat.Opaque);
            PreviewImage.Source = _playBitmap;
        }

        int stride = bitmap.RowBytes, total = stride * bitmap.Height;
        if (_copyBuffer.Length < total) _copyBuffer = new byte[total];
        Marshal.Copy(bitmap.GetPixels(), _copyBuffer, 0, total);
        using (var fb = _playBitmap.Lock())
        {
            int row = Math.Min(stride, fb.RowBytes);
            for (int y = 0; y < bitmap.Height; y++)
                Marshal.Copy(_copyBuffer, y * stride, fb.Address + y * fb.RowBytes, row);
        }
        PreviewImage.InvalidateVisual();
    }

    /// <summary>A linha do tempo acompanha a foto que está tocando.</summary>
    private void FollowSlide(int slide, int count)
    {
        if (slide < 0 || slide >= _items.Count) return;
        _suspendSelection = true;
        try { SlideList.SelectedIndex = slide; }
        finally { _suspendSelection = false; }
        _selected = slide;
        SlideList.ScrollIntoView(_items[slide]);
        UpdateSelectionButtons();
        SlideInfo.Text = $"Reproduzindo · foto {slide + 1} de {count}";
    }

    private void StopPlayback(bool refresh)
    {
        StopListening();
        if (!_playing && _preview is null) return;
        _playing = false;
        _playTimer?.Stop();
        _player?.Dispose();
        _player = null;
        _pendingAudio = null;
        _clock.Stop();
        DeletePreviewMix(_previewMix);
        _previewMix = null;

        var preview = _preview;
        _preview = null;
        if (preview is not null)
        {
            if (_frameBusy) _deferredDispose = preview; // um quadro ainda está sendo desenhado: libera quando terminar
            else preview.Dispose();
        }

        PlayButton.Content = "▶  Reproduzir";
        MotionSlider.IsEnabled = true;
        PlayTime.Text = "";
        _playBitmap = null;
        if (refresh)
        {
            LoadSlideControls();
            RefreshPreview();
        }
    }
}
