namespace Slyden.Core.Config;

public enum TextAlign { Left, Center, Right }

public enum TextAnimation { None, Fade }

/// <summary>
/// Um bloco de texto sobre um slide. Tamanhos e posições são proporções do quadro, para o texto
/// ter o mesmo aspecto em 16:9 e 9:16.
/// </summary>
public sealed class TextBlock
{
    /// <summary>Use "\n" para forçar quebra de linha; linhas longas quebram sozinhas.</summary>
    public string Text { get; set; } = "";
    /// <summary>Nome da fonte (instalada no Windows ou da pasta de fontes do programa).</summary>
    public string Font { get; set; } = "Arial";
    /// <summary>Altura do texto como fração do LADO MENOR do quadro (0,08 = 86 px em 16:9 e em 9:16).</summary>
    public double Size { get; set; } = 0.06;
    /// <summary>"#RRGGBB" ou "#RRGGBBAA".</summary>
    public string Color { get; set; } = "#FFFFFF";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public TextAlign Align { get; set; } = TextAlign.Center;
    /// <summary>Âncora horizontal (0 a 1 da largura): borda esquerda, centro ou borda direita, conforme <see cref="Align"/>.</summary>
    public double X { get; set; } = 0.5;
    /// <summary>Centro vertical do bloco (0 a 1 da altura).</summary>
    public double Y { get; set; } = 0.5;
    /// <summary>Largura máxima como fração da largura do quadro; acima disso quebra a linha e, se preciso, reduz a fonte.</summary>
    public double MaxWidth { get; set; } = 0.84;
    public double LineSpacing { get; set; } = 1.0;
    /// <summary>Sombra suave atrás do texto (ajuda a ler sobre fotos claras).</summary>
    public bool Shadow { get; set; } = true;
    /// <summary>Contorno escuro, como fração do tamanho da fonte (0 = sem contorno; 0,05 é discreto).</summary>
    public double Outline { get; set; }
    public TextAnimation Animation { get; set; } = TextAnimation.Fade;
    /// <summary>Duração do fade de entrada e de saída, em batidas.</summary>
    public double FadeBeats { get; set; } = 0.5;
    /// <summary>Segundos depois do início do slide até o texto começar a aparecer (0 = já no começo).</summary>
    public double Delay { get; set; }
    /// <summary>Duração do fade de entrada, em segundos; sem valor, usa <see cref="FadeBeats"/>.</summary>
    public double? FadeIn { get; set; }
    /// <summary>Duração do fade de saída, em segundos; sem valor, usa <see cref="FadeBeats"/>.</summary>
    public double? FadeOut { get; set; }
    /// <summary>Quanto tempo o texto fica na tela, em segundos, contando a partir de <see cref="Delay"/>; 0 ou vazio = até o fim da faixa.</summary>
    public double? Duration { get; set; }
    /// <summary>Em quantos slides seguidos o texto aparece (a partir deste).</summary>
    public int Span { get; set; } = 1;

    internal void Validate(string where)
    {
        if (Size is < 0.01 or > 0.5) throw new InvalidDataException($"{where}: size deve estar entre 0,01 e 0,5.");
        if (!HexColor.TryParse(Color, out _)) throw new InvalidDataException($"{where}: cor inválida \"{Color}\" (use #RRGGBB ou #RRGGBBAA).");
        if (X is < 0 or > 1 || Y is < 0 or > 1) throw new InvalidDataException($"{where}: x e y devem estar entre 0 e 1.");
        if (MaxWidth is <= 0.05 or > 1) throw new InvalidDataException($"{where}: max_width deve estar entre 0,05 e 1.");
        if (LineSpacing is < 0.5 or > 3) throw new InvalidDataException($"{where}: line_spacing deve estar entre 0,5 e 3.");
        if (Outline is < 0 or > 0.3) throw new InvalidDataException($"{where}: outline deve estar entre 0 e 0,3.");
        if (FadeBeats <= 0) throw new InvalidDataException($"{where}: fade_beats deve ser maior que zero.");
        if (Delay is < 0 or > 120) throw new InvalidDataException($"{where}: delay deve estar entre 0 e 120 segundos.");
        if (FadeIn is < 0 or > 30) throw new InvalidDataException($"{where}: fade_in deve estar entre 0 e 30 segundos.");
        if (FadeOut is < 0 or > 30) throw new InvalidDataException($"{where}: fade_out deve estar entre 0 e 30 segundos.");
        if (Duration is < 0 or > 600) throw new InvalidDataException($"{where}: duration deve estar entre 0 e 600 segundos.");
        if (Span < 1) throw new InvalidDataException($"{where}: span deve ser 1 ou mais.");
        if (string.IsNullOrWhiteSpace(Font)) throw new InvalidDataException($"{where}: font não pode ser vazio.");
    }
}
