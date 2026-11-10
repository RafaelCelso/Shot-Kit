using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ShotKit.Services;

/// <summary>
/// OCR local com o motor nativo do Windows (Windows.Media.Ocr).
/// Usa os pacotes de idioma instalados no sistema; nada sai da máquina.
/// </summary>
public static class OcrService
{
    public sealed record OcrLanguageOption(string Tag, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    public static IReadOnlyList<OcrLanguageOption> AvailableLanguages() =>
        OcrEngine.AvailableRecognizerLanguages
            .Select(l => new OcrLanguageOption(l.LanguageTag, $"{l.NativeName} ({l.LanguageTag})"))
            .ToList();

    public static async Task<string> RecognizeAsync(BitmapSource image, string languageTag)
    {
        OcrEngine? engine = null;
        if (!string.IsNullOrWhiteSpace(languageTag))
        {
            var lang = new Language(languageTag);
            if (OcrEngine.IsLanguageSupported(lang)) engine = OcrEngine.TryCreateFromLanguage(lang);
        }
        engine ??= OcrEngine.TryCreateFromUserProfileLanguages()
                   ?? throw new InvalidOperationException(
                       "Nenhum idioma de OCR instalado. Adicione um idioma em Configurações do Windows > Hora e idioma.");

        int max = (int)OcrEngine.MaxImageDimension;

        // Imagens muito altas (captura com rolagem) são lidas em faixas, para não reduzir o texto.
        if (image.PixelHeight > max && image.PixelWidth <= max)
        {
            var parts = new List<string>();
            int sliceHeight = max - 200;
            for (int y = 0; y < image.PixelHeight; y += sliceHeight)
            {
                int h = Math.Min(max, image.PixelHeight - y);
                var slice = new CroppedBitmap(image, new System.Windows.Int32Rect(0, y, image.PixelWidth, h));
                slice.Freeze();
                parts.Add(await RecognizeSingleAsync(engine, slice, max));
                if (y + h >= image.PixelHeight) break;
            }
            return string.Join(Environment.NewLine, parts.Where(p => p.Length > 0));
        }

        return await RecognizeSingleAsync(engine, image, max);
    }

    private static async Task<string> RecognizeSingleAsync(OcrEngine engine, BitmapSource image, int max)
    {
        // Amplia imagens pequenas para melhorar a leitura; reduz as que passam do limite do motor.
        BitmapSource src = image;
        double scale = 1;
        if (src.PixelWidth < 400 && src.PixelHeight < 400) scale = 2;
        if (Math.Max(src.PixelWidth, src.PixelHeight) * scale > max)
            scale = max / (double)Math.Max(src.PixelWidth, src.PixelHeight);
        if (Math.Abs(scale - 1) > 0.01)
        {
            var scaled = new TransformedBitmap(src, new System.Windows.Media.ScaleTransform(scale, scale));
            scaled.Freeze();
            src = scaled;
        }

        using SoftwareBitmap bitmap = await ToSoftwareBitmapAsync(src);
        OcrResult result = await engine.RecognizeAsync(bitmap);

        var sb = new StringBuilder();
        foreach (var line in result.Lines)
        {
            // Idiomas CJK não usam espaço entre palavras.
            sb.AppendLine(IsCjk(engine.RecognizerLanguage.LanguageTag)
                ? string.Concat(line.Words.Select(w => w.Text))
                : line.Text);
        }
        return sb.ToString().TrimEnd();
    }

    private static bool IsCjk(string tag) =>
        tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
        tag.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ||
        tag.StartsWith("ko", StringComparison.OrdinalIgnoreCase);

    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;

        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
}
