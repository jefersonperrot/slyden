namespace Slyden.Core.Config;

/// <summary>Formatos de saída: 16:9 (YouTube), 9:16 (Reels/Stories) e 1:1 (feed).</summary>
public static class VideoFormat
{
    public static bool IsKnown(string format) => format is "16:9" or "9:16" or "1:1";

    public static (int Width, int Height) SizeOf(string format) => format switch
    {
        "16:9" => (1920, 1080),
        "9:16" => (1080, 1920),
        "1:1" => (1080, 1080),
        _ => throw new ArgumentException($"Formato desconhecido: {format}", nameof(format)),
    };
}
