using System.Text;
using ImageMagick;

namespace Nacho.Core;

/// <summary>
/// Makes a Docs-ready copy of one image. Never writes to the original file, never crops or stretches:
/// every size is worked out from the original's shape (see <see cref="Limits.FitWithin"/>).
/// </summary>
public sealed class ImageShrinker
{
    private static readonly int[] JpegQualities = { 82, 76, 70, 64, 60 };
    private const uint GraphicColourLimit = 8000;
    private const int BudgetRounds = 5;

    private static readonly HashSet<string> ReadableFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPEG", "PNG", "GIF", "HEIC", "AVIF", "WEBP", "TIFF", "BMP", "ICO", "SVG", "MSVG", "JXL", "JP2",
        "DNG", "CR2", "CR3", "NEF", "ARW", "ORF", "RW2", "RAF", "PEF", "SRW", "PSD", "TGA",
    };

    private static readonly HashSet<string> NotImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".csv", ".log", ".md", ".html", ".htm", ".xml", ".json", ".doc", ".docx", ".xls", ".xlsx",
        ".ppt", ".pptx", ".zip", ".rar", ".7z", ".exe", ".dll", ".msi", ".lnk", ".ps", ".eps", ".ai",
    };

    private sealed record Encoded(byte[] Bytes, string Format, string Extension, int Width, int Height);

    public ShrinkResult Shrink(string path, Preset preset, ShrinkOptions options)
    {
        var r = new ShrinkResult { SourcePath = path };
        try
        {
            return ShrinkCore(path, preset, options, r);
        }
        catch (MagickMissingDelegateErrorException ex) { return Fail(r, ErrorKind.NotAnImage, ex.Message); }
        catch (MagickCorruptImageErrorException ex) { return Fail(r, ErrorKind.Damaged, ex.Message); }
        catch (MagickException ex)
        {
            return Fail(r, ex.Message.Contains("no decode delegate", StringComparison.OrdinalIgnoreCase)
                ? ErrorKind.NotAnImage : ErrorKind.Damaged, ex.Message);
        }
        catch (FileNotFoundException ex) { return Fail(r, ErrorKind.NotFound, ex.Message); }
        catch (DirectoryNotFoundException ex) { return Fail(r, ErrorKind.NotFound, ex.Message); }
    }

    private ShrinkResult ShrinkCore(string path, Preset preset, ShrinkOptions opt, ShrinkResult r)
    {
        if (!File.Exists(path)) return Fail(r, ErrorKind.NotFound, null);
        r.OriginalBytes = new FileInfo(path).Length;
        if (StartsWith(path, "%PDF-")) return Fail(r, ErrorKind.Pdf, null);
        if (NotImageExtensions.Contains(Path.GetExtension(path))) return Fail(r, ErrorKind.NotAnImage, null);

        using var images = new MagickImageCollection();
        images.Read(path);
        if (images.Count == 0) return Fail(r, ErrorKind.Damaged, null);

        var first = images[0];
        r.SourceFormat = NormaliseFormat(first.Format);
        if (!ReadableFormats.Contains(r.SourceFormat)) return Fail(r, ErrorKind.NotAnImage, r.SourceFormat);

        bool animated = r.SourceFormat == "GIF" && images.Count > 1;
        bool turned = first.Orientation is Orientation.LeftTop or Orientation.RightTop
            or Orientation.RightBottom or Orientation.LeftBottom;
        bool needsRotate = first.Orientation is not (Orientation.Undefined or Orientation.TopLeft);
        int srcW = (int)first.Width, srcH = (int)first.Height;
        r.OriginalWidth = turned ? srcH : srcW;
        r.OriginalHeight = turned ? srcW : srcH;

        var exif = first.GetExifProfile();
        bool hasGps = HasGps(exif) || XmpHasGps(first);
        bool hasCamera = HasCameraDetails(exif) || first.GetIptcProfile() != null;
        r.OriginalHadLocation = hasGps;

        bool privacyOk = !(opt.RemoveLocation && hasGps) && !(opt.RemoveCamera && hasCamera);
        bool formatOk = opt.Format switch
        {
            FormatMode.Jpeg => r.SourceFormat == "JPEG",
            FormatMode.Png => r.SourceFormat == "PNG",
            _ => Limits.IsDocsFormat(r.SourceFormat),
        };
        bool pixelsOk = (long)r.OriginalWidth * r.OriginalHeight <= Limits.MaxPixels;
        bool acceptableAsIs = formatOk && privacyOk && pixelsOk && !needsRotate;

        if (acceptableAsIs && Math.Max(r.OriginalWidth, r.OriginalHeight) <= preset.LongEdge
            && r.OriginalBytes <= preset.BudgetBytes)
        {
            return Keep(r, hasGps);
        }

        var (tw, th) = Limits.FitWithin(r.OriginalWidth, r.OriginalHeight, preset.LongEdge);
        Encoded enc = animated
            ? EncodeAnimated(images, tw, th, r)
            : EncodeStill(first, tw, th, r, preset, opt, hasCamera, hasGps, needsRotate);

        // Never bigger: when the original is already acceptable and the copy is not smaller, keep the original.
        if (enc.Bytes.LongLength >= r.OriginalBytes && acceptableAsIs) return Keep(r, hasGps);

        Directory.CreateDirectory(opt.OutputFolder);
        string stem = Path.GetFileNameWithoutExtension(path) + opt.Suffix;
        string outPath = WriteNew(opt.OutputFolder, stem, enc.Extension, enc.Bytes, path);

        r.Kind = ResultKind.Shrunk;
        r.OutputPath = outPath;
        r.OutputBytes = enc.Bytes.LongLength;
        r.OutputWidth = enc.Width;
        r.OutputHeight = enc.Height;
        r.OutputFormat = enc.Format;
        r.OutputHasLocation = hasGps && !opt.RemoveLocation && !opt.RemoveCamera;
        if (r.SourceFormat != enc.Format) r.Badges.Insert(0, Badge.FromFormat);
        return r;
    }

    private static Encoded EncodeStill(IMagickImage<byte> source, int tw, int th, ShrinkResult r, Preset preset,
        ShrinkOptions opt, bool hasCamera, bool hasGps, bool needsRotate)
    {
        // The basis is the upright, stripped, full-size picture. Every size is rendered from it,
        // so a second or third resize never builds on a rounded one.
        using var basis = source.Clone();
        if (needsRotate)
        {
            basis.AutoOrient();
            r.Badges.Add(Badge.RotatedUpright);
        }
        StripMetadata(basis, opt, hasCamera, hasGps, r);
        using var img = Render(basis, tw, th);

        bool alphaUsed = img.HasAlpha && !img.IsOpaque;
        bool graphic = CountColours(img) <= GraphicColourLimit;
        bool photo = hasCamera || !graphic;
        string format = opt.Format switch
        {
            FormatMode.Jpeg => "JPEG",
            FormatMode.Png => "PNG",
            _ => alphaUsed ? "PNG" : (photo ? "JPEG" : "PNG"),
        };
        if (format == "PNG" && alphaUsed) r.Badges.Add(Badge.Transparency);
        return format == "JPEG"
            ? EncodeJpeg(basis, img, preset, r)
            : EncodePng(img, preset, r, graphic, opt.Format == FormatMode.Auto && !alphaUsed);
    }

    /// <summary>
    /// A copy of the basis at exactly width x height. The size already has the original's shape;
    /// forcing it stops ImageMagick from rounding the short side its own way.
    /// </summary>
    private static IMagickImage<byte> Render(IMagickImage<byte> basis, int width, int height)
    {
        var img = basis.Clone();
        if ((int)img.Width != width || (int)img.Height != height)
        {
            img.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
        }
        return img;
    }

    private static Encoded EncodeJpeg(IMagickImage<byte> basis, IMagickImage<byte> first, Preset preset, ShrinkResult r)
    {
        IMagickImage<byte> img = first;
        IMagickImage<byte>? owned = null;
        try
        {
            byte[] bytes = Array.Empty<byte>();
            int longEdge = (int)Math.Max(img.Width, img.Height);
            for (int round = 0; round < BudgetRounds; round++)
            {
                PrepareJpeg(img);
                foreach (int q in round == 0 ? JpegQualities : JpegQualities.Skip(2))
                {
                    img.Quality = (uint)q;
                    bytes = img.ToByteArray();
                    if (bytes.LongLength <= preset.BudgetBytes)
                    {
                        return new Encoded(bytes, "JPEG", ".jpg", (int)img.Width, (int)img.Height);
                    }
                }
                if (round == BudgetRounds - 1 || longEdge <= 320) break;

                // Over budget: 15% smaller, worked out again from the full-size picture and its shape.
                longEdge = longEdge * 85 / 100;
                var (w, h) = Limits.FitWithin((int)basis.Width, (int)basis.Height, longEdge);
                if (w == (int)img.Width && h == (int)img.Height) break;
                owned?.Dispose();
                owned = Render(basis, w, h);
                img = owned;
            }
            r.Badges.Add(Badge.OverBudget);
            return new Encoded(bytes, "JPEG", ".jpg", (int)img.Width, (int)img.Height);
        }
        finally
        {
            owned?.Dispose();
        }
    }

    private static void PrepareJpeg(IMagickImage<byte> img)
    {
        if (img.HasAlpha)
        {
            img.BackgroundColor = MagickColors.White;
            img.Alpha(AlphaAction.Remove);
        }
        img.Format = MagickFormat.Jpeg;
        img.Settings.Interlace = Interlace.Jpeg;
        img.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:2:0");
    }

    private static Encoded EncodePng(IMagickImage<byte> img, Preset preset, ShrinkResult r, bool graphic, bool textBadge)
    {
        img.Format = MagickFormat.Png;
        img.Quality = 95;
        byte[] bytes = img.ToByteArray();
        if (graphic)
        {
            using var q = img.Clone();
            q.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.No });
            q.Format = MagickFormat.Png;
            q.Quality = 95;
            byte[] qb = q.ToByteArray();
            if (qb.LongLength <= bytes.LongLength * 7 / 10)
            {
                bytes = qb;
                r.Badges.Add(Badge.Colours256);
            }
        }
        if (textBadge && graphic) r.Badges.Add(Badge.TextKeptSharp);
        if (bytes.LongLength > preset.BudgetBytes) r.Badges.Add(Badge.OverBudget);
        return new Encoded(bytes, "PNG", ".png", (int)img.Width, (int)img.Height);
    }

    private static Encoded EncodeAnimated(MagickImageCollection images, int tw, int th, ShrinkResult r)
    {
        images.Coalesce();
        foreach (var frame in images)
        {
            if ((int)frame.Width != tw || (int)frame.Height != th)
            {
                frame.Resize(new MagickGeometry((uint)tw, (uint)th) { IgnoreAspectRatio = true });
            }
            frame.Strip();
        }
        // Resizing blends colours; bring the palette back to GIF's 256 so the copy does not grow.
        images.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.No });
        images.Optimize();
        byte[] bytes = images.ToByteArray(MagickFormat.Gif);
        r.Badges.Add(Badge.Animated);
        return new Encoded(bytes, "GIF", ".gif", tw, th);
    }

    private static void StripMetadata(IMagickImage<byte> img, ShrinkOptions opt, bool hasCamera, bool hasGps, ShrinkResult r)
    {
        if (opt.RemoveCamera)
        {
            img.RemoveProfile("exif");
            img.RemoveProfile("xmp");
            img.RemoveProfile("iptc");
            img.RemoveProfile("8bim");
            if (hasCamera) r.Badges.Add(Badge.CameraRemoved);
            if (hasGps) r.Badges.Add(Badge.LocationRemoved);
            return;
        }

        var exif = img.GetExifProfile();
        if (exif != null)
        {
            exif.SetValue(ExifTag.Orientation, (ushort)1);
            if (opt.RemoveLocation)
            {
                foreach (var v in exif.Values.ToList())
                {
                    if (v.Tag.ToString().Contains("GPS", StringComparison.OrdinalIgnoreCase)) exif.RemoveValue(v.Tag);
                }
            }
            img.SetProfile(exif);
        }
        if (opt.RemoveLocation)
        {
            img.RemoveProfile("xmp");
            if (hasGps) r.Badges.Add(Badge.LocationRemoved);
        }
    }

    private static uint CountColours(IMagickImage<byte> img)
    {
        using var probe = img.Clone();
        probe.Sample(new MagickGeometry(400, 400) { Greater = true });
        return probe.TotalColors;
    }

    public static bool HasGps(IExifProfile? exif) =>
        exif != null && (exif.GetValue(ExifTag.GPSLatitude) != null || exif.GetValue(ExifTag.GPSLongitude) != null);

    private static bool HasCameraDetails(IExifProfile? exif) =>
        exif != null && (exif.GetValue(ExifTag.Make) != null || exif.GetValue(ExifTag.Model) != null
            || exif.GetValue(ExifTag.Artist) != null || exif.GetValue(ExifTag.Copyright) != null);

    private static bool XmpHasGps(IMagickImage<byte> img)
    {
        var xmp = img.GetXmpProfile();
        var bytes = xmp?.ToByteArray();
        return bytes != null && Encoding.UTF8.GetString(bytes).Contains("GPSLatitude", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormaliseFormat(MagickFormat format)
    {
        string s = format.ToString().ToUpperInvariant();
        if (s is "JPG" or "JPE" or "PJPEG" or "JPEG") return "JPEG";
        if (s.StartsWith("PNG", StringComparison.Ordinal)) return "PNG";
        if (s.StartsWith("GIF", StringComparison.Ordinal)) return "GIF";
        if (s is "HEIF" or "HEIC") return "HEIC";
        if (s.StartsWith("TIF", StringComparison.Ordinal)) return "TIFF";
        if (s.StartsWith("BMP", StringComparison.Ordinal)) return "BMP";
        return s;
    }

    private static bool StartsWith(string path, string magic)
    {
        using var fs = File.OpenRead(path);
        var buf = new byte[magic.Length];
        int n = fs.Read(buf, 0, buf.Length);
        return n == buf.Length && Encoding.ASCII.GetString(buf) == magic;
    }

    /// <summary>Writes to a new file only; never replaces an existing one or the source.</summary>
    private static string WriteNew(string folder, string stem, string ext, byte[] bytes, string source)
    {
        string full = Path.GetFullPath(source);
        for (int i = 1; i < 10_000; i++)
        {
            string candidate = Path.Combine(folder, i == 1 ? stem + ext : $"{stem} ({i}){ext}");
            if (string.Equals(Path.GetFullPath(candidate), full, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(candidate)) continue;
            try
            {
                using var fs = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write);
                fs.Write(bytes, 0, bytes.Length);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Another copy took this name a moment ago; try the next one.
            }
        }
        throw new IOException("No free file name in " + folder);
    }

    private static ShrinkResult Keep(ShrinkResult r, bool hasGps)
    {
        r.Badges.Clear();
        r.Kind = ResultKind.Kept;
        r.OutputPath = null;
        r.OutputBytes = r.OriginalBytes;
        r.OutputWidth = r.OriginalWidth;
        r.OutputHeight = r.OriginalHeight;
        r.OutputFormat = r.SourceFormat;
        r.OutputHasLocation = hasGps;
        return r;
    }

    private static ShrinkResult Fail(ShrinkResult r, ErrorKind kind, string? detail)
    {
        r.Badges.Clear();
        r.Kind = ResultKind.Error;
        r.Error = kind;
        r.ErrorDetail = detail;
        r.OutputPath = null;
        return r;
    }
}
