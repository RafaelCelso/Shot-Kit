using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using ShotKit.Native;
using ShotKit.Views;
using static ShotKit.Native.NativeMethods;

namespace ShotKit.Services;

/// <summary>
/// Captura com rolagem automática: rola o conteúdo sob a região com a roda do mouse (SendInput),
/// captura cada quadro e junta tudo com o <see cref="ScrollStitcher"/>.
/// </summary>
public sealed class ScrollCaptureSession
{
    private const int MaxHeight = 30000;
    private const int MaxFrames = 300;
    private const int SettleDelayMs = 320;

    private readonly Int32Rect _region;
    private bool _stopRequested;
    private bool _cancelled;

    public ScrollCaptureSession(Int32Rect region) => _region = region;

    /// <summary>Executa a captura; retorna a imagem final ou null se cancelada.</summary>
    public async Task<(BitmapSource? Image, string? Warning)> RunAsync()
    {
        var frame = new ScrollFrameWindow(_region);
        var panel = new ScrollControlPanel(_region);
        panel.DoneClicked += () => _stopRequested = true;
        panel.CancelClicked += () => { _cancelled = true; _stopRequested = true; };
        frame.Show();
        panel.Show();

        GetCursorPos(out var originalCursor);
        string? warning = null;
        try
        {
            await Task.Delay(150); // deixa as janelas auxiliares aparecerem (já excluídas da captura)

            var stitcher = new ScrollStitcher(ScreenCapture.CaptureRegion(_region));
            int notches = _region.Height > 500 ? 3 : _region.Height > 250 ? 2 : 1;
            int noChange = 0;
            int lastHeight = stitcher.StitchedHeight;

            while (!_stopRequested)
            {
                panel.Status = $"Rolando... {stitcher.StitchedHeight} px ({stitcher.FrameCount} quadros)";
                Scroll(notches);
                await Task.Delay(SettleDelayMs);
                if (_stopRequested) break;

                var result = stitcher.Add(ScreenCapture.CaptureRegion(_region));
                if (result == ScrollStitcher.StepResult.NoChange)
                {
                    // Pode ser animação de rolagem suave ainda em andamento: tenta mais uma vez.
                    if (++noChange >= 2) break;
                    continue;
                }
                noChange = 0;

                if (result == ScrollStitcher.StepResult.MatchFailed)
                {
                    if (notches > 1) { notches--; continue; } // tenta passos menores
                    warning = "Não foi possível alinhar o conteúdo rolado (conteúdo animado ou fixo?). A imagem foi montada até aqui.";
                    break;
                }

                // Ajusta o passo: o ideal é rolar ~60% da altura por vez.
                int shift = stitcher.StitchedHeight - lastHeight;
                lastHeight = stitcher.StitchedHeight;
                if (shift > 0)
                {
                    double perNotch = shift / (double)notches;
                    notches = Math.Clamp((int)(_region.Height * 0.6 / perNotch), 1, 10);
                }

                if (stitcher.StitchedHeight >= MaxHeight || stitcher.FrameCount >= MaxFrames)
                {
                    warning = $"Limite de {MaxHeight} px atingido.";
                    break;
                }
            }

            if (_cancelled) return (null, null);
            panel.Status = "Montando imagem...";
            await Task.Yield();
            return (stitcher.Compose(), warning);
        }
        finally
        {
            SetCursorPos(originalCursor.X, originalCursor.Y);
            frame.Close();
            panel.Close();
        }
    }

    /// <summary>Move o cursor para o centro da região e envia "notches" cliques de roda para baixo.</summary>
    private void Scroll(int notches)
    {
        int cx = _region.X + _region.Width / 2;
        int cy = _region.Y + _region.Height / 2;
        SetCursorPos(cx, cy);

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT { mouseData = -WHEEL_DELTA * notches, dwFlags = MOUSEEVENTF_WHEEL },
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }
}
