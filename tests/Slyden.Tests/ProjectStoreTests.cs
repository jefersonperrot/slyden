using Slyden.Core.Config;

namespace Slyden.Tests;

public class ProjectStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"slp_{Guid.NewGuid():N}");
    private ProjectStore Store => new(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static ProjectFile Sample(string name) => new()
    {
        Name = name, PhotosFolder = @"C:\fotos", AudioPath = @"C:\musica.mp3",
        Config = new SlideshowConfig
        {
            Format = "9:16",
            Photos = [new PhotoSpec { File = "a.jpg", Length = 2, Texts = [new TextBlock { Text = "Maria e João" }] }],
        },
    };

    [Fact]
    public void Save_and_load_round_trip()
    {
        var project = Sample("Casamento");
        string path = Store.Save(project);
        var back = ProjectStore.Load(path);
        Assert.Equal("Casamento", back.Name);
        Assert.Equal(@"C:\musica.mp3", back.AudioPath);
        Assert.Equal("9:16", back.Config.Format);
        Assert.Equal(2, back.Config.Photos[0].Length);
        Assert.Equal("Maria e João", back.Config.Photos[0].Texts![0].Text);
    }

    [Fact]
    public void List_orders_newest_first_and_skips_broken_files()
    {
        var older = Sample("Antigo");
        Store.Save(older);
        Thread.Sleep(20);
        Store.Save(Sample("Novo"));
        File.WriteAllText(Path.Combine(_root, "quebrado" + ProjectStore.Extension), "{ não é json");

        var names = Store.List().Select(e => e.Project.Name).ToArray();
        Assert.Equal(["Novo", "Antigo"], names);
    }

    [Fact]
    public void List_of_missing_folder_is_empty() => Assert.Empty(new ProjectStore(_root + "_nao_existe").List());

    [Fact]
    public void Duplicate_creates_independent_copy()
    {
        var original = Sample("Casamento");
        Store.Save(original);
        var copy = Store.Duplicate(original.Id);

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal("Casamento (cópia)", copy.Name);
        Assert.Equal(2, Store.List().Count);

        var loaded = ProjectStore.Load(Store.PathOf(copy.Id));
        loaded.Config.Photos.Clear();
        Store.Save(loaded);
        Assert.Single(ProjectStore.Load(Store.PathOf(original.Id)).Config.Photos);
    }

    [Fact]
    public void Delete_removes_only_that_project()
    {
        var a = Sample("A"); var b = Sample("B");
        Store.Save(a); Store.Save(b);
        Store.Delete(a.Id);
        Assert.Equal(["B"], Store.List().Select(e => e.Project.Name).ToArray());
    }

    [Fact]
    public void Save_leaves_no_temp_file()
    {
        Store.Save(Sample("X"));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }
}
