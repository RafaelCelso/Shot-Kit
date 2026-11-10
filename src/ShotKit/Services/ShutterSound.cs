using System.IO;
using System.Media;

namespace ShotKit.Services;

/// <summary>Som de obturador sintetizado em memória (sem arquivos de áudio de terceiros).</summary>
public static class ShutterSound
{
    private static readonly Lazy<byte[]> Wav = new(Build);

    public static void Play()
    {
        try
        {
            using var player = new SoundPlayer(new MemoryStream(Wav.Value));
            player.Play(); // assíncrono
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao tocar som.", ex);
        }
    }

    /// <summary>Dois cliques curtos de ruído com envelope exponencial (~120 ms).</summary>
    private static byte[] Build()
    {
        const int rate = 22050;
        int samples = rate * 120 / 1000;
        var rnd = new Random(7);
        var data = new short[samples];
        int second = rate * 55 / 1000;

        for (int i = 0; i < samples; i++)
        {
            double t1 = i / (double)rate;
            double env = Math.Exp(-t1 * 90);
            if (i >= second)
            {
                double t2 = (i - second) / (double)rate;
                env += 0.7 * Math.Exp(-t2 * 110);
            }
            double noise = rnd.NextDouble() * 2 - 1;
            data[i] = (short)Math.Clamp(noise * env * 9000, short.MinValue, short.MaxValue);
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = samples * 2;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);     // PCM
        w.Write((short)1);     // mono
        w.Write(rate);
        w.Write(rate * 2);     // byte rate
        w.Write((short)2);     // block align
        w.Write((short)16);    // bits
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);
        foreach (short s in data) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }
}
