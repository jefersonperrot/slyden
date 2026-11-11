using System.Text.Json;

namespace Slyden.Core.Config;

/// <summary>
/// Um preset: o ponto de partida de um tipo de projeto (ex.: "Casamento", "Clientes", "Telão"). Guarda efeitos, fundo,
/// sombra, música, formato, resolução e o estilo dos textos novos.
/// </summary>
public sealed class Preset
{
    /// <summary>Identifica o preset mesmo se ele for renomeado (os projetos guardam este valor).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Padrão";
    public SlideDefaults Default { get; set; } = new();
    public MusicMode MusicMode { get; set; } = MusicMode.FitMusic;
    public double AudioFadeIn { get; set; }
    public double AudioFadeOut { get; set; } = 1.5;
    public string Format { get; set; } = "16:9";
    public int Resolution { get; set; } = 1080;
    /// <summary>Estilo dos textos novos; sem valor, vale o de fábrica.</summary>
    public TextBlock? TextStyle { get; set; }

    /// <summary>Copia o que vale para um projeto novo para <paramref name="config"/>.</summary>
    public void ApplyTo(SlideshowConfig config)
    {
        config.Default = Clone(Default);
        config.MusicMode = MusicMode;
        config.AudioFadeIn = AudioFadeIn;
        config.AudioFadeOut = AudioFadeOut;
        config.Format = Format;
        config.Resolution = Resolution;
    }

    /// <summary>Guarda no preset o que está em um projeto aberto.</summary>
    public void CaptureFrom(SlideshowConfig config)
    {
        Default = Clone(config.Default);
        MusicMode = config.MusicMode;
        AudioFadeIn = config.AudioFadeIn;
        AudioFadeOut = config.AudioFadeOut;
        Format = config.Format;
        Resolution = config.Resolution;
    }

    /// <summary>Guarda o estilo de <paramref name="text"/> (menos o conteúdo, a posição e em quantos slides continua).</summary>
    public void CaptureTextStyle(TextBlock text)
    {
        var style = Clone(text);
        style.Text = "";
        style.X = 0.5;
        style.Y = 0.5;
        style.Span = 1;
        TextStyle = style;
    }

    /// <summary>Texto novo com o estilo do preset (ou o de fábrica).</summary>
    public TextBlock NewText(string content)
    {
        var text = TextStyle is null ? new TextBlock { Font = "Georgia", Size = 0.08 } : Clone(TextStyle);
        text.Text = content;
        text.X = 0.5;
        text.Y = 0.5;
        text.Span = 1;
        return text;
    }

    /// <summary>Cópia independente, com outro nome e outro Id.</summary>
    public Preset Duplicate(string name)
    {
        var copy = Clone(this);
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = name;
        return copy;
    }

    /// <summary>Volta tudo ao de fábrica, mantendo o Id e o nome.</summary>
    public void ResetToFactory()
    {
        var factory = new Preset();
        Default = factory.Default;
        MusicMode = factory.MusicMode;
        AudioFadeIn = factory.AudioFadeIn;
        AudioFadeOut = factory.AudioFadeOut;
        Format = factory.Format;
        Resolution = factory.Resolution;
        TextStyle = null;
    }

    private static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, SlideshowConfig.Json), SlideshowConfig.Json)!;
}

/// <summary>Todos os presets do usuário e qual deles é o padrão para projetos novos.</summary>
public sealed class PresetLibrary
{
    public string DefaultId { get; set; } = "";
    public List<Preset> Presets { get; set; } = [];

    public Preset Default => Find(DefaultId) ?? Presets[0];

    public Preset? Find(string? id) => id is null ? null : Presets.FirstOrDefault(p => p.Id == id);

    /// <summary>O preset do projeto; se ele foi apagado (ou o projeto é antigo), o padrão.</summary>
    public Preset For(string? id) => Find(id) ?? Default;

    public string UniqueName(string wanted)
    {
        wanted = string.IsNullOrWhiteSpace(wanted) ? "Preset" : wanted.Trim();
        string name = wanted;
        for (int n = 2; Presets.Any(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase)); n++)
            name = $"{wanted} ({n})";
        return name;
    }

    public Preset Add(string name, Preset? basedOn = null)
    {
        var preset = basedOn?.Duplicate(UniqueName(name)) ?? new Preset { Name = UniqueName(name) };
        Presets.Add(preset);
        return preset;
    }

    /// <summary>Renomeia; falso se o nome estiver vazio ou já for de outro preset.</summary>
    public bool Rename(Preset preset, string name)
    {
        name = name.Trim();
        if (name.Length == 0) return false;
        if (Presets.Any(p => p != preset && string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase))) return false;
        preset.Name = name;
        return true;
    }

    /// <summary>Apaga; falso se for o único. Se era o padrão, o primeiro da lista passa a ser.</summary>
    public bool Delete(Preset preset)
    {
        if (Presets.Count <= 1 || !Presets.Remove(preset)) return false;
        if (DefaultId == preset.Id) DefaultId = Presets[0].Id;
        return true;
    }

    internal void Normalize()
    {
        if (Presets.Count == 0) Presets.Add(new Preset());
        if (Find(DefaultId) is null) DefaultId = Presets[0].Id;
    }
}

/// <summary>Lê e grava a <see cref="PresetLibrary"/> em um arquivo.</summary>
public sealed class PresetStore(string path, string? legacyPath = null)
{
    public string Path { get; } = path;

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Slyden", "presets.json");

    /// <summary>Arquivo da versão que tinha um único padrão; vira o primeiro preset.</summary>
    public static string LegacyDefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Slyden", "defaults.json");

    public bool Exists => File.Exists(Path);

    /// <summary>Os presets salvos; se não houver arquivo ou ele estiver estragado, só o de fábrica.</summary>
    public PresetLibrary Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var library = JsonSerializer.Deserialize<PresetLibrary>(File.ReadAllText(Path), SlideshowConfig.Json) ?? new PresetLibrary();
                foreach (var p in library.Presets) Check(p);
                library.Normalize();
                return library;
            }
            if (legacyPath is not null && File.Exists(legacyPath))
            {
                var legacy = JsonSerializer.Deserialize<Preset>(File.ReadAllText(legacyPath), SlideshowConfig.Json) ?? new Preset();
                legacy.Name = "Padrão";
                Check(legacy);
                var migrated = new PresetLibrary { Presets = [legacy], DefaultId = legacy.Id };
                migrated.Normalize();
                return migrated;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
        }
        var fresh = new PresetLibrary();
        fresh.Normalize();
        return fresh;
    }

    private static void Check(Preset preset)
    {
        var probe = new SlideshowConfig();
        preset.ApplyTo(probe);
        probe.Validate();
        preset.TextStyle?.Validate($"preset {preset.Name}");
        if (string.IsNullOrWhiteSpace(preset.Name)) preset.Name = "Preset";
    }

    public void Save(PresetLibrary library)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        string temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(library, SlideshowConfig.Json));
        File.Move(temp, Path, overwrite: true);
    }
}
