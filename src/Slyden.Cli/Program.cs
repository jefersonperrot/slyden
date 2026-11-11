using System.Globalization;
using System.Text.Json;
using Slyden.Core.Audio;
using Slyden.Core.Config;
using Slyden.Core.Render;

const string Usage = """
    Uso:
      slyden beats  <audio> [--photos N] [--per auto|2|4|8] [--json saida.json]
      slyden render <pasta_fotos> <saida.mp4> [--audio musica.mp3] [--config config.json] [--format 16:9|9:16|1:1] [--fonts pasta]

    Sem --audio, o vídeo sai mudo e usa o "bpm" da configuração.
    Se o --config indicado não existir, é criado com os padrões para você editar.
    """;

if (args.Length < 2 || args[0] is not ("beats" or "render"))
{
    Console.Error.WriteLine(Usage);
    return 1;
}

try
{
    return args[0] == "beats" ? Beats(args) : Render(args);
}
catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException
                              or InvalidDataException or ArgumentException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelado.");
    return 130;
}

static int Beats(string[] args)
{
    string audio = args[1];
    int photos = 30;
    int? per = null;
    string? jsonPath = null;
    for (int i = 2; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--photos": photos = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
            case "--per": { string v = args[++i]; per = v == "auto" ? null : int.Parse(v, CultureInfo.InvariantCulture); break; }
            case "--json": jsonPath = args[++i]; break;
            default: Console.Error.WriteLine($"Opção desconhecida: {args[i]}"); return 1;
        }
    }

    var analysis = BeatAnalyzer.Analyze(AudioDecoder.DecodeMono(audio));
    var plan = CutPlanner.Plan(analysis, photos, per);
    int used = Math.Min(photos, plan.CutTimes.Length - 1);

    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"música {analysis.DurationSeconds:F0}s | tempo ~{analysis.Tempo:F0} BPM | {analysis.BeatTimes.Length} batidas | " +
        $"troca a cada {plan.BeatsPerPhoto} (fase {plan.Phase}) | {used} fotos"));

    if (jsonPath is not null)
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(new
        {
            tempo = analysis.Tempo,
            beats = analysis.BeatTimes,
            cuts = plan.CutTimes,
        }));
    return 0;
}

static int Render(string[] args)
{
    if (args.Length < 3) { Console.Error.WriteLine(Usage); return 1; }
    string folder = args[1], output = args[2];
    string? audio = null, configPath = null, format = null;
    var fontFolders = new List<string>();
    for (int i = 3; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--audio": audio = args[++i]; break;
            case "--fonts": fontFolders.Add(args[++i]); break;
            case "--config": configPath = args[++i]; break;
            case "--format": format = args[++i]; break;
            default: Console.Error.WriteLine($"Opção desconhecida: {args[i]}"); return 1;
        }
    }

    if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Pasta de fotos não encontrada: {folder}");

    SlideshowConfig config;
    if (configPath is not null && File.Exists(configPath))
    {
        config = SlideshowConfig.Load(configPath);
    }
    else
    {
        config = SlideshowConfig.CreateFor(folder);
        if (configPath is not null)
        {
            config.Save(configPath);
            Console.WriteLine($"Configuração criada: {configPath}");
        }
    }
    if (format is not null) config.Format = format;

    int lastPercent = -1;
    var progress = new Progress<RenderProgress>(p =>
    {
        int percent = (int)(p.Fraction * 100);
        if (percent == lastPercent) return;
        lastPercent = percent;
        Console.Write($"\rrenderizando... {percent,3}%  (foto {p.Slide}/{p.Slides})");
    });

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    var summary = SlideshowRenderer.Render(
        new RenderRequest(folder, output, config, audio, FontFolders: fontFolders), progress, cts.Token);
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"\rok: {output} | {summary.DurationSeconds:F0}s | {summary.Slides} fotos | tempo ~{summary.Tempo:F0} BPM | troca a cada {summary.BeatsPerPhoto} batidas   "));
    foreach (var warning in summary.Warnings) Console.WriteLine($"aviso: {warning}");
    return 0;
}
