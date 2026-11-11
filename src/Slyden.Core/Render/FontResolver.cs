using SkiaSharp;

namespace Slyden.Core.Render;

/// <summary>
/// Acha a fonte pelo nome: primeiro nas pastas de fontes do programa (que viajam com o instalador),
/// depois nas fontes instaladas no sistema. Se não achar, usa a fonte padrão e avisa.
/// </summary>
public sealed class FontResolver : IDisposable
{
    private static readonly string[] Extensions = [".ttf", ".otf", ".ttc"];

    private readonly List<SKTypeface> _fromFolders = [];
    private readonly Dictionary<string, SKTypeface> _cache = new(StringComparer.OrdinalIgnoreCase);

    public FontResolver(IEnumerable<string>? fontFolders = null)
    {
        foreach (var dir in fontFolders ?? [])
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
                var face = SKTypeface.FromFile(file);
                if (face is not null) _fromFolders.Add(face);
            }
        }
    }

    /// <param name="warnings">Recebe uma mensagem se a fonte não existir.</param>
    public SKTypeface Resolve(string family, bool bold, bool italic, ICollection<string>? warnings = null)
    {
        string key = $"{family}|{bold}|{italic}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var face = FromFolders(family, bold, italic) ?? FromSystem(family, bold, italic);
        if (face is null)
        {
            face = SKTypeface.Default;
            warnings?.Add($"Fonte \"{family}\" não encontrada; usando \"{face.FamilyName}\".");
        }
        return _cache[key] = face;
    }

    private SKTypeface? FromFolders(string family, bool bold, bool italic)
    {
        return _fromFolders
            .Where(f => string.Equals(f.FamilyName, family, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => (f.FontWeight >= 600 ? 1 : 0) == (bold ? 1 : 0) ? 0 : 2)
            .ThenBy(f => (f.FontSlant != SKFontStyleSlant.Upright) == italic ? 0 : 1)
            .FirstOrDefault();
    }

    private static SKTypeface? FromSystem(string family, bool bold, bool italic)
    {
        var style = new SKFontStyle(
            bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        var face = SKTypeface.FromFamilyName(family, style);
        // o sistema devolve a fonte "mais parecida" quando não existe; só aceitamos o nome pedido
        return face is not null && string.Equals(face.FamilyName, family, StringComparison.OrdinalIgnoreCase) ? face : null;
    }

    public void Dispose()
    {
        foreach (var f in _fromFolders) f.Dispose();
        foreach (var f in _cache.Values)
            if (!ReferenceEquals(f, SKTypeface.Default) && !_fromFolders.Contains(f)) f.Dispose();
    }
}
