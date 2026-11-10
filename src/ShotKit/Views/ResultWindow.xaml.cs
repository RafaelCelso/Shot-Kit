using System.Windows;
using System.Windows.Media.Imaging;
using ShotKit.Services;

namespace ShotKit.Views;

/// <summary>Pré-visualização de uma captura longa com ações de salvar/copiar/fixar/OCR.</summary>
public partial class ResultWindow : Window
{
    public ResultWindow(BitmapSource image, AppSettings settings, string? warning)
    {
        InitializeComponent();
        Icon = AppIcon.WindowIcon;
        ResultImage.Source = image;
        // Exibe em tamanho real (até a largura da janela) em vez de tamanho em DIPs.
        ResultImage.MaxWidth = image.PixelWidth;
        InfoText.Text = $"{image.PixelWidth} × {image.PixelHeight} px";

        if (!string.IsNullOrEmpty(warning))
        {
            WarningText.Text = warning;
            WarningBar.Visibility = Visibility.Visible;
        }

        SaveButton.Click += (_, _) => Run(() =>
        {
            string path = ImageExporter.SaveToDefaultFolder(image, settings);
            InfoText.Text = "Salvo: " + System.IO.Path.GetFileName(path);
        });
        SaveAsButton.Click += (_, _) => Run(() =>
        {
            string? path = ImageExporter.SaveAs(image, settings, this);
            if (path != null) InfoText.Text = "Salvo: " + System.IO.Path.GetFileName(path);
        });
        CopyButton.Click += (_, _) =>
            InfoText.Text = ImageExporter.CopyToClipboard(image) ? "Copiado para a área de transferência." : "Falha ao copiar.";
        PinButton.Click += (_, _) =>
        {
            var (x, y) = Native.WindowHelper.CursorPx();
            var work = Native.WindowHelper.WorkAreaAt(x, y);
            new PinWindow(image, new Int32Rect(work.X + 40, work.Y + 40, image.PixelWidth, image.PixelHeight), settings).Show();
        };
        OcrButton.Click += (_, _) => new OcrWindow(image, settings) { Owner = this }.Show();
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao salvar.", ex);
            MessageBox.Show(this, "Não foi possível salvar: " + ex.Message, "ShotKit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
