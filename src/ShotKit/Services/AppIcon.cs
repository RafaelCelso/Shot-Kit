using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ShotKit.Services;

/// <summary>Ícone do app desenhado em código (moldura de seleção com cantos), sem arquivos de terceiros.</summary>
public static class AppIcon
{
    private static Icon? _icon;

    public static Icon TrayIcon => _icon ??= Create(32);

    public static BitmapSource WindowIcon
    {
        get
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(Create(64).Handle, System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
    }

    private static Icon Create(int size)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float s = size / 32f;

            // Fundo arredondado azul.
            using (var path = RoundedRect(new RectangleF(1 * s, 1 * s, 30 * s, 30 * s), 7 * s))
            using (var brush = new SolidBrush(Color.FromArgb(255, 47, 128, 237)))
                g.FillPath(brush, path);

            // Cantos de seleção brancos.
            using var pen = new Pen(Color.White, 3 * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float a = 8 * s, b = 24 * s, l = 6 * s;
            g.DrawLines(pen, new[] { new PointF(a, a + l), new PointF(a, a), new PointF(a + l, a) });
            g.DrawLines(pen, new[] { new PointF(b - l, a), new PointF(b, a), new PointF(b, a + l) });
            g.DrawLines(pen, new[] { new PointF(a, b - l), new PointF(a, b), new PointF(a + l, b) });
            g.DrawLines(pen, new[] { new PointF(b - l, b), new PointF(b, b), new PointF(b, b - l) });

            // Ponto central.
            using var dot = new SolidBrush(Color.White);
            g.FillEllipse(dot, 14 * s, 14 * s, 4 * s, 4 * s);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
