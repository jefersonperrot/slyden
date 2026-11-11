using NAudio.Wave;

namespace Slyden.App;

/// <summary>Toca a música da prévia a partir de um ponto e informa quanto já tocou (relógio da prévia). Só Windows por enquanto.</summary>
public sealed class AudioPlayer : IDisposable
{
    private AudioFileReader? _reader;
    private WaveOutEvent? _output;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    /// <summary>Segundos tocados desde o <see cref="Start"/>, medidos no que já saiu pela caixa de som.</summary>
    public double Elapsed
    {
        get
        {
            if (_output is null || _reader is null) return 0;
            return _output.GetPosition() / (double)_reader.WaveFormat.AverageBytesPerSecond;
        }
    }

    /// <returns>Falso, com a mensagem em <paramref name="error"/>, se o áudio não pôde ser tocado.</returns>
    public bool Start(string path, double startSeconds, out string? error)
    {
        try
        {
            _reader = new AudioFileReader(path) { CurrentTime = TimeSpan.FromSeconds(Math.Max(0, startSeconds)) };
            _output = new WaveOutEvent { DesiredLatency = 100 };
            _output.Init(_reader);
            _output.Play();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            Dispose();
            error = ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        try { _output?.Stop(); } catch { /* já parado */ }
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
    }
}
