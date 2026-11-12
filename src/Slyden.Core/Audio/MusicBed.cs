using System.Diagnostics;
using System.Globalization;
using Slyden.Core.Config;

namespace Slyden.Core.Audio;

/// <summary>Um trecho de uma faixa já posicionado na trilha final: de onde sai, onde entra e os fades do cruzamento.</summary>
/// <param name="SourceStart">Início do corte dentro do arquivo, já alinhado a uma batida (s).</param>
/// <param name="SourceEnd">Fim do corte dentro do arquivo (s).</param>
/// <param name="Offset">Instante da trilha final em que o trecho começa (s).</param>
/// <param name="FadeIn">Segundos de entrada gradual (cruzamento com a faixa anterior; 0 na primeira).</param>
/// <param name="FadeOut">Segundos de saída gradual (cruzamento com a próxima; 0 na última).</param>
public sealed record MusicSegment(MusicTrack Track, double SourceStart, double SourceEnd, double Offset, double FadeIn, double FadeOut)
{
    public double Length => SourceEnd - SourceStart;
    public double End => Offset + Length;
}

/// <summary>
/// A trilha de música do vídeo: as faixas do projeto cortadas, em sequência e com cruzamento (fade) entre elas.
/// Tem uma <see cref="Analysis"/> única (as batidas de cada faixa deslocadas para o tempo da trilha), então o
/// planejamento dos cortes funciona igual para uma ou várias músicas. <see cref="Render"/> grava o áudio da trilha.
/// </summary>
public sealed class MusicBed
{
    /// <summary>Duração mínima de um trecho depois do corte.</summary>
    public const double MinSegment = 1.0;

    public IReadOnlyList<MusicSegment> Segments { get; }
    public BeatAnalysis Analysis { get; }

    private MusicBed(IReadOnlyList<MusicSegment> segments, BeatAnalysis analysis)
    {
        Segments = segments;
        Analysis = analysis;
    }

    /// <summary>Uma única faixa inteira, sem corte: o próprio arquivo serve, não precisa mixar.</summary>
    public bool IsPassThrough => Segments.Count == 1 && Segments[0].SourceStart <= 0.001 && Segments[0].Track.End is null;

    public double Duration => Segments[^1].End;

    /// <param name="analyze">Análise de batidas de um arquivo (quem chama guarda em cache; é a parte lenta).</param>
    public static MusicBed Plan(IReadOnlyList<MusicTrack> tracks, double crossfade, Func<string, BeatAnalysis> analyze)
    {
        if (tracks.Count == 0) throw new ArgumentException("Nenhuma faixa de música.", nameof(tracks));
        crossfade = Math.Max(0, crossfade);

        var segments = new List<MusicSegment>();
        var beats = new List<(double Time, double Strength)>();
        var tempoByLength = new List<(double Tempo, double Length)>();
        double prevEnd = 0, prevOffset = 0;
        double prevBeatLimit = 0; // as batidas da faixa anterior valem até aqui (o meio do cruzamento)

        for (int i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            var analysis = analyze(track.Path);
            double start = Snap(analysis, track.Start, 0, analysis.DurationSeconds);
            double end = track.End is { } e && e < analysis.DurationSeconds - 0.05
                ? Snap(analysis, e, start, analysis.DurationSeconds)
                : analysis.DurationSeconds;
            if (end - start < MinSegment) end = Math.Min(analysis.DurationSeconds, start + MinSegment);
            if (end - start < MinSegment) start = Math.Max(0, end - MinSegment);
            double length = end - start;

            double offset = 0, overlap = 0;
            if (i > 0)
            {
                double prevLength = prevEnd - prevOffset;
                overlap = Math.Min(crossfade, Math.Min(prevLength, length) / 2);
                offset = prevEnd - overlap;
                if (overlap > 0)
                {
                    // a primeira batida da nova faixa cai numa batida da anterior: a grade de cortes não "tropeça" na emenda
                    var near = beats.Where(b => b.Time >= prevOffset + prevLength / 2 && b.Time <= prevEnd - overlap / 2)
                                    .Select(b => b.Time).DefaultIfEmpty(offset)
                                    .MinBy(t => Math.Abs(t - offset));
                    double aligned = prevEnd - near;
                    if (aligned >= 0.25 && aligned <= Math.Min(prevLength, length) / 2)
                    {
                        offset = near;
                        overlap = aligned;
                    }
                }
                var last = segments[^1];
                segments[^1] = last with { FadeOut = overlap };
                prevBeatLimit = offset + overlap / 2;
                beats.RemoveAll(b => b.Time >= prevBeatLimit);
            }
            segments.Add(new MusicSegment(track, start, end, offset, overlap, 0));

            for (int k = 0; k < analysis.BeatTimes.Length; k++)
            {
                double t = analysis.BeatTimes[k];
                if (t < start - 1e-9 || t >= end) continue;
                double shifted = offset + (t - start);
                if (i > 0 && shifted < prevBeatLimit) continue;
                beats.Add((shifted, analysis.BeatStrengths[k]));
            }
            tempoByLength.Add((analysis.Tempo, length));
            prevEnd = offset + length;
            prevOffset = offset;
        }

        double tempo = tempoByLength.MaxBy(t => t.Length).Tempo;
        var combined = new BeatAnalysis(tempo, beats.Select(b => b.Time).ToArray(), beats.Select(b => b.Strength).ToArray(), prevEnd);
        return new MusicBed(segments, combined);
    }

    private static double Snap(BeatAnalysis analysis, double seconds, double min, double max)
    {
        seconds = Math.Clamp(seconds, min, max);
        if (analysis.BeatTimes.Length == 0 || seconds <= 0) return seconds;
        double best = analysis.BeatTimes.MinBy(b => Math.Abs(b - seconds));
        return Math.Clamp(best, min, max);
    }

    /// <summary>
    /// Grava o áudio da trilha (WAV) em <paramref name="outputPath"/>. Se for uma só faixa inteira, devolve o
    /// próprio arquivo e não grava nada.
    /// </summary>
    public string Render(string outputPath)
    {
        if (IsPassThrough) return Segments[0].Track.Path;

        var args = new List<string> { "-y", "-loglevel", "error" };
        foreach (var s in Segments) args.AddRange(["-i", s.Track.Path]);
        args.AddRange(["-filter_complex", FilterGraph(), "-map", "[out]", "-c:a", "pcm_s16le", "-ar", "44100", "-ac", "2", outputPath]);

        var psi = new ProcessStartInfo(FfmpegLocator.Find())
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o ffmpeg.");
        string errors = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0) throw new InvalidOperationException($"ffmpeg falhou ao montar a música: {errors.Trim()}");
        return outputPath;
    }

    internal string FilterGraph()
    {
        var parts = new List<string>();
        for (int i = 0; i < Segments.Count; i++)
        {
            var s = Segments[i];
            var f = new List<string>
            {
                $"atrim=start={Inv(s.SourceStart)}:end={Inv(s.SourceEnd)}", "asetpts=PTS-STARTPTS",
                "aresample=44100", "aformat=sample_fmts=fltp:channel_layouts=stereo",
            };
            if (s.FadeIn > 0) f.Add($"afade=t=in:st=0:d={Inv(s.FadeIn)}");
            if (s.FadeOut > 0) f.Add($"afade=t=out:st={Inv(s.Length - s.FadeOut)}:d={Inv(s.FadeOut)}");
            int ms = (int)Math.Round(s.Offset * 1000);
            if (ms > 0) f.Add($"adelay={ms}|{ms}");
            parts.Add($"[{i}:a]{string.Join(',', f)}[a{i}]");
        }
        parts.Add(string.Concat(Enumerable.Range(0, Segments.Count).Select(i => $"[a{i}]")) +
                  $"amix=inputs={Segments.Count}:normalize=0:duration=longest[out]");
        return string.Join(';', parts);
    }

    private static string Inv(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
