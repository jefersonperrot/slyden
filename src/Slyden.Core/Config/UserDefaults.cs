using System.Text.Json;

namespace Slyden.Core.Config;

/// <summary>
/// Configuração padrão do usuário: o ponto de partida de todo projeto novo (efeitos, fundo, música, formato...)
/// e o estilo dos textos novos (fonte, tamanho, cor...). Fica em um arquivo no perfil do usuário.
/// </summary>
public sealed class UserDefaults
{
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

    /// <summary>Guarda o que vale para novos projetos a partir de um projeto aberto.</summary>
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

    /// <summary>Texto novo com o estilo padrão do usuário (ou o de fábrica).</summary>
    public TextBlock NewText(string content)
    {
        var text = TextStyle is null ? new TextBlock { Font = "Georgia", Size = 0.08 } : Clone(TextStyle);
        text.Text = content;
        text.X = 0.5;
        text.Y = 0.5;
        text.Span = 1;
        return text;
    }

    private static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, SlideshowConfig.Json), SlideshowConfig.Json)!;
}

/// <summary>Lê e grava a <see cref="UserDefaults"/> em um arquivo.</summary>
public sealed class UserDefaultsStore(string path)
{
    public string Path { get; } = path;

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Slyden", "defaults.json");

    public bool Exists => File.Exists(Path);

    /// <summary>Os padrões salvos; se não houver arquivo ou ele estiver estragado, os de fábrica.</summary>
    public UserDefaults Load()
    {
        try
        {
            if (!File.Exists(Path)) return new UserDefaults();
            var loaded = JsonSerializer.Deserialize<UserDefaults>(File.ReadAllText(Path), SlideshowConfig.Json) ?? new UserDefaults();
            var probe = new SlideshowConfig();
            loaded.ApplyTo(probe);
            probe.Validate();
            loaded.TextStyle?.Validate("estilo de texto padrão");
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            return new UserDefaults();
        }
    }

    public void Save(UserDefaults defaults)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        string temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(defaults, SlideshowConfig.Json));
        File.Move(temp, Path, overwrite: true);
    }

    /// <summary>Volta ao padrão de fábrica (apaga o arquivo).</summary>
    public void Reset()
    {
        if (File.Exists(Path)) File.Delete(Path);
    }
}
