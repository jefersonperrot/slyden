namespace Slyden.Core.Config;

/// <summary>Uma música do projeto. As faixas tocam em sequência, com cruzamento (fade) entre elas.</summary>
public sealed class MusicTrack
{
    public string Path { get; set; } = "";
    /// <summary>De onde a faixa começa (s); é alinhado à batida mais próxima.</summary>
    public double Start { get; set; }
    /// <summary>Onde a faixa termina (s), alinhado à batida mais próxima; sem valor = vai até o fim do arquivo.</summary>
    public double? End { get; set; }
}
