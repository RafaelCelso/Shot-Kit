using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ShotKit.Services;

/// <summary>Salvar em arquivo e copiar para a área de transferência.</summary>
public static class ImageExporter
{
    public static string Extension(ImageFormatKind f) => f switch
    {
        ImageFormatKind.Jpg => ".jpg",
        ImageFormatKind.Bmp => ".bmp",
        _ => ".png",
    };

    public static string NewFileName(ImageFormatKind f) =>
        $"ShotKit_{DateTime.Now:yyyyMMdd_HHmmss_fff}{Extension(f)}";

    /// <summary>Salva na pasta configurada com nome automático e retorna o caminho.</summary>
    public static string SaveToDefaultFolder(BitmapSource image, AppSettings settings)
    {
        Directory.CreateDirectory(settings.SaveFolder);
        string path = Path.Combine(settings.SaveFolder, NewFileName(settings.SaveFormat));
        SaveToFile(image, path, settings.JpegQuality);
        return path;
    }

    /// <summary>Abre "Salvar como"; retorna o caminho ou null se o usuário cancelar.</summary>
    public static string? SaveAs(BitmapSource image, AppSettings settings, Window? owner = null)
    {
        Directory.CreateDirectory(settings.SaveFolder);
        var dlg = new SaveFileDialog
        {
            InitialDirectory = settings.SaveFolder,
            FileName = NewFileName(settings.SaveFormat),
            Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg;*.jpeg|Bitmap (*.bmp)|*.bmp",
            FilterIndex = settings.SaveFormat switch { ImageFormatKind.Jpg => 2, ImageFormatKind.Bmp => 3, _ => 1 },
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(owner) != true) return null;
        SaveToFile(image, dlg.FileName, settings.JpegQuality);
        return dlg.FileName;
    }

    public static void SaveToFile(BitmapSource image, string path, int jpegQuality = 92)
    {
        BitmapEncoder encoder = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) },
            ".bmp" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder(),
        };
        encoder.Frames.Add(BitmapFrame.Create(image));

        // Grava em arquivo temporário e move, para não deixar arquivo corrompido em caso de erro.
        string tmp = path + ".tmp";
        using (var fs = File.Create(tmp)) encoder.Save(fs);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Copia para a área de transferência, com novas tentativas se ela estiver ocupada.</summary>
    public static bool CopyToClipboard(BitmapSource image)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetImage(image);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(60);
            }
        }
        Log.Error("Não foi possível abrir a área de transferência.");
        return false;
    }

    public static bool CopyText(string text)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(60);
            }
        }
        return false;
    }

    /// <summary>Abre o Explorer com o arquivo selecionado.</summary>
    public static void ShowInFolder(string path)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { "/select,", path },
            UseShellExecute = false,
        });
    }

    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { folder },
            UseShellExecute = false,
        });
    }
}
