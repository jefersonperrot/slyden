namespace Slyden.Core.Config;

/// <summary>Formatos de saída: 16:9 (YouTube), 9:16 (Reels/Stories) e 1:1 (feed); resolução = lado menor do vídeo.</summary>
public static class VideoFormat
{
    /// <summary>Lados menores aceitos (720 = HD, 1080 = Full HD, 1440 = 2K, 2160 = 4K).</summary>
    public static readonly int[] Resolutions = [720, 1080, 1440, 2160];

    public static bool IsKnown(string format) => format is "16:9" or "9:16" or "1:1";

    public static bool IsKnownResolution(int resolution) => Resolutions.Contains(resolution);

    public static (int Width, int Height) SizeOf(string format, int resolution = 1080)
    {
        if (!IsKnownResolution(resolution))
            throw new ArgumentException($"Resolução desconhecida: {resolution}", nameof(resolution));
        int longSide = Even((int)Math.Round(resolution * 16 / 9.0));
        return format switch
        {
            "16:9" => (longSide, resolution),
            "9:16" => (resolution, longSide),
            "1:1" => (resolution, resolution),
            _ => throw new ArgumentException($"Formato desconhecido: {format}", nameof(format)),
        };
    }

    private static int Even(int value) => (value + 1) / 2 * 2; // yuv420p exige medidas pares
}
