namespace Slyden.Core.Config;

/// <summary>Um som por cima da música (ex.: a voz do noivo): entra quando uma foto aparece e a música abaixa enquanto ele toca.</summary>
public sealed class OverlaySound
{
    public string Path { get; set; } = "";
    /// <summary>Número da foto (a partir de 1) em cujo início o som começa a tocar.</summary>
    public int Photo { get; set; } = 1;
    /// <summary>Espera, em segundos, depois do início da foto.</summary>
    public double Delay { get; set; }
    /// <summary>Corte do arquivo: de onde começa a tocar (s).</summary>
    public double Start { get; set; }
    /// <summary>Corte do arquivo: onde termina (s); sem valor = até o fim do arquivo.</summary>
    public double? End { get; set; }
    /// <summary>Volume do som (1 = como está no arquivo).</summary>
    public double Volume { get; set; } = 1;
    /// <summary>Volume da música enquanto o som toca (0,3 = 30%; 1 = não abaixa).</summary>
    public double Duck { get; set; } = 0.3;
}
