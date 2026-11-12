using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Slyden.Core.Config;
using AvaloniaText = Avalonia.Controls.TextBlock;

namespace Slyden.App;

/// <summary>Sons por cima da música (ex.: voz do noivo): cada um entra no início de uma foto e a música abaixa enquanto ele toca.</summary>
public partial class MainWindow
{
    private void InitSounds()
    {
        AddSoundButton.Click += async (_, _) => await AddSoundsAsync();
    }

    private async Task AddSoundsAsync()
    {
        if (_project is null) return;
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Som por cima da música",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Áudio") { Patterns = ["*.mp3", "*.wav", "*.m4a", "*.aac", "*.flac", "*.ogg"] }],
        });
        var paths = picked.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0) return;
        StopPlayback(refresh: true);
        // começa na foto que está selecionada (é o jeito mais natural de "colocar a voz aqui")
        int photo = Math.Clamp(_selected + 1, 1, Math.Max(1, _cfg.Photos.Count));
        foreach (var p in paths) _cfg.Sounds.Add(new OverlaySound { Path = p, Photo = photo });
        MarkDirty();
        RebuildSoundList();
    }

    private void RebuildSoundList()
    {
        SoundList.Children.Clear();
        for (int i = 0; i < _cfg.Sounds.Count; i++) SoundList.Children.Add(BuildSoundRow(_cfg.Sounds[i]));
    }

    private Control BuildSoundRow(OverlaySound sound)
    {
        bool exists = File.Exists(sound.Path);
        var name = new AvaloniaText
        {
            Text = (exists ? "" : "⚠ ") + Path.GetFileName(sound.Path),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = exists ? null : new SolidColorBrush(Color.Parse("#E8A33A")),
        };
        ToolTip.SetTip(name, exists ? sound.Path : "Arquivo não encontrado: " + sound.Path);

        var remove = new Button { Content = "✕", Padding = new Thickness(8, 2) };
        remove.Classes.Add("btn");
        ToolTip.SetTip(remove, "Tirar este som do projeto");
        remove.Click += (_, _) =>
        {
            _cfg.Sounds.Remove(sound);
            MarkDirty();
            RebuildSoundList();
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(name);
        Grid.SetColumn(remove, 1);
        header.Children.Add(remove);

        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, RowSpacing = 6 };
        int row = 0;
        void Add(string label, string tip, double value, double min, double max, double step, string format, Action<double> apply)
        {
            fields.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var text = new AvaloniaText { Text = label, VerticalAlignment = VerticalAlignment.Center };
            text.Classes.Add("dim");
            var box = new NumericUpDown
            {
                Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)step, FormatString = format,
                Value = (decimal)value, HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            ToolTip.SetTip(box, tip);
            box.ValueChanged += (_, _) =>
            {
                if (_loading || _project is null) return;
                apply(Num(box));
                MarkDirty();
            };
            Grid.SetRow(text, row);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            fields.Children.Add(text);
            fields.Children.Add(box);
            row++;
        }

        Add("Entra na foto nº", "O som começa quando esta foto aparece.", sound.Photo, 1, 9999, 1, "0", v => sound.Photo = (int)v);
        Add("Espera (s)", "Atraso depois do início da foto.", sound.Delay, 0, 600, 0.5, "0.##", v => sound.Delay = v);
        Add("Início do trecho (s)", "De onde do arquivo o som começa a tocar.", sound.Start, 0, 3600, 1, "0.##", v => sound.Start = v);
        Add("Fim do trecho (s)", "Onde o som termina. 0 = até o fim do arquivo.", sound.End ?? 0, 0, 3600, 1, "0.##",
            v => sound.End = v > sound.Start ? v : null);
        Add("Volume do som (%)", "100 = como está no arquivo.", sound.Volume * 100, 0, 400, 5, "0", v => sound.Volume = v / 100);
        Add("Música fica em (%)", "Volume da música enquanto o som toca. 100 = não abaixa.", sound.Duck * 100, 0, 100, 5, "0", v => sound.Duck = v / 100);

        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(header);
        body.Children.Add(fields);
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#26262A")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 8,
                Children =
                {
                    new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.Parse("#E5605A")) },
                    Place(body, 1),
                },
            },
        };

        static Control Place(Control c, int column)
        {
            Grid.SetColumn(c, column);
            return c;
        }
    }
}
