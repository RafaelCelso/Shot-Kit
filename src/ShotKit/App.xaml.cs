using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShotKit.Native;
using ShotKit.Services;
using ShotKit.Views;
using WinForms = System.Windows.Forms;

namespace ShotKit;

/// <summary>
/// Ponto central do app: ícone na bandeja, atalhos globais, instância única
/// e encaminhamento dos resultados da captura (copiar, salvar, fixar, OCR, rolagem).
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\ShotKit-SingleInstance-7C1E2B9A";
    private const string ActivateEventName = @"Local\ShotKit-Activate-7C1E2B9A";

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private WinForms.NotifyIcon? _tray;
    private HotkeyManager? _hotkeys;
    private IReadOnlyList<HotkeySetting> _failedHotkeys = [];
    private bool _busy;
    private string? _lastSavedPath;

    public AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Instância única: uma segunda execução apenas pede uma captura à primeira.
        _mutex = new Mutex(true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            try { EventWaitHandle.OpenExisting(ActivateEventName).Set(); } catch { /* primeira instância encerrando */ }
            Shutdown();
            return;
        }
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        var listener = new Thread(() =>
        {
            while (_activateEvent.WaitOne())
                Dispatcher.BeginInvoke(() => _ = StartCaptureAsync(CaptureMode.Edit));
        }) { IsBackground = true, Name = "ShotKit activation" };
        listener.Start();

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Exceção não tratada na interface.", args.Exception);
            ShowBalloon("Ocorreu um erro: " + args.Exception.Message, WinForms.ToolTipIcon.Error);
            _busy = false;
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Exceção não tratada.", args.ExceptionObject as Exception);

        Settings = AppSettings.Load();
        bool firstRun = !File.Exists(Path.Combine(AppSettings.DataFolder, "settings.json"));
        if (firstRun) TrySaveSettings();

        CreateTray();

        _hotkeys = new HotkeyManager();
        _hotkeys.Triggered += action => _ = OnHotkeyAsync(action);
        RegisterHotkeys(notify: true);

        Log.Info("ShotKit iniciado.");

        if (e.Args.Contains("--capture", StringComparer.OrdinalIgnoreCase))
            _ = StartCaptureAsync(CaptureMode.Edit);
        else if (firstRun)
            ShowBalloon($"O ShotKit está na bandeja do sistema. Use {HotkeyText(HotkeyAction.CaptureArea)} para capturar uma área.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _activateEvent?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    // ------------------------------------------------------------------ Bandeja

    private void CreateTray()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Capturar área", null, (_, _) => _ = StartCaptureAsync(CaptureMode.Edit));
        menu.Items.Add("Capturar tela inteira", null, (_, _) => _ = CaptureFullScreenAsync());
        menu.Items.Add("Captura com rolagem", null, (_, _) => _ = StartCaptureAsync(CaptureMode.Scrolling));
        menu.Items.Add("Extrair texto (OCR)", null, (_, _) => _ = StartCaptureAsync(CaptureMode.Ocr));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Abrir pasta das capturas", null, (_, _) => ImageExporter.OpenFolder(Settings.SaveFolder));
        menu.Items.Add("Configurações...", null, (_, _) => ShowSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => Shutdown());

        _tray = new WinForms.NotifyIcon
        {
            Icon = AppIcon.TrayIcon,
            Text = "ShotKit",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) _ = StartCaptureAsync(CaptureMode.Edit);
        };
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_lastSavedPath != null && File.Exists(_lastSavedPath)) ImageExporter.ShowInFolder(_lastSavedPath);
        };
    }

    private void ShowBalloon(string text, WinForms.ToolTipIcon icon = WinForms.ToolTipIcon.Info)
    {
        _tray?.ShowBalloonTip(3000, "ShotKit", text, icon);
    }

    // ------------------------------------------------------------------ Atalhos

    private void RegisterHotkeys(bool notify)
    {
        if (_hotkeys == null) return;
        _failedHotkeys = _hotkeys.RegisterAll(Settings.Hotkeys);
        if (notify && _failedHotkeys.Count > 0)
        {
            ShowBalloon("Atalhos em uso por outro programa: " + string.Join(", ", _failedHotkeys.Select(h => h.ToString())) +
                        ". Altere-os em Configurações.", WinForms.ToolTipIcon.Warning);
        }
    }

    private string HotkeyText(HotkeyAction action) =>
        Settings.Hotkeys.FirstOrDefault(h => h.Action == action)?.ToString() ?? "o ícone da bandeja";

    private Task OnHotkeyAsync(HotkeyAction action) => action switch
    {
        HotkeyAction.CaptureArea => StartCaptureAsync(CaptureMode.Edit),
        HotkeyAction.CaptureOcr => StartCaptureAsync(CaptureMode.Ocr),
        HotkeyAction.CaptureScrolling => StartCaptureAsync(CaptureMode.Scrolling),
        HotkeyAction.CaptureFullScreen => CaptureFullScreenAsync(),
        _ => Task.CompletedTask,
    };

    // ------------------------------------------------------------------ Captura

    public async Task StartCaptureAsync(CaptureMode mode)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            // Dá tempo para o menu da bandeja sumir antes do instantâneo.
            await Task.Delay(120);
            var snapshot = ScreenCapture.CaptureSnapshot();
            var overlay = new CaptureOverlay(snapshot, mode, Settings);
            overlay.ShowDialog();

            var result = overlay.Result;
            TrySaveSettings(); // estilo de anotação escolhido

            if (result.Outcome == OverlayOutcome.Scrolling)
            {
                _busy = false; // a sessão de rolagem tem seu próprio fluxo
                await RunScrollingAsync(result.ScreenRect);
                return;
            }
            HandleResult(result);
        }
        catch (Exception ex)
        {
            Log.Error("Falha na captura.", ex);
            ShowBalloon("Falha na captura: " + ex.Message, WinForms.ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    private void HandleResult(OverlayResult result)
    {
        if (result.Image is not { } image) return;
        switch (result.Outcome)
        {
            case OverlayOutcome.Copy:
                if (Settings.PlaySound) ShutterSound.Play();
                if (!ImageExporter.CopyToClipboard(image))
                    ShowBalloon("Não foi possível acessar a área de transferência.", WinForms.ToolTipIcon.Warning);
                break;

            case OverlayOutcome.Save:
                if (Settings.PlaySound) ShutterSound.Play();
                SaveImage(image);
                break;

            case OverlayOutcome.Pin:
                new PinWindow(image, result.ScreenRect, Settings).Show();
                break;

            case OverlayOutcome.Ocr:
                new OcrWindow(image, Settings).Show();
                break;
        }
    }

    private void SaveImage(BitmapSource image)
    {
        try
        {
            string path = ImageExporter.SaveToDefaultFolder(image, Settings);
            if (Settings.CopyToClipboardAfterSave) ImageExporter.CopyToClipboard(image);
            _lastSavedPath = path;
            ShowBalloon($"Salvo em {path}\nClique para abrir a pasta.");
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao salvar.", ex);
            ShowBalloon("Não foi possível salvar: " + ex.Message, WinForms.ToolTipIcon.Error);
        }
    }

    /// <summary>Captura o monitor sob o cursor e salva direto (também copia).</summary>
    private async Task CaptureFullScreenAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await Task.Delay(120);
            var (x, y) = WindowHelper.CursorPx();
            var monitor = WindowHelper.MonitorAt(x, y);
            var image = ScreenCapture.CaptureRegion(monitor).ToBitmapSource();
            if (Settings.PlaySound) ShutterSound.Play();
            ImageExporter.CopyToClipboard(image);
            SaveImage(image);
        }
        catch (Exception ex)
        {
            Log.Error("Falha na captura de tela inteira.", ex);
            ShowBalloon("Falha na captura: " + ex.Message, WinForms.ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RunScrollingAsync(Int32Rect region)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (region.Height < 40 || region.Width < 40)
            {
                ShowBalloon("Selecione uma região maior para a captura com rolagem.", WinForms.ToolTipIcon.Warning);
                return;
            }
            var (image, warning) = await new ScrollCaptureSession(region).RunAsync();
            if (image == null) return;
            if (Settings.PlaySound) ShutterSound.Play();
            var window = new ResultWindow(image, Settings, warning);
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            Log.Error("Falha na captura com rolagem.", ex);
            ShowBalloon("Falha na captura com rolagem: " + ex.Message, WinForms.ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    // ------------------------------------------------------------------ Configurações

    private SettingsWindow? _settingsWindow;

    private void ShowSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        // Libera os atalhos enquanto o usuário edita, para poder capturar as combinações.
        _hotkeys?.UnregisterAll();
        _settingsWindow = new SettingsWindow(Settings, _failedHotkeys);
        try
        {
            if (_settingsWindow.ShowDialog() == true && _settingsWindow.Saved is { } saved)
            {
                bool startupChanged = saved.LaunchOnStartup != Settings.LaunchOnStartup;
                saved.Style = Settings.Style;
                Settings = saved;
                TrySaveSettings();
                if (startupChanged) StartupRegistration.Apply(Settings.LaunchOnStartup);
            }
        }
        finally
        {
            _settingsWindow = null;
            RegisterHotkeys(notify: true);
        }
    }

    private void TrySaveSettings()
    {
        try { Settings.Save(); }
        catch (Exception ex) { Log.Error("Falha ao salvar configurações.", ex); }
    }
}
