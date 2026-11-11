using Avalonia;
using Avalonia.Controls;
using Slyden.Core.Config;
using SlideText = Slyden.Core.Config.TextBlock;

namespace Slyden.App;

/// <summary>Tela de configurações do programa: padrões para projetos e textos NOVOS, fora de qualquer projeto.</summary>
public partial class MainWindow
{
    private bool _stLoading;
    private Preset _settingsPreset = new();
    private readonly Dictionary<Button, (string Label, Avalonia.Threading.DispatcherTimer Timer)> _confirming = [];

    private static readonly string[] SwatchColors =
        ["#FFFFFF", "#F3EBDD", "#C9C9CE", "#6B6B73", "#1B2A41", "#7A1F2B", "#D9A5A5", "#3E5C4B"];

    private void InitSettings()
    {
        BuildChips(StFormatChips, FormatLabels, multi: false, on => EditDefaults(d => d.Format = FormatValues[on[0]]));
        BuildChips(StResolutionChips, ResolutionLabels, multi: false,
                   on => EditDefaults(d => d.Resolution = VideoFormat.Resolutions[on[0]]));
        BuildChips(StEffectChips, Effects.Select(EffectName).ToArray(), multi: true,
                   on => EditDefaults(d => d.Default.Effects = on.Select(i => Effects[i]).ToList()));
        BuildChips(StTransitionChips, ["Corte", "Fade"], multi: false,
                   on => EditDefaults(d => d.Default.Transition = (TransitionKind)on[0]));
        BuildChips(StBackgroundChips, ["Desfocado", "Preto", "Cor"], multi: false,
                   on => EditDefaults(d => d.Default.Background = (BackgroundKind)on[0]));

        Slide(StIntensity, StIntensityValue, v => $"{v:0}%", v => EditDefaults(d => d.Default.Intensity = v / 100));
        Slide(StScale, StScaleValue, v => $"{v:0}%", v => EditDefaults(d => d.Default.Scale = v / 100));
        Slide(StShadow, StShadowValue, v => $"{v:0}%", v => EditDefaults(d => d.Default.Shadow = v / 100));
        Slide(StFadeIn, StFadeInValue, Seconds, v => EditDefaults(d => d.AudioFadeIn = v));
        Slide(StFadeOut, StFadeOutValue, Seconds, v => EditDefaults(d => d.AudioFadeOut = v));
        Slide(StTextSize, StTextSizeValue, v => $"{v:0}", v => EditTextStyle(t => t.Size = v / 100));

        StMusicMode.SelectionChanged += (_, _) =>
            EditDefaults(d => d.MusicMode = StMusicMode.SelectedIndex == 1 ? MusicMode.FitSlides : MusicMode.FitMusic);

        foreach (var hex in SwatchColors)
            StSwatches.Children.Add(Swatch(hex, () => { StColorBox.Text = hex; }));
        StColorBox.TextChanged += (_, _) =>
        {
            if (!TryHex(StColorBox.Text, out var color)) return;
            StColorPreview.Background = new Avalonia.Media.SolidColorBrush(color);
            if (_stLoading) return;
            string text = StColorBox.Text!.Trim().ToUpperInvariant();
            EditDefaults(d => { d.Default.BackgroundColor = text; d.Default.Background = BackgroundKind.Color; });
            SetChips(StBackgroundChips, [(int)BackgroundKind.Color]); // escolher uma cor já escolhe o fundo "Cor"
        };

        StTextFont.ItemsSource = _fontNames;
        StTextFont.SelectionChanged += (_, _) =>
        {
            if (StTextFont.SelectedItem is string family) EditTextStyle(t => t.Font = family);
        };
        StTextColor.TextChanged += (_, _) =>
        {
            if (!TryHex(StTextColor.Text, out var color)) return;
            StTextColorPreview.Background = new Avalonia.Media.SolidColorBrush(color);
            string text = StTextColor.Text!.Trim().ToUpperInvariant();
            EditTextStyle(t => t.Color = text);
        };
        StTextBold.Click += (_, _) => EditTextStyle(t => t.Bold = StTextBold.IsChecked == true);
        StTextItalic.Click += (_, _) => EditTextStyle(t => t.Italic = StTextItalic.IsChecked == true);
        StTextShadow.Click += (_, _) => EditTextStyle(t => t.Shadow = StTextShadow.IsChecked == true);
        StTextAlign.SelectionChanged += (_, _) =>
            EditTextStyle(t => t.Align = (Slyden.Core.Config.TextAlign)Math.Max(0, StTextAlign.SelectedIndex));
        StTextFadeIn.ValueChanged += (_, _) => EditTextStyle(t => t.FadeIn = Num(StTextFadeIn));
        StTextFadeOut.ValueChanged += (_, _) => EditTextStyle(t => t.FadeOut = Num(StTextFadeOut));

        SettingsButton.Click += (_, _) => { if (SettingsView.IsVisible) CloseSettings(); else ShowSettings(); };
        SettingsCloseButton.Click += (_, _) => CloseSettings();
        CopyFromProjectButton.Click += (_, _) =>
        {
            if (!EditorActive) return;
            _settingsPreset.CaptureFrom(_cfg);
            SavePresets(DefaultsStatus, $"Copiado: os ajustes do projeto aberto foram para o preset \"{_settingsPreset.Name}\".");
            LoadSettings();
        };
        InitPresetManagement();
    }

    private void InitPresetManagement()
    {
        StPresetBox.SelectionChanged += (_, _) =>
        {
            if (_stLoading || StPresetBox.SelectedIndex < 0 || StPresetBox.SelectedIndex >= _library.Presets.Count) return;
            _settingsPreset = _library.Presets[StPresetBox.SelectedIndex];
            DefaultsStatus.Text = "";
            LoadSettings();
            UpdatePresetControls();
        };
        NewPresetBox.SelectionChanged += (_, _) =>
        {
            if (_loading || NewPresetBox.SelectedIndex < 0 || !NewProjectView.IsVisible) return;
            ApplyNewPresetChips();
            UpdateNewSizeInfo();
        };
        PresetNewButton.Click += (_, _) => AddPreset(_library.Add("Novo preset"), "Preset criado com os ajustes de fábrica.");
        PresetDuplicateButton.Click += (_, _) =>
            AddPreset(_library.Add(_settingsPreset.Name + " (cópia)", _settingsPreset), "Preset duplicado.");
        PresetFromProjectButton.Click += (_, _) =>
        {
            if (!EditorActive) return;
            var preset = _library.Add(_project!.Name);
            preset.CaptureFrom(_cfg);
            AddPreset(preset, "Preset criado a partir do projeto aberto.");
        };
        PresetDeleteButton.Click += (_, _) => Confirm(PresetDeleteButton, "Clique de novo para excluir", () =>
        {
            if (!_library.Delete(_settingsPreset)) return;
            _settingsPreset = _library.Default;
            RefreshPresetList();
            LoadSettings();
            SavePresets(DefaultsStatus, "Preset excluído (os projetos que o usavam passam a usar o padrão).");
        });
        PresetRenameButton.Click += (_, _) =>
        {
            if (!_library.Rename(_settingsPreset, StPresetName.Text ?? ""))
            {
                DefaultsStatus.Text = "Nome vazio ou já usado por outro preset.";
                return;
            }
            RefreshPresetList();
            SavePresets(DefaultsStatus, "Renomeado.");
        };
        PresetDefaultButton.Click += (_, _) =>
        {
            _library.DefaultId = _settingsPreset.Id;
            RefreshPresetList();
            SavePresets(DefaultsStatus, $"\"{_settingsPreset.Name}\" é o padrão para projetos novos.");
        };
        ResetDefaultsButton.Click += (_, _) => Confirm(ResetDefaultsButton, "Clique de novo para restaurar", () =>
        {
            _settingsPreset.ResetToFactory();
            SavePresets(DefaultsStatus, "Preset restaurado ao de fábrica.");
            LoadSettings();
        });
    }

    private void AddPreset(Preset preset, string message)
    {
        _settingsPreset = preset;
        RefreshPresetList();
        LoadSettings();
        SavePresets(DefaultsStatus, message);
    }

    /// <summary>Recarrega a lista de presets (★ marca o padrão) e seleciona <see cref="_settingsPreset"/>.</summary>
    private void RefreshPresetList()
    {
        bool was = _stLoading;
        _stLoading = true;
        try
        {
            StPresetBox.ItemsSource = _library.Presets.Select(p => p.Id == _library.DefaultId ? p.Name + "  ★" : p.Name).ToList();
            StPresetBox.SelectedIndex = Math.Max(0, _library.Presets.IndexOf(_settingsPreset));
            StPresetName.Text = _settingsPreset.Name;
        }
        finally { _stLoading = was; }
        UpdatePresetControls();
    }

    private void UpdatePresetControls()
    {
        StPresetName.Text = _settingsPreset.Name;
        PresetDeleteButton.IsEnabled = _library.Presets.Count > 1;
        PresetDefaultButton.IsChecked = _settingsPreset.Id == _library.DefaultId;
    }

    /// <summary>Botão de duas etapas: o primeiro clique pede confirmação, o segundo (em 4 s) executa.</summary>
    private void Confirm(Button button, string ask, Action run)
    {
        if (_confirming.Remove(button, out var pending))
        {
            pending.Timer.Stop();
            button.Content = pending.Label;
            run();
            return;
        }
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _confirming[button] = (button.Content?.ToString() ?? "", timer);
        button.Content = ask;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_confirming.Remove(button, out var p)) button.Content = p.Label;
        };
        timer.Start();
    }
    private static bool TryHex(string? text, out Avalonia.Media.Color color)
    {
        color = default;
        text = text?.Trim();
        if (text is not { Length: 7 } || !HexColor.TryParse(text, out var c)) return false;
        color = Avalonia.Media.Color.FromRgb(c.R, c.G, c.B);
        return true;
    }

    private static Button Swatch(string hex, Action pick)
    {
        var button = new Button
        {
            Width = 26, Height = 26, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(0),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex)),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#55555B")),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
        };
        button.Click += (_, _) => pick();
        return button;
    }

    private void Slide(Slider slider, Avalonia.Controls.TextBlock label, Func<double, string> format, Action<double> changed)
    {
        slider.ValueChanged += (_, _) =>
        {
            label.Text = format(slider.Value);
            if (!_stLoading) changed(slider.Value);
        };
    }

    private void EditDefaults(Action<Preset> edit)
    {
        if (_stLoading) return;
        edit(_settingsPreset);
        SavePresets(DefaultsStatus, "Salvo.");
    }

    private void EditTextStyle(Action<SlideText> edit)
    {
        if (_stLoading) return;
        _settingsPreset.TextStyle ??= _settingsPreset.NewText("");
        edit(_settingsPreset.TextStyle);
        SavePresets(DefaultsStatus, "Salvo.");
    }

    private void ShowSettings()
    {
        _settingsPreset = _library.For(_project?.PresetId);
        RefreshPresetList();
        LoadSettings();
        CopyFromProjectButton.IsEnabled = EditorActive;
        PresetFromProjectButton.IsEnabled = EditorActive;
        DefaultsStatus.Text = "";
        SettingsView.IsVisible = true;
    }

    private void CloseSettings() => SettingsView.IsVisible = false;

    /// <summary>Mostra nos controles o que está salvo em <see cref="_settingsPreset"/>.</summary>
    private void LoadSettings()
    {
        _stLoading = true;
        bool was = _loading;
        _loading = true;
        try
        {
            var d = _settingsPreset;
            SetChips(StFormatChips, [Math.Max(0, Array.IndexOf(FormatValues, d.Format))]);
            SetChips(StResolutionChips, [Math.Max(0, Array.IndexOf(VideoFormat.Resolutions, d.Resolution))]);
            SetChips(StEffectChips, d.Default.Effects.Select(e => Array.IndexOf(Effects, e)).Where(i => i >= 0).ToArray());
            SetChips(StTransitionChips, [(int)d.Default.Transition]);
            SetChips(StBackgroundChips, [(int)d.Default.Background]);
            StIntensity.Value = d.Default.Intensity * 100;
            StScale.Value = d.Default.Scale * 100;
            StShadow.Value = d.Default.Shadow * 100;
            StFadeIn.Value = d.AudioFadeIn;
            StFadeOut.Value = d.AudioFadeOut;
            StMusicMode.SelectedIndex = d.MusicMode == MusicMode.FitSlides ? 1 : 0;
            StColorBox.Text = d.Default.BackgroundColor;

            var style = d.TextStyle ?? d.NewText("");
            string family = style.Font;
            if (!_fontNames.Any(n => string.Equals(n, family, StringComparison.OrdinalIgnoreCase))) _fontNames.Insert(0, family);
            StTextFont.SelectedItem = _fontNames.First(n => string.Equals(n, family, StringComparison.OrdinalIgnoreCase));
            StTextSize.Value = style.Size * 100;
            StTextColor.Text = style.Color;
            StTextBold.IsChecked = style.Bold;
            StTextItalic.IsChecked = style.Italic;
            StTextShadow.IsChecked = style.Shadow;
            StTextAlign.SelectedIndex = (int)style.Align;
            StTextFadeIn.Value = (decimal)(style.FadeIn ?? Math.Round(style.FadeBeats * 0.5, 1));
            StTextFadeOut.Value = (decimal)(style.FadeOut ?? Math.Round(style.FadeBeats * 0.5, 1));
        }
        finally
        {
            _loading = was;
            _stLoading = false;
        }
    }
}
