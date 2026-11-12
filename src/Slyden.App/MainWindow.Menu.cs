using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Slyden.Core.Config;

namespace Slyden.App;

/// <summary>Barra de menus: Arquivo, Editar, Projeto e Ajuda; cada item chama uma ação que já existe.</summary>
public partial class MainWindow
{
    private void InitMenu()
    {
        MenuNew.Click += (_, _) => { LeaveEditor(); ShowNewProject(); };
        MenuProjects.Click += (_, _) => ShowHome();
        MenuSave.Click += (_, _) => Save();
        MenuExport.Click += async (_, _) => { ShowTab(3); await ExportOrCancelAsync(); };
        MenuExit.Click += (_, _) => Close(); // OnClosing cuida de salvar
        MenuUndo.Click += (_, _) => Undo();
        MenuRedo.Click += (_, _) => Redo();
        MenuRename.Click += async (_, _) => { if (_project is not null) await RenameProjectAsync(_project); };
        MenuMusic.Click += async (_, _) => await ChooseMusicAsync();
        MenuRemoveMusic.Click += (_, _) => RemoveMusic();
        MenuSettings.Click += (_, _) => { if (SettingsView.IsVisible) CloseSettings(); else ShowSettings(); };
        MenuShortcuts.Click += (_, _) => ShowInfo("Atalhos de teclado",
            "Espaço\t\ttocar / parar a prévia\nCtrl+S\t\tsalvar\nCtrl+Z\t\tdesfazer\nCtrl+Y  ou  Ctrl+Shift+Z\trefazer\n" +
            "Clique duplo num controle deslizante\tvolta ao valor padrão\nArrastar o texto na prévia\tmove o texto");
        MenuAbout.Click += (_, _) => ShowInfo("Sobre o Slyden",
            $"Slyden {typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1"}\n\n" +
            "Cria vídeos de fotos com cortes na batida da música.\nO vídeo é gerado com o ffmpeg.");

        // itens que dependem de um projeto aberto são atualizados quando o menu abre
        foreach (var top in new[] { MenuFile, MenuEdit, MenuProject })
            top.SubmenuOpened += (_, _) => UpdateMenuState();
        UpdateMenuState();
    }

    private void UpdateMenuState()
    {
        bool editor = EditorActive;
        MenuSave.IsEnabled = _project is not null;
        MenuExport.IsEnabled = editor;
        MenuUndo.IsEnabled = editor && _undo.Count > 0;
        MenuRedo.IsEnabled = editor && _redo.Count > 0;
        MenuRename.IsEnabled = editor;
        MenuMusic.IsEnabled = editor;
        MenuRemoveMusic.IsEnabled = editor && _audio is not null;
    }

    /// <summary>Sai do que estiver aberto por cima (Configurações) e guarda o projeto antes de ir para outra tela.</summary>
    private void LeaveEditor()
    {
        StopPlayback(refresh: false);
        SettingsView.IsVisible = false;
        if (_project is not null && _dirty) Save();
    }

    private async void ShowInfo(string title, string text)
    {
        var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(20, 5) };
        var dialog = new Window
        {
            Title = title, SizeToContent = SizeToContent.WidthAndHeight, MinWidth = 380, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 18,
                Children = { new Avalonia.Controls.TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, LineHeight = 22 }, ok },
            },
        };
        ok.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    /// <summary>Pede o novo nome e grava; vale para o projeto aberto e para os da lista.</summary>
    private async Task RenameProjectAsync(ProjectFile project)
    {
        string? name = await PromptAsync("Renomear projeto", "Novo nome do projeto:", project.Name);
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name == project.Name) return;
        try
        {
            if (_project is not null && _project.Id == project.Id)
            {
                _project.Name = name;
                Save(); // grava o projeto aberto inteiro, para um salvamento seguinte não trazer o nome antigo de volta
            }
            else
            {
                project.Name = name;
                _store.Save(project);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HomeMessage.Text = "Não consegui renomear: " + ex.Message;
            return;
        }
        UpdateSaveState();
        if (HomeView.IsVisible) RefreshProjectList();
    }

    /// <summary>Janelinha com um campo de texto; devolve o texto, ou nulo se o usuário cancelar.</summary>
    private async Task<string?> PromptAsync(string title, string label, string initial)
    {
        var box = new TextBox { Text = initial, MinWidth = 340 };
        var ok = new Button { Content = "OK", Padding = new Thickness(20, 5), IsDefault = true };
        var cancel = new Button { Content = "Cancelar", Padding = new Thickness(16, 5), IsCancel = true };
        var dialog = new Window
        {
            Title = title, SizeToContent = SizeToContent.WidthAndHeight, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 12,
                Children =
                {
                    new Avalonia.Controls.TextBlock { Text = label }, box,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancel, ok },
                    },
                },
            },
        };
        ok.Click += (_, _) => dialog.Close(box.Text);
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        return await dialog.ShowDialog<string?>(this);
    }
}