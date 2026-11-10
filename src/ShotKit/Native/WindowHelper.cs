using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using static ShotKit.Native.NativeMethods;

namespace ShotKit.Native;

/// <summary>Posicionamento de janelas WPF em pixels físicos e estilos estendidos.</summary>
internal static class WindowHelper
{
    public static IntPtr Handle(Window w) => new WindowInteropHelper(w).EnsureHandle();

    /// <summary>Move/redimensiona a janela em pixels físicos (ignora o DPI do WPF).</summary>
    public static void SetBoundsPx(Window w, Int32Rect r, bool topmost = true)
    {
        SetWindowPos(Handle(w), topmost ? HWND_TOPMOST : IntPtr.Zero, r.X, r.Y, r.Width, r.Height,
            SWP_NOACTIVATE | (topmost ? 0 : SWP_NOZORDER));
    }

    public static Int32Rect GetBoundsPx(Window w)
    {
        GetWindowRect(Handle(w), out var rc);
        return new Int32Rect(rc.Left, rc.Top, rc.Width, rc.Height);
    }

    /// <summary>Fator DIP→pixel atual da janela (1.0 = 96 DPI).</summary>
    public static double DpiScale(Visual v) => VisualTreeHelper.GetDpi(v).DpiScaleX;

    /// <summary>Janela que não recebe cliques nem foco, e não aparece no Alt+Tab.</summary>
    public static void MakeClickThrough(Window w)
    {
        IntPtr h = Handle(w);
        long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
    }

    /// <summary>Exclui a janela de capturas de tela (Windows 10 2004+). Falha silenciosa em versões antigas.</summary>
    public static void ExcludeFromCapture(Window w) => SetWindowDisplayAffinity(Handle(w), WDA_EXCLUDEFROMCAPTURE);

    /// <summary>
    /// Impede o WPF de redimensionar a janela quando ela muda de monitor/DPI
    /// (mantemos o tamanho em pixels físicos manualmente).
    /// </summary>
    public static void SuppressDpiResize(Window w)
    {
        var src = HwndSource.FromHwnd(Handle(w));
        src?.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
        {
            if (msg == WM_DPICHANGED) handled = true;
            return IntPtr.Zero;
        });
    }

    public static Int32Rect MonitorAt(int x, int y)
    {
        IntPtr hmon = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hmon, ref info);
        var m = info.rcMonitor;
        return new Int32Rect(m.Left, m.Top, m.Width, m.Height);
    }

    public static Int32Rect WorkAreaAt(int x, int y)
    {
        IntPtr hmon = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hmon, ref info);
        var m = info.rcWork;
        return new Int32Rect(m.Left, m.Top, m.Width, m.Height);
    }

    public static (int X, int Y) CursorPx()
    {
        GetCursorPos(out var p);
        return (p.X, p.Y);
    }
}
