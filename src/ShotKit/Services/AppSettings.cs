using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace ShotKit.Services;

public enum ImageFormatKind { Png, Jpg, Bmp }

/// <summary>Ação disparada por um atalho global.</summary>
public enum HotkeyAction { CaptureArea, CaptureOcr, CaptureFullScreen, CaptureScrolling }

public sealed class HotkeySetting
{
    public HotkeyAction Action { get; set; }
    public ModifierKeys Modifiers { get; set; }
    public Key Key { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Key == Key.None;

    public override string ToString() => Format(Modifiers, Key);

    public static string Format(ModifierKeys mods, Key key)
    {
        if (key == Key.None) return "(nenhum)";
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key == Key.Snapshot ? "PrintScreen" : key.ToString());
        return string.Join(" + ", parts);
    }
}

/// <summary>Configurações persistidas em %LOCALAPPDATA%\ShotKit\settings.json.</summary>
public sealed class AppSettings
{
    public string SaveFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ShotKit");

    public ImageFormatKind SaveFormat { get; set; } = ImageFormatKind.Png;
    public int JpegQuality { get; set; } = 92;
    public bool PlaySound { get; set; } = true;
    public bool CopyToClipboardAfterSave { get; set; } = false;
    public bool LaunchOnStartup { get; set; } = false;

    /// <summary>Tag BCP-47 do idioma do OCR; vazio = idioma do perfil do usuário.</summary>
    public string OcrLanguage { get; set; } = "";

    public List<HotkeySetting> Hotkeys { get; set; } = DefaultHotkeys();

    public AnnotationStyle Style { get; set; } = new();

    public static List<HotkeySetting> DefaultHotkeys() =>
    [
        new() { Action = HotkeyAction.CaptureArea, Modifiers = ModifierKeys.None, Key = Key.Snapshot },
        new() { Action = HotkeyAction.CaptureOcr, Modifiers = ModifierKeys.Control | ModifierKeys.Alt, Key = Key.O },
        new() { Action = HotkeyAction.CaptureFullScreen, Modifiers = ModifierKeys.Control | ModifierKeys.Alt, Key = Key.F },
        new() { Action = HotkeyAction.CaptureScrolling, Modifiers = ModifierKeys.Control | ModifierKeys.Alt, Key = Key.R },
    ];

    // ---------- Persistência ----------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShotKit");

    private static string FilePath => Path.Combine(DataFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (s != null)
                {
                    // Garante que toda ação tenha uma entrada de atalho.
                    foreach (var def in DefaultHotkeys())
                        if (s.Hotkeys.All(h => h.Action != def.Action)) s.Hotkeys.Add(def);
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao ler configurações; usando padrão.", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(DataFolder);
        // Escrita atômica: grava em arquivo temporário e substitui.
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
}

/// <summary>Estilo atual das ferramentas de anotação.</summary>
public sealed class AnnotationStyle
{
    public string Color { get; set; } = "#FFE53935";
    public double StrokeThickness { get; set; } = 3;
    public double FontSize { get; set; } = 20;
    public int MosaicBlockSize { get; set; } = 12;
    public bool Filled { get; set; } = false;
}
