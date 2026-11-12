using System.Diagnostics;
using System.Globalization;
using Slyden.Core.Config;
using Slyden.Core.Render;

namespace Slyden.Core.Audio;

/// <summary>Um som por cima já com hora marcada no vídeo.</summary>
/// <param name="At">Instante do vídeo (s) em que o som começa.</param>
/// <param name="Length">Quanto ele toca (s), já cortado para caber no vídeo.</param>
public sealed record SoundPlacement(OverlaySound Sound, double At, double Length)
{
    public double End => At + Length;
}

/// <summary>Entradas e filtros do ffmpeg que montam o áudio final: música (com capa, fades) + sons por cima, com a música abaixando.</summary>
/// <param name="Inputs">Argumentos "-ss .. -t .. -i arquivo" de cada entrada, na ordem dos índices usados no grafo.</param>
public sealed record MixSpec(IReadOnlyList<string> Inputs, string Graph, double Total);

public static class AudioMix
{
    /// <summary>Tempo que a música leva para abaixar antes do som e voltar depois dele.</summary>
    public const double Ramp = 0.35;

    /// <summary>Quando cada som toca: no início da foto escolhida (+ espera). Ignora fotos que não entraram no vídeo.</summary>
    public static IReadOnlyList<SoundPlacement> Place(SlideshowConfig cfg, SlideshowPlan plan, Func<string, double>? duration = null)
    {
        duration ??= AudioDecoder.Duration;
        var result = new List<SoundPlacement>();
        foreach (var sound in cfg.Sounds)
        {
            int slide = sound.Photo - 1;
            if (slide < 0 || slide >= plan.Slides.Count) continue;
            if (!File.Exists(sound.Path)) throw new FileNotFoundException($"Som não encontrado: {sound.Path}", sound.Path);

            double at = plan.Edges[slide] / (double)plan.Fps + sound.Delay;
            double fileLength = duration(sound.Path);
            double length = Math.Min(sound.End ?? fileLength, fileLength) - sound.Start;
            length = Math.Min(length, plan.TotalSeconds - at); // o que passa do fim do vídeo é cortado
            if (length < 0.05) continue;
            result.Add(new SoundPlacement(sound, at, length));
        }
        return result;
    }

    /// <param name="music">Arquivo da música (ou da trilha montada); nulo = só os sons.</param>
    /// <param name="firstInput">Índice da primeira entrada de áudio (1 quando o vídeo entra pelo índice 0).</param>
    public static MixSpec Spec(SlideshowConfig cfg, SlideshowPlan plan, string? music, IReadOnlyList<SoundPlacement> sounds, int firstInput)
    {
        double total = plan.TotalSeconds;
        double delay = plan.IntroSeconds; // com capa antes da música, a música é atrasada
        double musicTotal = Math.Max(0.1, total - delay);
        var inputs = new List<string>();
        var chains = new List<string>();
        var labels = new List<string>();

        if (music is not null)
        {
            int i = firstInput + labels.Count;
            inputs.AddRange(["-ss", Inv(plan.AudioStart), "-t", Inv(musicTotal), "-i", music]);
            var f = new List<string> { "aresample=44100", "aformat=sample_fmts=fltp:channel_layouts=stereo" };
            double fadeIn = Math.Min(cfg.AudioFadeIn, musicTotal), fadeOut = Math.Min(cfg.AudioFadeOut, musicTotal);
            if (fadeIn > 0) f.Add($"afade=t=in:st=0:d={Inv(fadeIn)}");
            if (fadeOut > 0) f.Add($"afade=t=out:st={Inv(musicTotal - fadeOut)}:d={Inv(fadeOut)}");
            if (delay > 0) f.Add($"adelay={(int)Math.Round(delay * 1000)}:all=1");
            string? duck = DuckExpression(sounds);
            if (duck is not null) f.Add($"volume='{duck}':eval=frame");
            chains.Add($"[{i}:a]{string.Join(',', f)}[m]");
            labels.Add("[m]");
        }

        for (int k = 0; k < sounds.Count; k++)
        {
            var s = sounds[k];
            int i = firstInput + labels.Count;
            inputs.AddRange(["-ss", Inv(s.Sound.Start), "-t", Inv(s.Length), "-i", s.Sound.Path]);
            int ms = (int)Math.Round(s.At * 1000);
            var f = new List<string> { "aresample=44100", "aformat=sample_fmts=fltp:channel_layouts=stereo", $"volume={Inv(s.Sound.Volume)}" };
            if (ms > 0) f.Add($"adelay={ms}|{ms}");
            chains.Add($"[{i}:a]{string.Join(',', f)}[s{k}]");
            labels.Add($"[s{k}]");
        }

        chains.Add(string.Concat(labels) + $"amix=inputs={labels.Count}:normalize=0:duration=longest,alimiter=limit=0.95[out]");
        return new MixSpec(inputs, string.Join(';', chains), total);
    }

    /// <summary>Fator de volume da música ao longo do tempo: abaixa para <c>Duck</c> durante cada som, com rampas suaves.</summary>
    internal static string? DuckExpression(IReadOnlyList<SoundPlacement> sounds)
    {
        var terms = sounds.Where(s => s.Sound.Duck < 0.999).Select(s =>
        {
            double a = s.At, b = s.End;
            // 1 - (1 - duck) * clamp(min((t - (a - rampa)) / rampa, ((b + rampa) - t) / rampa), 0, 1)
            return $"(1-{Inv(1 - s.Sound.Duck)}*max(0,min(1,min((t-({Inv(a - Ramp)}))/{Inv(Ramp)},(({Inv(b + Ramp)})-t)/{Inv(Ramp)}))))";
        }).ToList();
        return terms.Count == 0 ? null : string.Join('*', terms);
    }

    /// <summary>Grava o áudio final (WAV) para a prévia tocar o mesmo que será exportado.</summary>
    public static void RenderWav(MixSpec spec, string outputPath)
    {
        var args = new List<string> { "-y", "-loglevel", "error" };
        args.AddRange(spec.Inputs);
        args.AddRange(["-filter_complex", spec.Graph, "-map", "[out]", "-t", Inv(spec.Total), "-c:a", "pcm_s16le", "-ar", "44100", "-ac", "2", outputPath]);
        var psi = new ProcessStartInfo(FfmpegLocator.Find()) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o ffmpeg.");
        string errors = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0) throw new InvalidOperationException($"ffmpeg falhou ao montar o áudio: {errors.Trim()}");
    }

    internal static string Inv(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
