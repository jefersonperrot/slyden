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
    private WriteableBitmap? _playBitmap;
    private byte[] _copyBuffer = [];
    private DispatcherTimer? _playTimer;
    private readonly Dictionary<string, (DateTime Stamp, BeatAnalysis Analysis)> _analyses = [];

    private async Task TogglePlayAsync()
    {
        if (_playing) { StopPlayback(refresh: true); return; }
        if (_preparing) return;
        await StartPlaybackAsync();
    }

    private async Task StartPlaybackAsync()
    {
        if (_project is null || _cfg.Photos.Count == 0) return;
        _preparing = true;
        PlayButton.IsEnabled = false;

        var snapshot = _cfg.Clone();
        string folder = _folder;
        string? audio = _audio;
        int startSlide = Math.Max(0, _selected);
        var (w, h) = VideoFormat.SizeOf(snapshot.Format, snapshot.Resolution);
        double scale = Math.Min(1, PlayLongSide / Math.Max(w, h));

        SlideshowPreview? preview = null;
        try
        {
            BeatAnalysis? analysis = null;
            if (audio is not null)
            {
                SlideInfo.Text = "Analisando a música…";
                analysis = await AnalyzeAsync(audio);
            }
            SlideInfo.Text = "Preparando a prévia…";
            preview = await Task.Run(() =>
            {
                var p = SlideshowPreview.Create(snapshot, folder, analysis, scale, PlayFps, null, LoadPhoto);
                p.Warm(Math.Min(startSlide, p.Plan.Slides.Count - 1));
                return p;
            });
        }
        catch (Exception ex)
        {
            SlideInfo.Text = "Não foi possível tocar a prévia: " + ex.Message;
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

        if (audio is not null)
        {
            _player = new AudioPlayer();
            if (!_player.Start(audio, plan.AudioStart + (double)_playStartFrame / PlayFps, out var audioError))
            {
                _player.Dispose();
                _player = null;
                SlideInfo.Text = "Prévia sem som: " + audioError;
            }
        }

        _clock.Restart();
        _playTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(12) };
        _playTimer.Tick -= OnPlayTick;
        _playTimer.Tick += OnPlayTick;
        _playTimer.Start();
    }

    private async Task<BeatAnalysis> AnalyzeAsync(string path)
    {
        var stamp = File.GetLastWriteTimeUtc(path);
        if (_analyses.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Analysis;
        var analysis = await Task.Run(() => BeatAnalyzer.Analyze(AudioDecoder.DecodeMono(path)));
        _analyses[path] = (stamp, analysis);
        return analysis;
    }

    private void OnPlayTick(object? sender, EventArgs e)
    {
        if (!_playing || _preview is not { } preview) return;

        // o relógio é a música (quando há): assim imagem e som não se afastam, mesmo que a tela perca quadros
        double elapsed = _player is not null ? _player.Elapsed : _clock.Elapsed.TotalSeconds;
        int frame = _playStartFrame + (int)(elapsed * PlayFps);
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
        if (!_playing && _preview is null) return;
        _playing = false;
        _playTimer?.Stop();
        _player?.Dispose();
        _player = null;
        _clock.Stop();

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
