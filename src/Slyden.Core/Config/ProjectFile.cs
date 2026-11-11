using System.Text.Json;

namespace Slyden.Core.Config;

/// <summary>Um projeto salvo: onde estão as fotos, a música e toda a configuração do slideshow.</summary>
public sealed class ProjectFile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Novo projeto";
    public string PhotosFolder { get; set; } = "";
    public string? AudioPath { get; set; }
    /// <summary>Preset com que o projeto foi criado (de onde vêm, por exemplo, os textos novos); vazio = o padrão.</summary>
    public string? PresetId { get; set; }
    public DateTime Modified { get; set; } = DateTime.UtcNow;
    public SlideshowConfig Config { get; set; } = new();

    /// <summary>Onde o arquivo está gravado; vazio = pasta padrão do programa. Não vai dentro do arquivo (a pasta pode ser movida).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? FilePath { get; set; }
}

public sealed record ProjectEntry(string Path, ProjectFile Project);

/// <summary>Guarda os projetos em uma pasta (um arquivo .slyden por projeto).</summary>
public sealed class ProjectStore(string root)
{
    public const string Extension = ".slyden";

    public string Root { get; } = root;

    public static string DefaultRoot =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Slyden", "Projects");

    public string PathOf(string id) => System.IO.Path.Combine(Root, id + Extension);

    // projetos gravados fora da pasta padrão (a pasta que o usuário escolheu) ficam registrados aqui
    private string IndexPath => System.IO.Path.Combine(Root, "projects.json");

    private List<string> ReadIndex()
    {
        try
        {
            return File.Exists(IndexPath)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(IndexPath)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return []; }
    }

    private void WriteIndex(List<string> paths)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(paths));
    }

    private static bool Same(string a, string b) =>
        string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private bool IsInRoot(string path) =>
        Same(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!, Root);

    private void Register(string path)
    {
        var index = ReadIndex();
        if (index.Any(p => Same(p, path))) return;
        index.Add(System.IO.Path.GetFullPath(path));
        WriteIndex(index);
    }

    private void Unregister(string path)
    {
        var index = ReadIndex();
        if (index.RemoveAll(p => Same(p, path)) > 0) WriteIndex(index);
    }

    /// <summary>Caminho livre para um projeto novo em <paramref name="folder"/>, a partir do nome (sem caracteres inválidos).</summary>
    public static string NewPathIn(string folder, string name)
    {
        string safe = string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (safe.Length == 0) safe = "projeto";
        string path = System.IO.Path.Combine(folder, safe + Extension);
        for (int n = 2; File.Exists(path); n++) path = System.IO.Path.Combine(folder, $"{safe} ({n}){Extension}");
        return path;
    }

    /// <summary>Projetos do mais recente para o mais antigo; arquivos ilegíveis ou que sumiram são ignorados.</summary>
    public IReadOnlyList<ProjectEntry> List()
    {
        var files = new List<string>();
        if (Directory.Exists(Root)) files.AddRange(Directory.EnumerateFiles(Root, "*" + Extension));
        files.AddRange(ReadIndex().Where(p => File.Exists(p) && !files.Any(f => Same(f, p))));

        var result = new List<ProjectEntry>();
        foreach (var file in files)
        {
            try { result.Add(new ProjectEntry(file, Load(file))); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException) { }
        }
        return result.OrderByDescending(e => e.Project.Modified).ToList();
    }

    public static ProjectFile Load(string path)
    {
        try
        {
            var project = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path), SlideshowConfig.Json)
                          ?? throw new InvalidDataException("Projeto vazio.");
            project.Config.Validate();
            project.FilePath = System.IO.Path.GetFullPath(path);
            return project;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Projeto inválido ({path}): {ex.Message}", ex);
        }
    }

    /// <summary>Grava (escrevendo num arquivo temporário e trocando, para não corromper se o programa fechar no meio).</summary>
    public string Save(ProjectFile project)
    {
        project.Modified = DateTime.UtcNow;
        string path = project.FilePath ?? PathOf(project.Id);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(project, SlideshowConfig.Json));
        File.Move(temp, path, overwrite: true);
        project.FilePath = System.IO.Path.GetFullPath(path);
        if (!IsInRoot(path)) Register(path);
        return path;
    }

    public void Delete(string id) => DeleteFile(PathOf(id));

    /// <summary>Apaga só o arquivo do projeto (nunca as fotos nem os vídeos).</summary>
    public void DeleteFile(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        Unregister(path);
    }

    public ProjectFile Duplicate(string id) => DuplicateFile(PathOf(id));

    /// <summary>Cria a cópia na mesma pasta do original.</summary>
    public ProjectFile DuplicateFile(string path)
    {
        var original = Load(path);
        var duplicate = new ProjectFile
        {
            Name = original.Name + " (cópia)",
            PhotosFolder = original.PhotosFolder,
            AudioPath = original.AudioPath,
            Config = original.Config.Clone(),
        };
        string folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        if (!IsInRoot(path)) duplicate.FilePath = NewPathIn(folder, duplicate.Name);
        Save(duplicate);
        return duplicate;
    }
}
