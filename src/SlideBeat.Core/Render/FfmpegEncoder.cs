using System.Diagnostics;

namespace SlideBeat.Core.Render;

/// <summary>Processo do ffmpeg que recebe os quadros crus (BGRA) pela entrada padrão.</summary>
internal sealed class FfmpegEncoder : IDisposable
{
    private readonly Process _process;
    private readonly Stream _input;
    private readonly Task<string> _errors;
    private bool _finished;

    public FfmpegEncoder(string ffmpegPath, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o ffmpeg.");
        _input = _process.StandardInput.BaseStream;
        _errors = _process.StandardError.ReadToEndAsync();
    }

    public void WriteFrame(ReadOnlySpan<byte> frame)
    {
        try { _input.Write(frame); }
        catch (IOException) when (_process.HasExited)
        {
            throw new InvalidOperationException($"O ffmpeg encerrou durante a gravação: {_errors.GetAwaiter().GetResult().Trim()}");
        }
    }

    public void Finish()
    {
        _input.Close();
        _process.WaitForExit();
        _finished = true;
        if (_process.ExitCode != 0)
            throw new InvalidOperationException($"O ffmpeg falhou: {_errors.GetAwaiter().GetResult().Trim()}");
    }

    public void Dispose()
    {
        if (!_finished && !_process.HasExited) _process.Kill(entireProcessTree: true);
        _process.Dispose();
    }
}
