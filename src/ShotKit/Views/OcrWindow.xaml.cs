using System.Windows;
using System.Windows.Media.Imaging;
using ShotKit.Services;

namespace ShotKit.Views;

/// <summary>Mostra o texto reconhecido de uma imagem e permite copiá-lo.</summary>
public partial class OcrWindow : Window
{
    private readonly BitmapSource _image;
    private readonly AppSettings _settings;
    private int _runId;

    public OcrWindow(BitmapSource image, AppSettings settings)
    {
        InitializeComponent();
        _image = image;
        _settings = settings;
        Icon = AppIcon.WindowIcon;
        Preview.Source = image;

        var languages = OcrService.AvailableLanguages();
        LanguageBox.ItemsSource = languages;
        LanguageBox.SelectedItem =
            languages.FirstOrDefault(l => string.Equals(l.Tag, settings.OcrLanguage, StringComparison.OrdinalIgnoreCase))
            ?? languages.FirstOrDefault();
        LanguageBox.SelectionChanged += async (_, _) =>
        {
            if (LanguageBox.SelectedItem is OcrService.OcrLanguageOption opt)
            {
                _settings.OcrLanguage = opt.Tag;
                TrySaveSettings();
            }
            await RunAsync();
        };

        CopyButton.Click += (_, _) =>
        {
            string text = ResultText.SelectionLength > 0 ? ResultText.SelectedText : ResultText.Text;
            StatusText.Text = ImageExporter.CopyText(text) ? "Copiado para a área de transferência." : "Falha ao copiar.";
        };
        CloseButton.Click += (_, _) => Close();

        Loaded += async (_, _) =>
        {
            if (languages.Count == 0)
            {
                StatusText.Text = "";
                ResultText.Text = "Nenhum idioma de OCR está instalado no Windows.\n\n" +
                                  "Instale um em Configurações > Hora e idioma > Idioma e região > Adicionar um idioma " +
                                  "(marque o recurso de reconhecimento óptico de caracteres).";
                CopyButton.IsEnabled = false;
                return;
            }
            await RunAsync();
        };
    }

    private async Task RunAsync()
    {
        int id = ++_runId;
        string tag = (LanguageBox.SelectedItem as OcrService.OcrLanguageOption)?.Tag ?? "";
        StatusText.Text = "Reconhecendo texto...";
        CopyButton.IsEnabled = false;
        try
        {
            string text = await OcrService.RecognizeAsync(_image, tag);
            if (id != _runId) return; // outra execução foi iniciada
            ResultText.Text = text;
            StatusText.Text = text.Length == 0 ? "Nenhum texto encontrado." : $"{text.Length} caracteres.";
            CopyButton.IsEnabled = text.Length > 0;
            ResultText.Focus();
        }
        catch (Exception ex)
        {
            if (id != _runId) return;
            Log.Error("Falha no OCR.", ex);
            StatusText.Text = "Falha no OCR.";
            ResultText.Text = ex.Message;
        }
    }

    private void TrySaveSettings()
    {
        try { _settings.Save(); }
        catch (Exception ex) { Log.Error("Falha ao salvar configurações.", ex); }
    }
}
