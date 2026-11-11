using System.Globalization;
using System.Text.Json;
using SlideBeat.Core.Audio;

// slidebeat beats <audio> [--photos N] [--per auto|2|4|8] [--json saida.json]
if (args.Length < 2 || args[0] != "beats")
{
    Console.Error.WriteLine("Uso: slidebeat beats <audio> [--photos N] [--per auto|2|4|8] [--json saida.json]");
    return 1;
}

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

try
{
    var samples = AudioDecoder.DecodeMono(audio);
    var analysis = BeatAnalyzer.Analyze(samples);
    var plan = CutPlanner.Plan(analysis, photos, per);
    int used = Math.Min(photos, plan.CutTimes.Length - 1);

    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"música {analysis.DurationSeconds:F0}s | tempo ~{analysis.Tempo:F0} BPM | {analysis.BeatTimes.Length} batidas | " +
        $"troca a cada {plan.BeatsPerPhoto} (fase {plan.Phase}) | {used} fotos"));

    if (jsonPath is not null)
    {
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(new
        {
            tempo = analysis.Tempo,
            beats = analysis.BeatTimes,
            cuts = plan.CutTimes,
        }));
    }
    return 0;
}
catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
