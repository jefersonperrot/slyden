using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slyden.Core.Config;

public enum EffectKind { None, ZoomIn, ZoomOut, PanLr, PanRl, TiltUp, TiltDown }

public enum TransitionKind { Cut, Fade }

public enum EffectOrder { Cycle, Random }

/// <summary>Padrões que valem para todas as fotos, salvo exceção em <see cref="PhotoSpec"/>.</summary>
public sealed class SlideDefaults
{
    /// <summary>Um item = mesmo efeito em todas; vários = alternam conforme <see cref="Order"/>.</summary>
    public List<EffectKind> Effects { get; set; } = [EffectKind.ZoomIn, EffectKind.ZoomOut, EffectKind.PanLr, EffectKind.PanRl];
    public EffectOrder Order { get; set; } = EffectOrder.Cycle;
    /// <summary>0,05 sutil ... 0,3 forte.</summary>
    public double Intensity { get; set; } = 0.15;
    public TransitionKind Transition { get; set; } = TransitionKind.Cut;
    /// <summary>Duração do fade, em batidas.</summary>
    public double FadeBeats { get; set; } = 0.5;
}

/// <summary>Uma foto e, opcionalmente, o que difere do padrão.</summary>
public sealed class PhotoSpec
{
    public string File { get; set; } = "";
    public EffectKind? Effect { get; set; }
    public double? Intensity { get; set; }
    /// <summary>Transição ao ENTRAR nesta foto.</summary>
    public TransitionKind? Transition { get; set; }
    /// <summary>Avançado: zoom inicial e final.</summary>
    public double[]? Zoom { get; set; }
    /// <summary>Avançado: centro inicial e final, cada um [x, y] de 0 a 1.</summary>
    public double[][]? Center { get; set; }
    /// <summary>Duração em "ciclos" normais de slide (2 = o dobro do tempo; útil para a capa). Padrão 1.</summary>
    public int? Length { get; set; }
    /// <summary>Escurece a foto (0 a 1) para o texto ler melhor.</summary>
    public double? Dim { get; set; }
    /// <summary>Textos sobre este slide.</summary>
    public List<TextBlock>? Texts { get; set; }
}

public sealed class SlideshowConfig
{
    /// <summary>Usado só quando não há música.</summary>
    public double Bpm { get; set; } = 120;
    /// <summary>Troca a cada N batidas; nulo = automático ("auto" no arquivo).</summary>
    [JsonConverter(typeof(AutoOrIntConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] // senão o "auto" some do arquivo salvo
    public int? BeatsPerPhoto { get; set; }
    /// <summary>Só sem música: segundos por foto (ex.: telão). Se preenchido, vale no lugar de bpm/beats_per_photo.</summary>
    public double? SecondsPerPhoto { get; set; }
    /// <summary>Só sem música e com seconds_per_photo: duração do fade em segundos (padrão 1).</summary>
    public double? FadeSeconds { get; set; }
    /// <summary>"16:9", "9:16" ou "1:1".</summary>
    public string Format { get; set; } = "16:9";
    public SlideDefaults Default { get; set; } = new();
    public List<PhotoSpec> Photos { get; set; } = [];

    private static readonly JsonSerializerOptions Json = CreateOptions();

    private static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static SlideshowConfig Load(string path)
    {
        try
        {
            var cfg = JsonSerializer.Deserialize<SlideshowConfig>(File.ReadAllText(path), Json)
                      ?? throw new InvalidDataException("Arquivo de configuração vazio.");
            cfg.Validate();
            return cfg;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Configuração inválida ({path}): {ex.Message}", ex);
        }
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Json));

    /// <summary>Configuração inicial para as fotos .jpg/.jpeg de uma pasta, em ordem alfabética.</summary>
    public static SlideshowConfig CreateFor(string photosFolder)
    {
        var files = Directory.EnumerateFiles(photosFolder)
            .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Order(StringComparer.OrdinalIgnoreCase);
        return new SlideshowConfig { Photos = files.Select(f => new PhotoSpec { File = f! }).ToList() };
    }

    public void Validate()
    {
        if (!VideoFormat.IsKnown(Format))
            throw new InvalidDataException($"Formato desconhecido: \"{Format}\" (use \"16:9\", \"9:16\" ou \"1:1\").");
        if (SecondsPerPhoto is <= 0) throw new InvalidDataException("seconds_per_photo deve ser maior que zero.");
        if (FadeSeconds is <= 0) throw new InvalidDataException("fade_seconds deve ser maior que zero.");
        if (Bpm <= 0) throw new InvalidDataException("bpm deve ser maior que zero.");
        if (BeatsPerPhoto is < 1) throw new InvalidDataException("beats_per_photo deve ser 1 ou mais, ou \"auto\".");
        if (Default.Effects.Count == 0) throw new InvalidDataException("default.effects não pode ser vazio.");
        if (Default.Intensity is < 0 or > 1) throw new InvalidDataException("default.intensity deve estar entre 0 e 1.");
        if (Default.FadeBeats <= 0) throw new InvalidDataException("default.fade_beats deve ser maior que zero.");
        foreach (var p in Photos)
        {
            if (string.IsNullOrWhiteSpace(p.File)) throw new InvalidDataException("Foto sem nome de arquivo.");
            if (p.Intensity is < 0 or > 1) throw new InvalidDataException($"{p.File}: intensity deve estar entre 0 e 1.");
            if (p.Zoom is { Length: not 2 }) throw new InvalidDataException($"{p.File}: zoom deve ter 2 valores [inicial, final].");
            if (p.Zoom is { } z && z.Any(v => v < 1)) throw new InvalidDataException($"{p.File}: zoom não pode ser menor que 1.");
            if (p.Center is { } c && (c.Length != 2 || c.Any(pt => pt.Length != 2)))
                throw new InvalidDataException($"{p.File}: center deve ser [[x,y],[x,y]].");
            if (p.Length is < 1 or > 16) throw new InvalidDataException($"{p.File}: length deve estar entre 1 e 16.");
            if (p.Dim is < 0 or > 1) throw new InvalidDataException($"{p.File}: dim deve estar entre 0 e 1.");
            for (int i = 0; i < (p.Texts?.Count ?? 0); i++) p.Texts![i].Validate($"{p.File}, texto {i + 1}");
        }
    }

    private sealed class AutoOrIntConverter : JsonConverter<int?>
    {
        public override bool HandleNull => true;

        public override int? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.Number => reader.GetInt32(),
                JsonTokenType.String when reader.GetString() == "auto" => null,
                _ => throw new JsonException("beats_per_photo deve ser um número ou \"auto\"."),
            };

        public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        {
            if (value is { } v) writer.WriteNumberValue(v);
            else writer.WriteStringValue("auto");
        }
    }
}
