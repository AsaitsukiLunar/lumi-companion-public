// Version: 0.3.2-preview; Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// LOCAL-WORK / Windows / Codex Desktop / Lumi body-hit subtask / Codex
// Original source: user body/cable separation requirement; unchanged existing Lumi PNG alpha/color.
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LumiCompanion.Animation;

public enum SpriteHitPart : byte { Transparent, Body, Cable }

/// <summary>Interaction-only segmentation; never changes the rendered PNG.</summary>
public sealed class SpriteHitMask
{
    private readonly byte[] _parts;
    public int Width { get; }
    public int Height { get; }
    public int BodyPixelCount => _parts.Count(part => part == (byte)SpriteHitPart.Body);
    public int CablePixelCount => _parts.Count(part => part == (byte)SpriteHitPart.Cable);

    private SpriteHitMask(int width, int height, byte[] parts) { Width = width; Height = height; _parts = parts; }

    public SpriteHitPart GetPart(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height
        ? SpriteHitPart.Transparent : (SpriteHitPart)_parts[y * Width + x];

    public SpriteHitPart GetScaledPart(int x, int y, int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0) return SpriteHitPart.Transparent;
        double scale = Math.Min(pixelWidth / (double)Width, pixelHeight / (double)Height);
        double offsetX = (pixelWidth - Width * scale) / 2, offsetY = (pixelHeight - Height * scale) / 2;
        return GetPart((int)Math.Floor((x + .5 - offsetX) / scale), (int)Math.Floor((y + .5 - offsetY) / scale));
    }

    public byte[] CreateBodyRegionData(int pixelWidth, int pixelHeight)
    {
        var runs = new List<(int X, int Y, int Right)>();
        for (int y = 0; y < pixelHeight; y++)
        {
            int start = -1;
            for (int x = 0; x <= pixelWidth; x++)
            {
                bool body = x < pixelWidth && GetScaledPart(x, y, pixelWidth, pixelHeight) == SpriteHitPart.Body;
                if (body && start < 0) start = x;
                else if (!body && start >= 0) { runs.Add((start, y, x)); start = -1; }
            }
        }
        var data = new byte[32 + runs.Count * 16];
        void Put(int offset, int value) => BitConverter.GetBytes(value).CopyTo(data, offset);
        Put(0, 32); Put(4, 1); Put(8, runs.Count); Put(12, runs.Count * 16);
        Put(16, 0); Put(20, 0); Put(24, pixelWidth); Put(28, pixelHeight);
        for (int i = 0; i < runs.Count; i++)
        {
            Put(32 + i * 16, runs[i].X); Put(36 + i * 16, runs[i].Y);
            Put(40 + i * 16, runs[i].Right); Put(44 + i * 16, runs[i].Y + 1);
        }
        return data;
    }

    public static SpriteHitMask Create(BitmapSource source, string? frameKey = null)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = bitmap.PixelWidth, h = bitmap.PixelHeight, length = w * h;
        var pixels = new byte[length * 4];
        bitmap.CopyPixels(pixels, w * 4, 0);
        var opaque = new bool[length];
        var distance = new int[length];
        for (int i = 0; i < length; i++) { opaque[i] = pixels[i * 4 + 3] >= 32; distance[i] = opaque[i] ? w + h : 0; }
        // Chessboard distance to alpha background. Thin yellow cable has no thick interior.
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            if (!opaque[i]) continue;
            if (x == 0 || y == 0) distance[i] = 1;
            if (x > 0) distance[i] = Math.Min(distance[i], distance[i - 1] + 1);
            if (y > 0) distance[i] = Math.Min(distance[i], distance[i - w] + 1);
            if (x > 0 && y > 0) distance[i] = Math.Min(distance[i], distance[i - w - 1] + 1);
            if (x + 1 < w && y > 0) distance[i] = Math.Min(distance[i], distance[i - w + 1] + 1);
        }
        for (int y = h - 1; y >= 0; y--) for (int x = w - 1; x >= 0; x--)
        {
            int i = y * w + x;
            if (!opaque[i]) continue;
            if (x == w - 1 || y == h - 1) distance[i] = 1;
            if (x + 1 < w) distance[i] = Math.Min(distance[i], distance[i + 1] + 1);
            if (y + 1 < h) distance[i] = Math.Min(distance[i], distance[i + w] + 1);
            if (x + 1 < w && y + 1 < h) distance[i] = Math.Min(distance[i], distance[i + w + 1] + 1);
            if (x > 0 && y + 1 < h) distance[i] = Math.Min(distance[i], distance[i + w - 1] + 1);
        }
        int thinRadius = Math.Max(1, w / 96), cutRadius = Math.Max(1, w / 192);
        var cut = new bool[length];
        for (int i = 0; i < length; i++)
        {
            int p = i * 4, b = pixels[p], g = pixels[p + 1], r = pixels[p + 2];
            if (!opaque[i] || distance[i] > thinRadius || r < 100 || g < 85 || b > g * .78 || r > g * 1.65) continue;
            int x = i % w, y = i / w;
            for (int dy = -cutRadius; dy <= cutRadius; dy++) for (int dx = -cutRadius; dx <= cutRadius; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < w && ny < h) cut[ny * w + nx] = true;
            }
        }
        var component = new int[length];
        var sizes = new List<int> { 0 };
        var queue = new Queue<int>();
        for (int seed = 0; seed < length; seed++)
        {
            if (!opaque[seed] || cut[seed] || component[seed] != 0) continue;
            int label = sizes.Count, count = 0;
            component[seed] = label; queue.Enqueue(seed);
            while (queue.TryDequeue(out int at))
            {
                count++; int x = at % w, y = at / w;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int next = ny * w + nx;
                    if (!opaque[next] || cut[next] || component[next] != 0) continue;
                    component[next] = label; queue.Enqueue(next);
                }
            }
            sizes.Add(count);
        }
        int largest = sizes.Count <= 1 ? 0 : Enumerable.Range(1, sizes.Count - 1).MaxBy(label => sizes[label]);
        var body = new bool[length];
        for (int i = 0; i < length; i++) body[i] = largest != 0 && component[i] == largest;
        // Restore nearby detached fingers/trim as whole components, without reconstructing the cut cable.
        int joinRadius = Math.Max(2, w / 64);
        var keep = new HashSet<int> { largest };
        for (int i = 0; i < length; i++)
        {
            if (!body[i]) continue;
            int x = i % w, y = i / w;
            for (int dy = -joinRadius; dy <= joinRadius; dy++) for (int dx = -joinRadius; dx <= joinRadius; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < w && ny < h && component[ny * w + nx] > 0)
                    keep.Add(component[ny * w + nx]);
            }
        }
        for (int i = 0; i < length; i++) body[i] = component[i] > 0 && keep.Contains(component[i]);
        var parts = new byte[length];
        int restoreRadius = Math.Max(1, w / 128);
        for (int i = 0; i < length; i++)
        {
            if (!opaque[i]) continue;
            bool hit = body[i];
            int x = i % w, y = i / w;
            for (int dy = -restoreRadius; !hit && dy <= restoreRadius; dy++) for (int dx = -restoreRadius; !hit && dx <= restoreRadius; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < w && ny < h && body[ny * w + nx]) hit = true;
            }
            parts[i] = (byte)(hit ? SpriteHitPart.Body : SpriteHitPart.Cable);
        }
        // These five accepted source frames put the broad connector next to a raised boot.
        // Preserve the boot and classify the connector explicitly rather than joining by proximity.
        int? leftConnectorBoundary = frameKey switch
        {
            "running-left/00" => 241,
            "running-left/02" => 247,
            "running-left/04" => 238,
            _ => null
        };
        int? rightConnectorBoundary = frameKey switch
        {
            "running-right/01" => 137,
            "running-right/05" => 141,
            _ => null
        };
        if (leftConnectorBoundary.HasValue || rightConnectorBoundary.HasValue)
        {
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                double designX = x * 384.0 / w, designY = y * 416.0 / h;
                bool connector = leftConnectorBoundary is { } left && designY >= 300 && designX >= left ||
                                 rightConnectorBoundary is { } right && designY >= 280 && designX <= right;
                if (connector && parts[y * w + x] != 0) parts[y * w + x] = (byte)SpriteHitPart.Cable;
            }
        }
        return new SpriteHitMask(w, h, parts);
    }

    public BitmapSource CreateDiagnosticOverlay()
    {
        var pixels = new byte[Width * Height * 4];
        for (int i = 0; i < _parts.Length; i++)
        {
            if (_parts[i] == 0) continue;
            pixels[i * 4 + 1] = _parts[i] == (byte)SpriteHitPart.Body ? (byte)220 : (byte)60;
            pixels[i * 4 + 2] = _parts[i] == (byte)SpriteHitPart.Cable ? (byte)250 : (byte)30;
            pixels[i * 4 + 3] = 150;
        }
        return BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, pixels, Width * 4);
    }
}
