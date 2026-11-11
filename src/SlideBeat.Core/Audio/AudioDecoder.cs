using System.Diagnostics;

namespace SlideBeat.Core.Audio;

public static class AudioDecoder
{
    /// <summary>Decodifica qualquer áudio (mp3, wav...) para amostras mono em ponto flutuante.</summary>
    public static float[] DecodeMono(string path, int sampleRate = BeatAnalyzer.SampleRate)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Arquivo de áudio não encontrado.", path);

        var psi = new ProcessStartInfo(FfmpegLocator.Find())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string a in new[] { "-loglevel", "error", "-i", path, "-vn", "-ac", "1",
                                     "-ar", sampleRate.ToString(), "-f", "f32le", "-" })
            psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o ffmpeg.");
        var errors = proc.StandardError.ReadToEndAsync();

        using var ms = new MemoryStream();
        proc.StandardOutput.BaseStream.CopyTo(ms);
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg falhou ao ler o áudio: {errors.Result.Trim()}");

        var bytes = ms.GetBuffer();
        var samples = new float[ms.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * sizeof(float));
        return samples;
    }
}
