using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ShotKit.Services;

namespace ShotKit.Views;

/// <summary>Edita uma cópia das configurações; só aplica ao clicar em Salvar.</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _draft;
    private readonly Dictionary<HotkeyAction, TextBox> _hotkeyBoxes = new();

    /// <summary>Configurações confirmadas pelo usuário (null se cancelou).</summary>
    public AppSettings? Saved { get; private set; }

    private static readonly Dictionary<HotkeyAction, string> ActionNames = new()
    {
        [HotkeyAction.CaptureArea] = "Capturar área",
        [HotkeyAction.CaptureOcr] = "Capturar e extrair texto",
        [HotkeyAction.CaptureFullScreen] = "Capturar tela inteira",
        [HotkeyAction.CaptureScrolling] = "Captura com rolagem",
    };

    public SettingsWindow(AppSettings current, IReadOnlyCollection<HotkeySetting> failedHotkeys)
    {
        InitializeComponent();
        Icon = AppIcon.WindowIcon;
        _draft = current.Clone();

        FolderBox.Text = _draft.SaveFolder;
        FormatBox.SelectedIndex = (int)_draft.SaveFormat;
        QualitySlider.Value = _draft.JpegQuality;
        SoundBox.IsChecked = _draft.PlaySound;
        ClipboardBox.IsChecked = _draft.CopyToClipboardAfterSave;
        StartupBox.IsChecked = _draft.LaunchOnStartup;

        var langs = OcrService.AvailableLanguages().ToList();
        langs.Insert(0, new OcrService.OcrLanguageOption("", "Automático (idioma do Windows)"));
        OcrBox.ItemsSource = langs;
        OcrBox.SelectedItem = langs.FirstOrDefault(l => l.Tag.Equals(_draft.OcrLanguage, StringComparison.OrdinalIgnoreCase)) ?? langs[0];

        BuildHotkeyRows();
        if (failedHotkeys.Count > 0)
        {
            HotkeyWarning.Text = "Em uso por outro programa (não registrados): " + string.Join(", ", failedHotkeys.Select(h => h.ToString()));
            HotkeyWarning.Visibility = Visibility.Visible;
        }

        VersionText.Text = "Versão " + (typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
        DataFolderRun.Text = AppSettings.DataFolder;

        BrowseButton.Click += (_, _) => Browse();
        ResetHotkeysButton.Click += (_, _) =>
        {
            _draft.Hotkeys = AppSettings.DefaultHotkeys();
            foreach (var hk in _draft.Hotkeys) _hotkeyBoxes[hk.Action].Text = hk.ToString();
        };
        OkButton.Click += (_, _) => Commit();
    }

    private void BuildHotkeyRows()
    {
        int row = 0;
        foreach (var hk in _draft.Hotkeys.OrderBy(h => h.Action))
        {
            HotkeyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new Label { Content = ActionNames[hk.Action], Padding = new Thickness(0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var box = new TextBox
            {
                Text = hk.ToString(),
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(0, 3, 0, 3),
                Tag = hk,
            };
            label.Target = box;
            System.Windows.Automation.AutomationProperties.SetName(box, "Atalho para " + ActionNames[hk.Action]);
            box.PreviewKeyDown += OnHotkeyKeyDown;
            Grid.SetRow(label, row);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            HotkeyGrid.Children.Add(label);
            HotkeyGrid.Children.Add(box);
            _hotkeyBoxes[hk.Action] = box;
            row++;
        }
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        var hk = (HotkeySetting)box.Tag;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Deixa Tab navegar normalmente entre os campos.
        if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return;
        e.Handled = true;

        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            hk.Key = Key.None;
            hk.Modifiers = ModifierKeys.None;
            box.Text = hk.ToString();
            return;
        }

        // Ignora teclas modificadoras isoladas.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            box.Text = HotkeySetting.Format(Keyboard.Modifiers, Key.None) + "...";
            return;
        }

        var mods = Keyboard.Modifiers;
        // Exige modificador, exceto para PrintScreen e teclas F.
        bool standalone = key == Key.Snapshot || key is >= Key.F1 and <= Key.F24;
        if (mods == ModifierKeys.None && !standalone)
        {
            box.Text = "Use Ctrl, Alt, Shift ou Win com a tecla";
            return;
        }

        hk.Key = key;
        hk.Modifiers = mods;
        box.Text = hk.ToString();
    }

    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : null,
            Title = "Escolha a pasta para salvar as capturas",
        };
        if (dlg.ShowDialog(this) == true) FolderBox.Text = dlg.FolderName;
    }

    private void Commit()
    {
        string folder = FolderBox.Text.Trim();
        try
        {
            if (string.IsNullOrEmpty(folder) || !Path.IsPathRooted(folder)) throw new IOException("Informe um caminho completo.");
            Directory.CreateDirectory(folder);
            // Confere permissão de escrita.
            string probe = Path.Combine(folder, ".shotkit-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "A pasta não pode ser usada: " + ex.Message, "ShotKit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Atalhos duplicados.
        var dup = _draft.Hotkeys.Where(h => !h.IsEmpty)
            .GroupBy(h => (h.Modifiers, h.Key)).FirstOrDefault(g => g.Count() > 1);
        if (dup != null)
        {
            MessageBox.Show(this, $"O atalho {dup.First()} está atribuído a mais de uma ação.", "ShotKit",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _draft.SaveFolder = folder;
        _draft.SaveFormat = (ImageFormatKind)Math.Max(0, FormatBox.SelectedIndex);
        _draft.JpegQuality = (int)QualitySlider.Value;
        _draft.PlaySound = SoundBox.IsChecked == true;
        _draft.CopyToClipboardAfterSave = ClipboardBox.IsChecked == true;
        _draft.LaunchOnStartup = StartupBox.IsChecked == true;
        _draft.OcrLanguage = (OcrBox.SelectedItem as OcrService.OcrLanguageOption)?.Tag ?? "";

        Saved = _draft;
        DialogResult = true;
    }
}
