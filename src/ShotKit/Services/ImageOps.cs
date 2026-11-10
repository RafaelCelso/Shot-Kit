using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShotKit.Services;

/// <summary>Operações de pixel usadas pelas ferramentas de mosaico e desfoque.</summary>
public static class ImageOps
{
    /// <summary>Recorta uma região de um buffer BGRX.</summary>
    public static PixelBuffer Crop(PixelBuffer src, Int32Rect r)
    {
        r = Clamp(r, src.Width, src.Height);
        var dst = new byte[r.Width * r.Height * 4];
        for (int y = 0; y < r.Height; y++)
        {
            Buffer.BlockCopy(src.Pixels, (r.Y + y) * src.Stride + r.X * 4, dst, y * r.Width * 4, r.Width * 4);
        }
        return new PixelBuffer(r.Width, r.Height, dst);
    }

    /// <summary>Mosaico: substitui cada bloco pela sua cor média.</summary>
    public static PixelBuffer Pixelate(PixelBuffer src, int block)
    {
        block = Math.Max(2, block);
        var dst = new byte[src.Pixels.Length];
        int stride = src.Stride;

        for (int by = 0; by < src.Height; by += block)
        {
            int bh = Math.Min(block, src.Height - by);
            for (int bx = 0; bx < src.Width; bx += block)
            {
                int bw = Math.Min(block, src.Width - bx);
                long b = 0, g = 0, r = 0;
                for (int y = by; y < by + bh; y++)
                {
                    int row = y * stride;
                    for (int x = bx; x < bx + bw; x++)
                    {
                        int i = row + x * 4;
                        b += src.Pixels[i];
                        g += src.Pixels[i + 1];
                        r += src.Pixels[i + 2];
                    }
                }
                int n = bw * bh;
                byte mb = (byte)(b / n), mg = (byte)(g / n), mr = (byte)(r / n);
                for (int y = by; y < by + bh; y++)
                {
                    int row = y * stride;
                    for (int x = bx; x < bx + bw; x++)
                    {
                        int i = row + x * 4;
                        dst[i] = mb;
                        dst[i + 1] = mg;
                        dst[i + 2] = mr;
                        dst[i + 3] = 255;
                    }
                }
            }
        }
        return new PixelBuffer(src.Width, src.Height, dst);
    }

    /// <summary>Desfoque aproximadamente gaussiano: três passadas de box blur separável.</summary>
    public static PixelBuffer Blur(PixelBuffer src, int radius)
    {
        radius = Math.Max(1, radius);
        var a = (byte[])src.Pixels.Clone();
        var tmp = new byte[a.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            BoxBlurHorizontal(a, tmp, src.Width, src.Height, radius);
            BoxBlurVertical(tmp, a, src.Width, src.Height, radius);
        }
        return new PixelBuffer(src.Width, src.Height, a);
    }

    private static void BoxBlurHorizontal(byte[] src, byte[] dst, int w, int h, int r)
    {
        int stride = w * 4;
        for (int y = 0; y < h; y++)
        {
            int row = y * stride;
            for (int c = 0; c < 3; c++)
            {
                int sum = 0;
                for (int k = -r; k <= r; k++) sum += src[row + Math.Clamp(k, 0, w - 1) * 4 + c];
                for (int x = 0; x < w; x++)
                {
                    dst[row + x * 4 + c] = (byte)(sum / (2 * r + 1));
                    int add = Math.Min(x + r + 1, w - 1);
                    int sub = Math.Max(x - r, 0);
                    sum += src[row + add * 4 + c] - src[row + sub * 4 + c];
                }
            }
            for (int x = 0; x < w; x++) dst[row + x * 4 + 3] = 255;
        }
    }

    private static void BoxBlurVertical(byte[] src, byte[] dst, int w, int h, int r)
    {
        int stride = w * 4;
        for (int x = 0; x < w; x++)
        {
            int col = x * 4;
            for (int c = 0; c < 3; c++)
            {
                int sum = 0;
                for (int k = -r; k <= r; k++) sum += src[Math.Clamp(k, 0, h - 1) * stride + col + c];
                for (int y = 0; y < h; y++)
                {
                    dst[y * stride + col + c] = (byte)(sum / (2 * r + 1));
                    int add = Math.Min(y + r + 1, h - 1);
                    int sub = Math.Max(y - r, 0);
                    sum += src[add * stride + col + c] - src[sub * stride + col + c];
                }
            }
            for (int y = 0; y < h; y++) dst[y * stride + col + 3] = 255;
        }
    }

    public static Int32Rect Clamp(Int32Rect r, int width, int height)
    {
        int x1 = Math.Clamp(r.X, 0, width);
        int y1 = Math.Clamp(r.Y, 0, height);
        int x2 = Math.Clamp(r.X + r.Width, 0, width);
        int y2 = Math.Clamp(r.Y + r.Height, 0, height);
        return new Int32Rect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
    }

    /// <summary>Converte qualquer BitmapSource para um PixelBuffer BGRX.</summary>
    public static PixelBuffer ToPixelBuffer(BitmapSource source)
    {
        BitmapSource bgr = source.Format == PixelFormats.Bgr32 || source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = bgr.PixelWidth, h = bgr.PixelHeight;
        var pixels = new byte[w * h * 4];
        bgr.CopyPixels(pixels, w * 4, 0);
        return new PixelBuffer(w, h, pixels);
    }
}
