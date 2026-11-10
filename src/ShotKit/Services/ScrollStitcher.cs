using System.Windows.Media.Imaging;

namespace ShotKit.Services;

/// <summary>
/// Junta quadros sucessivos de uma região rolada verticalmente numa imagem longa.
///
/// Para cada par de quadros consecutivos:
///  1. calcula uma assinatura (hash) por linha, ignorando a faixa da barra de rolagem à direita;
///  2. detecta cabeçalho e rodapé fixos (linhas iguais na mesma posição nos dois quadros);
///  3. procura o deslocamento vertical d que maximiza a coincidência das linhas "informativas"
///     (não uniformes) entre o corpo do quadro anterior e o do novo;
///  4. anexa ao resultado apenas as d linhas novas do corpo.
/// </summary>
public sealed class ScrollStitcher
{
    public enum StepResult { Appended, NoChange, MatchFailed }

    private const int ScrollbarMargin = 28;
    private const double MinScore = 0.80;
    private const int MinInformativeRows = 4;

    private readonly int _width;
    private readonly int _height;
    private readonly List<PixelBuffer> _frames = new();
    private readonly List<(int Shift, int Footer)> _steps = new();
    private ulong[] _lastHashes;

    public ScrollStitcher(PixelBuffer first)
    {
        _width = first.Width;
        _height = first.Height;
        _frames.Add(first);
        _lastHashes = RowSignatures(first).Hashes;
    }

    public int FrameCount => _frames.Count;

    /// <summary>Altura que a imagem final terá com os quadros aceitos até agora.</summary>
    public int StitchedHeight => _height + _steps.Sum(s => s.Shift);

    public StepResult Add(PixelBuffer frame)
    {
        if (frame.Width != _width || frame.Height != _height)
            throw new ArgumentException("Todos os quadros devem ter o mesmo tamanho.");

        var (hashes, informative) = RowSignatures(frame);
        int h = _height;

        // Cabeçalho fixo.
        int top = 0;
        while (top < h && hashes[top] == _lastHashes[top]) top++;
        if (top == h) return StepResult.NoChange; // nada mudou: chegou ao fim

        // Rodapé fixo.
        int bottom = 0;
        while (bottom < h - top && hashes[h - 1 - bottom] == _lastHashes[h - 1 - bottom]) bottom++;

        int bodyEnd = h - bottom;
        int bodyLen = bodyEnd - top;

        int bestShift = -1;
        double bestScore = 0;

        // d = quantas linhas o conteúdo subiu. Exige sobreposição mínima de 1/8 do corpo.
        int maxShift = bodyLen - Math.Max(8, bodyLen / 8);
        for (int d = 1; d <= maxShift; d++)
        {
            int matches = 0, considered = 0;
            for (int i = top; i < bodyEnd - d; i++)
            {
                // Linha i do novo quadro deve ser igual à linha i + d do anterior.
                if (!informative[i]) continue;
                considered++;
                if (hashes[i] == _lastHashes[i + d]) matches++;
            }
            if (considered < MinInformativeRows) continue;
            double score = matches / (double)considered;
            if (score > bestScore + 1e-9)
            {
                bestScore = score;
                bestShift = d;
                if (score >= 0.999) break; // coincidência perfeita com o menor d
            }
        }

        if (bestShift < 0 || bestScore < MinScore) return StepResult.MatchFailed;

        _frames.Add(frame);
        _steps.Add((bestShift, bottom));
        _lastHashes = hashes;
        return StepResult.Appended;
    }

    /// <summary>Monta a imagem final.</summary>
    public BitmapSource Compose()
    {
        // Usa o rodapé detectado no primeiro passo para toda a montagem.
        int footer = _steps.Count > 0 ? _steps[0].Footer : 0;
        int h = _height;
        int stride = _width * 4;
        int total = StitchedHeight;
        var output = new byte[total * stride];

        // Quadro 0 sem o rodapé.
        int y = 0;
        CopyRows(_frames[0], 0, h - footer, output, ref y, stride);

        // Para cada quadro seguinte, apenas as linhas novas imediatamente acima do rodapé.
        for (int k = 0; k < _steps.Count; k++)
        {
            int d = _steps[k].Shift;
            CopyRows(_frames[k + 1], h - footer - d, h - footer, output, ref y, stride);
        }

        // Rodapé do último quadro.
        CopyRows(_frames[^1], h - footer, h, output, ref y, stride);

        var result = new PixelBuffer(_width, y, y == total ? output : output[..(y * stride)]);
        return result.ToBitmapSource();
    }

    private static void CopyRows(PixelBuffer src, int from, int to, byte[] dst, ref int dstRow, int stride)
    {
        from = Math.Max(0, from);
        to = Math.Min(src.Height, to);
        if (to <= from) return;
        Buffer.BlockCopy(src.Pixels, from * stride, dst, dstRow * stride, (to - from) * stride);
        dstRow += to - from;
    }

    /// <summary>Hash FNV-1a por linha + indicador de linha não uniforme.</summary>
    private static (ulong[] Hashes, bool[] Informative) RowSignatures(PixelBuffer f)
    {
        int usable = f.Width > ScrollbarMargin * 3 ? f.Width - ScrollbarMargin : f.Width;
        var hashes = new ulong[f.Height];
        var informative = new bool[f.Height];

        for (int y = 0; y < f.Height; y++)
        {
            int row = y * f.Stride;
            ulong hash = 14695981039346656037UL;
            uint first = BitConverter.ToUInt32(f.Pixels, row) & 0x00FFFFFF;
            int distinct = 0;
            for (int x = 0; x < usable; x++)
            {
                int i = row + x * 4;
                uint px = (uint)(f.Pixels[i] | f.Pixels[i + 1] << 8 | f.Pixels[i + 2] << 16);
                if (px != first) distinct++;
                hash = (hash ^ px) * 1099511628211UL;
            }
            hashes[y] = hash;
            // Linhas quase lisas (fundo) não ajudam a decidir o deslocamento.
            informative[y] = distinct > usable / 50;
        }
        return (hashes, informative);
    }
}
