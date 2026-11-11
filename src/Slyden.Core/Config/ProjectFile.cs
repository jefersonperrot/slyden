using System.Text.Json;

namespace Slyden.Core.Config;

/// <summary>Um projeto salvo: onde estão as fotos, a música e toda a configuração do slideshow.</summary>
public sealed class ProjectFile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Novo projeto";
    public string PhotosFolder { get; set; } = "";
    public string? AudioPath { get; set; }
    public DateTime Modified { get; set; } = DateTime.UtcNow;
    public SlideshowConfig Config { get; set; } = new();
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

    /// <summary>Projetos do mais recente para o mais antigo; arquivos ilegíveis são ignorados.</summary>
    public IReadOnlyList<ProjectEntry> List()
    {
        if (!Directory.Exists(Root)) return [];
        var result = new List<ProjectEntry>();
        foreach (var file in Directory.EnumerateFiles(Root, "*" + Extension))
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
        Directory.CreateDirectory(Root);
        project.Modified = DateTime.UtcNow;
        string path = PathOf(project.Id);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(project, SlideshowConfig.Json));
        File.Move(temp, path, overwrite: true);
        return path;
    }

    public void Delete(string id)
    {
        string path = PathOf(id);
        if (File.Exists(path)) File.Delete(path);
    }

    public ProjectFile Duplicate(string id)
    {
        var copy = Load(PathOf(id)).Config.Clone();
        var original = Load(PathOf(id));
        var duplicate = new ProjectFile
        {
            Name = original.Name + " (cópia)",
            PhotosFolder = original.PhotosFolder,
            AudioPath = original.AudioPath,
            Config = copy,
        };
        Save(duplicate);
        return duplicate;
    }
}
