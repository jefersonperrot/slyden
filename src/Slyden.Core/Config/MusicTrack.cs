namespace Slyden.Core.Config;

/// <summary>Uma música do projeto. As faixas tocam em sequência, com cruzamento (fade) entre elas.</summary>
public sealed class MusicTrack
{
    public string Path { get; set; } = "";
    /// <summary>De onde a faixa começa (s); é alinhado à batida mais próxima.</summary>
    public double Start { get; set; }
    /// <summary>Onde a faixa termina (s), alinhado à batida mais próxima; sem valor = vai até o fim do arquivo.</summary>
    public double? End { get; set; }
    /// <summary>
    /// Número da foto (a partir de 1) em que esta música entra. Vale da segunda música em diante; vazio = automático
    /// (proporcional ao tempo). Se alguma música tiver um valor, as fotos de cada trecho se espalham só pela música dele.
    /// </summary>
    public int? StartPhoto { get; set; }
}
