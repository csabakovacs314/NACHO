using ImageMagick;
using Nacho.Core.Pdf;

namespace Nacho.Core;

/// <summary>
/// The pictures for the before/after slider, as 8-bit BGRA pixels (not premultiplied) that both apps can show
/// without a decoder of their own. The original is turned upright and resized to the copy's exact size with the
/// same resize the engine uses, so the slider shows only what saving did (JPEG blocks, banding), not the resize.
/// </summary>
public sealed class ComparePictures
{
    public int Width { get; private init; }
    public int Height { get; private init; }

    /// <summary>The original, upright, at Width x Height.</summary>
    public byte[] Original { get; private init; } = Array.Empty<byte>();

    /// <summary>The Docs-ready copy as saved.</summary>
    public byte[] Copy { get; private init; } = Array.Empty<byte>();

    /// <summary>
    /// The 256-colour version NACHO tried and turned down (badge "Full colour, no banding"), so the reason can be
    /// seen; null otherwise. Same settings as the engine, built from the copy, which has the same pixels.
    /// </summary>
    public byte[]? Palette { get; private init; }

    /// <summary>How close the 256-colour version came, in dB; NACHO needs <see cref="ImageShrinker.MinPalettePsnr"/>.</summary>
    public double? PalettePsnr { get; private init; }

    public int OriginalWidth { get; private init; }
    public int OriginalHeight { get; private init; }

    /// <summary>True when the original had more pixels than the copy and was resized for the comparison.</summary>
    public bool OriginalResized => !IsPdf && (OriginalWidth != Width || OriginalHeight != Height);

    /// <summary>A PDF comparison shows one page of the original PDF and of the smaller copy, drawn at the same size.</summary>
    public bool IsPdf { get; private init; }

    /// <summary>The page shown (0-based) and the number of pages, for a PDF.</summary>
    public int Page { get; private init; }

    public int PageCount { get; private init; }

    /// <summary>Long edge of a drawn PDF page: about 170 dpi on A4, enough to see what scaling a picture changed.</summary>
    public const int PdfPageLongEdge = 2000;

    public static bool CanCompare(ShrinkResult? r) =>
        r is { Kind: ResultKind.Shrunk, OutputPath: { } p } && (!r.IsDocument || r.Document!.Format == "PDF")
        && File.Exists(p) && File.Exists(r.SourcePath);

    /// <summary>One page of a shrunk PDF: the original page left, the smaller copy's page right.</summary>
    public static ComparePictures LoadPdfPage(ShrinkResult r, int page)
    {
        if (!CanCompare(r) || r.Document?.Format != "PDF") throw new InvalidOperationException("Not a shrunk PDF.");
        int count = PdfPages.Count(r.OutputPath!);
        page = Math.Clamp(page, 0, Math.Max(0, count - 1));
        var a = PdfPages.Render(r.SourcePath, page, PdfPageLongEdge);
        var b = PdfPages.Render(r.OutputPath!, page, PdfPageLongEdge);
        if (a.Width != b.Width || a.Height != b.Height) throw new InvalidDataException("The pages have different sizes.");
        return new ComparePictures
        {
            Width = a.Width,
            Height = a.Height,
            OriginalWidth = a.Width,
            OriginalHeight = a.Height,
            Original = a.Bgra,
            Copy = b.Bgra,
            IsPdf = true,
            Page = page,
            PageCount = count,
        };
    }

    public static ComparePictures Load(ShrinkResult r)
    {
        if (!CanCompare(r)) throw new InvalidOperationException("Nothing to compare: the copy or the original is missing.");
        if (r.IsDocument) return LoadPdfPage(r, 0);

        using var copy = ReadFirstFrame(r.OutputPath!);
        using var original = ReadFirstFrame(r.SourcePath);
        original.AutoOrient();
        int ow = (int)original.Width, oh = (int)original.Height;
        int w = (int)copy.Width, h = (int)copy.Height;
        if (ow != w || oh != h) original.Resize(new MagickGeometry((uint)w, (uint)h) { IgnoreAspectRatio = true });

        byte[]? palette = null;
        double? psnr = null;
        if (r.Badges.Contains(Badge.FullColour) && r.OutputFormat == "PNG")
        {
            using var q = copy.Clone();
            q.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.No });
            psnr = ImageShrinker.Psnr(copy, q);
            palette = Bgra(q);
        }

        return new ComparePictures
        {
            Width = w,
            Height = h,
            OriginalWidth = ow,
            OriginalHeight = oh,
            Original = Bgra(original),
            Copy = Bgra(copy),
            Palette = palette,
            PalettePsnr = psnr,
        };
    }

    private static MagickImage ReadFirstFrame(string path)
    {
        var img = new MagickImage();
        img.Read(path, new MagickReadSettings { FrameIndex = 0, FrameCount = 1 });
        if (img.ColorSpace is ColorSpace.CMYK or ColorSpace.Gray or ColorSpace.LinearGray) img.ColorSpace = ColorSpace.sRGB;
        return img;
    }

    private static byte[] Bgra(IMagickImage<byte> img)
    {
        using var pixels = img.GetPixelsUnsafe();
        return pixels.ToByteArray(PixelMapping.BGRA) ?? throw new InvalidOperationException("No pixels.");
    }
}
