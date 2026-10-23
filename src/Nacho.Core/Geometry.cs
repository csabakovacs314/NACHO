using System.Globalization;

namespace Nacho.Core;

public static class Limits
{
    /// <summary>Google Docs refuses images above 25 megapixels.</summary>
    public const long MaxPixels = 25_000_000;

    /// <summary>The formats Google Docs accepts as they are.</summary>
    public static bool IsDocsFormat(string format) => format is "JPEG" or "PNG" or "GIF";

    /// <summary>
    /// The size to resize to: long edge at most <paramref name="longEdge"/>, never upscaled, under 25 megapixels,
    /// and the same shape as the original. In order of preference:
    /// 1. an exact ratio within 3% of the target size (4:3, 3:2, 16:9 and most camera sizes land here);
    /// 2. the largest size within 25% of the target whose shape is within 0.1%;
    /// 3. the closest shape within 1%;
    /// 4. an exact ratio at half the target size or more;
    /// 5. the original size, when it is under 25 megapixels (only very thin strips get here).
    /// </summary>
    public static (int Width, int Height) FitWithin(int width, int height, int longEdge)
    {
        if (width <= 0 || height <= 0) return (width, height);
        bool portrait = height > width;
        long lo = portrait ? height : width;
        long sh = portrait ? width : height;
        long cap = (long)Math.Floor(Math.Sqrt((double)MaxPixels * lo / sh));
        long target = Math.Min(Math.Min(longEdge, lo), cap);
        if (target >= lo) return (width, height);
        target = Math.Max(1, target);

        var (l, s) = Pick(lo, sh, target, mustShrink: lo * sh > MaxPixels);
        return portrait ? ((int)s, (int)l) : ((int)l, (int)s);
    }

    private static (long Long, long Short) Pick(long lo, long sh, long target, bool mustShrink)
    {
        long g = Gcd(lo, sh), rl = lo / g, rs = sh / g;
        long k = target / rl;
        if (k >= 1 && k * rl * 100 >= target * 97) return (k * rl, k * rs);

        long bestL = target, bestS = ShortFor(target, lo, sh);
        double bestErr = double.MaxValue;
        long floor = Math.Max(1, target * 3 / 4);
        for (long l = target; l >= floor; l--)
        {
            long s = ShortFor(l, lo, sh);
            if (l * s > MaxPixels) continue;
            double err = Shape.Error(lo, sh, l, s);
            if (err <= 0.001) return (l, s);
            if (err < bestErr)
            {
                bestErr = err;
                bestL = l;
                bestS = s;
            }
        }
        if (bestErr <= 0.01) return (bestL, bestS);
        if (k >= 1 && k * rl * 2 >= target) return (k * rl, k * rs);
        return mustShrink ? (bestL, bestS) : (lo, sh);
    }

    /// <summary>The short side for long side <paramref name="l"/>, rounded to the nearest pixel, at least 1.</summary>
    private static long ShortFor(long l, long lo, long sh) => Math.Max(1, (2 * l * sh + lo) / (2 * lo));

    private static long Gcd(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}

/// <summary>Compares and names image shapes (aspect ratios).</summary>
public static class Shape
{
    private static readonly (int A, int B)[] Common =
    {
        (1, 1), (5, 4), (4, 3), (3, 2), (16, 10), (16, 9), (2, 1), (21, 9), (3, 1),
    };

    /// <summary>How far apart two shapes are: 0 is identical, 0.001 is 0.1%.</summary>
    public static double Error(long w1, long h1, long w2, long h2)
    {
        if (w1 <= 0 || h1 <= 0 || w2 <= 0 || h2 <= 0) return 0;
        long a = w2 * h1, b = h2 * w1;
        return Math.Abs(a - b) / (double)Math.Max(a, b);
    }

    /// <summary>True when the two sizes have exactly the same ratio.</summary>
    public static bool Same(long w1, long h1, long w2, long h2) => w1 > 0 && h1 > 0 && w2 * h1 == h2 * w1;

    /// <summary>A readable name: "4:3", "3:4", "16:9", or "1.82:1" / "1:1.82" for other shapes.</summary>
    public static string Label(int width, int height, CultureInfo culture)
    {
        if (width <= 0 || height <= 0) return "";
        bool portrait = height > width;
        double r = portrait ? (double)height / width : (double)width / height;
        foreach (var (a, b) in Common)
        {
            if (Math.Abs(r / ((double)a / b) - 1) <= 0.005) return portrait ? $"{b}:{a}" : $"{a}:{b}";
        }
        string d = r.ToString("0.00", culture);
        return portrait ? $"1:{d}" : $"{d}:1";
    }
}
