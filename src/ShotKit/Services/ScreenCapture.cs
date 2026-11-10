using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static ShotKit.Native.NativeMethods;

namespace ShotKit.Services;

/// <summary>Pixels BGRX (4 bytes por pixel, alfa ignorado) de uma região da tela.</summary>
public sealed class PixelBuffer
{
    public PixelBuffer(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public int Stride => Width * 4;

    public BitmapSource ToBitmapSource()
    {
        var bmp = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgr32, null, Pixels, Stride);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Cor do pixel (x, y) no formato #RRGGBB.</summary>
    public string HexAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return "#000000";
        int i = y * Stride + x * 4;
        return $"#{Pixels[i + 2]:X2}{Pixels[i + 1]:X2}{Pixels[i]:X2}";
    }
}

/// <summary>Instantâneo da área de trabalho virtual (todos os monitores).</summary>
public sealed class ScreenSnapshot
{
    public required PixelBuffer Buffer { get; init; }
    public required BitmapSource Image { get; init; }

    /// <summary>Origem da área de trabalho virtual em pixels físicos (pode ser negativa).</summary>
    public required Int32Rect VirtualBounds { get; init; }

    /// <summary>Retângulos de janelas/monitores, relativos ao instantâneo, do topo para o fundo.</summary>
    public required IReadOnlyList<Int32Rect> HitRects { get; init; }

    /// <summary>Retorna o primeiro retângulo (janela mais ao topo) que contém o ponto.</summary>
    public Int32Rect? HitTest(int x, int y)
    {
        foreach (var r in HitRects)
        {
            if (x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height) return r;
        }
        return null;
    }
}

public static class ScreenCapture
{
    public static Int32Rect GetVirtualScreen() => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN),
        GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN),
        GetSystemMetrics(SM_CYVIRTUALSCREEN));

    /// <summary>Captura uma região em coordenadas absolutas de tela (pixels físicos).</summary>
    public static PixelBuffer CaptureRegion(Int32Rect r)
    {
        if (r.Width <= 0 || r.Height <= 0) throw new ArgumentException("Região vazia.", nameof(r));

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hbmp = CreateCompatibleBitmap(screenDc, r.Width, r.Height);
        IntPtr old = SelectObject(memDc, hbmp);
        try
        {
            if (!BitBlt(memDc, 0, 0, r.Width, r.Height, screenDc, r.X, r.Y, SRCCOPY | CAPTUREBLT))
                throw new InvalidOperationException("BitBlt falhou: " + Marshal.GetLastWin32Error());

            SelectObject(memDc, old);
            var info = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = r.Width,
                biHeight = -r.Height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            };
            var pixels = new byte[r.Width * r.Height * 4];
            if (GetDIBits(memDc, hbmp, 0, (uint)r.Height, pixels, ref info, DIB_RGB_COLORS) == 0)
                throw new InvalidOperationException("GetDIBits falhou.");
            return new PixelBuffer(r.Width, r.Height, pixels);
        }
        finally
        {
            DeleteObject(hbmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Captura todos os monitores e a geometria das janelas visíveis.</summary>
    public static ScreenSnapshot CaptureSnapshot()
    {
        var vs = GetVirtualScreen();
        var hitRects = new List<Int32Rect>();
        hitRects.AddRange(EnumerateWindowRects(vs));
        hitRects.AddRange(EnumerateMonitorRects(vs));

        var buffer = CaptureRegion(vs);
        return new ScreenSnapshot
        {
            Buffer = buffer,
            Image = buffer.ToBitmapSource(),
            VirtualBounds = vs,
            HitRects = hitRects,
        };
    }

    public static IReadOnlyList<Int32Rect> GetMonitorRects()
    {
        var list = new List<Int32Rect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT rc, IntPtr _) =>
        {
            list.Add(new Int32Rect(rc.Left, rc.Top, rc.Width, rc.Height));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static IEnumerable<Int32Rect> EnumerateMonitorRects(Int32Rect vs) =>
        GetMonitorRects().Select(m => new Int32Rect(m.X - vs.X, m.Y - vs.Y, m.Width, m.Height));

    /// <summary>Janelas de nível superior visíveis, em ordem Z (topo primeiro).</summary>
    private static List<Int32Rect> EnumerateWindowRects(Int32Rect vs)
    {
        var result = new List<Int32Rect>();
        var className = new StringBuilder(256);

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;

            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            className.Clear();
            GetClassName(hwnd, className, className.Capacity);
            string cls = className.ToString();
            if (cls is "Progman" or "WorkerW") return true; // área de trabalho

            // Limites visíveis reais (sem a sombra invisível do Windows 10/11).
            RECT rc;
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rc, Marshal.SizeOf<RECT>()) != 0
                && !GetWindowRect(hwnd, out rc))
                return true;

            if (rc.Width < 8 || rc.Height < 8) return true;

            // Converte para coordenadas do instantâneo e recorta aos limites.
            int left = Math.Max(rc.Left - vs.X, 0);
            int top = Math.Max(rc.Top - vs.Y, 0);
            int right = Math.Min(rc.Right - vs.X, vs.Width);
            int bottom = Math.Min(rc.Bottom - vs.Y, vs.Height);
            if (right - left >= 8 && bottom - top >= 8)
                result.Add(new Int32Rect(left, top, right - left, bottom - top));
            return true;
        }, IntPtr.Zero);

        return result;
    }
}
