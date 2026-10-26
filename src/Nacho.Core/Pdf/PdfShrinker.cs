using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ImageMagick;
using static Nacho.Core.Pdf.Pdfium;

namespace Nacho.Core.Pdf;

/// <summary>
/// Makes a smaller copy of a PDF by shrinking the pictures inside it; text, fonts, links and forms stay as they are.
/// Pictures printed sharper than the preset's dots per inch are scaled down to it (photos as JPEG, graphics without
/// loss) and put back with PDFium, which then writes the whole file again. Before the copy is kept, every page of the
/// copy is compared with the original: same page count and size, same text, and the drawn page must look the same.
/// Anything else, or a copy that is not smaller, keeps the original.
/// </summary>
/// <remarks>
/// Left as they are: pictures with transparency (PDFium can't return them at full size with their mask), print
/// colours (CMYK and other special colour spaces, to keep print output true), black-and-white scans (already
/// tiny), pictures inside drawing groups (form XObjects, which PDFium can't write back), and anything not sharper
/// than needed. Password-protected and signed PDFs are not changed: a new file would remove the protection or break
/// the signature.
/// </remarks>
public sealed class PdfShrinker
{
    private const double Tolerance = 1.1;          // only pictures more than 10% sharper than needed
    private const long MinPixels = 160 * 160;
    private const double MinPagePsnr = 24;         // a drawn page must stay at least this close (dB)
    private const int CheckLongEdge = 700;          // pixels per page for the check
    private const uint JpegQuality = 80;
    private const uint GraphicColourLimit = 8000;
    private const double MinSaving = 0.02;          // a copy must be at least 2% smaller

    private sealed class Picture
    {
        public required string Key { get; init; }
        public required ImageMetadata Meta { get; init; }
        public required List<string> Filters { get; init; }
        public required long RawBytes { get; init; }
        public List<(IntPtr Page, IntPtr Obj)> Uses { get; } = new();
        public double MinDpi { get; set; } = double.MaxValue;
    }

    public ShrinkResult Shrink(string path, Preset preset, ShrinkOptions opt, ShrinkResult r)
    {
        r.SourceFormat = "PDF";
        var report = new DocumentReport { Format = "PDF", Dpi = preset.PdfDpi };
        r.Document = report;
        if (!PdfDoc.EnsureLibrary()) return ImageShrinker.Fail(r, ErrorKind.PdfEngine, "PDFium library not found");
        byte[] data = File.ReadAllBytes(path);
        lock (PdfDoc.Gate)
        {
            return ShrinkLocked(path, data, preset.PdfDpi, opt, r, report);
        }
    }

    private static unsafe ShrinkResult ShrinkLocked(string path, byte[] data, int dpi, ShrinkOptions opt, ShrinkResult r, DocumentReport report)
    {
        using var doc = PdfDoc.Open(data, out uint error);
        if (doc is null) return ImageShrinker.Fail(r, error == ERR_PASSWORD || error == ERR_SECURITY ? ErrorKind.PdfLocked : ErrorKind.Damaged, "PDFium error " + error);
        if (FPDF_GetSecurityHandlerRevision(doc.Handle) != -1) return ImageShrinker.Fail(r, ErrorKind.PdfLocked, "encrypted");
        if (FPDF_GetSignatureCount(doc.Handle) > 0) return ImageShrinker.Fail(r, ErrorKind.PdfSigned, null);
        report.Pages = doc.PageCount;
        if (doc.PageCount <= 0) return ImageShrinker.Fail(r, ErrorKind.Damaged, "no pages");

        var pages = new IntPtr[doc.PageCount];
        try
        {
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i] = FPDF_LoadPage(doc.Handle, i);
                if (pages[i] == IntPtr.Zero) return ImageShrinker.Fail(r, ErrorKind.Damaged, "page " + (i + 1));
            }

            // Pass 1: every picture once, with all the places it is used and the sharpness it needs.
            var pictures = new Dictionary<string, Picture>();
            var inGroups = new HashSet<string>();
            foreach (IntPtr page in pages)
            {
                for (int i = 0; i < FPDFPage_CountObjects(page); i++)
                {
                    IntPtr obj = FPDFPage_GetObject(page, i);
                    int type = FPDFPageObj_GetType(obj);
                    if (type == PAGEOBJ_IMAGE)
                    {
                        var p = Describe(page, obj);
                        if (!pictures.TryGetValue(p.Key, out var known)) pictures[p.Key] = known = p;
                        known.Uses.Add((page, obj));
                        known.MinDpi = Math.Min(known.MinDpi, Dpi(p.Meta));
                    }
                    else if (type == PAGEOBJ_FORM)
                    {
                        CollectGroupPictures(page, obj, inGroups);
                    }
                }
            }
            inGroups.ExceptWith(pictures.Keys);
            report.Skipped += inGroups.Count;

            // Pass 2: shrink each picture once and put it back everywhere it is used.
            var dirty = new HashSet<IntPtr>();
            foreach (var pic in pictures.Values)
            {
                Outcome outcome = TryShrink(doc, pic, dpi, dirty);
                switch (outcome.Kind)
                {
                    case OutcomeKind.Skipped:
                        report.Skipped++;
                        break;
                    case OutcomeKind.Kept:
                        report.Pictures++;
                        report.Kept++;
                        report.PicturesBefore += pic.RawBytes;
                        report.PicturesAfter += pic.RawBytes;
                        break;
                    case OutcomeKind.Shrunk:
                        report.Pictures++;
                        report.Shrunk++;
                        report.PicturesBefore += pic.RawBytes;
                        report.PicturesAfter += outcome.NewBytes;
                        break;
                }
            }
            if (report.Shrunk == 0) return KeepDocument(r, report);

            foreach (IntPtr page in dirty) FPDFPage_GenerateContent(page);
            byte[]? smaller = doc.Save();
            if (smaller is null)
            {
                report.CheckFailed = true;
                return KeepDocument(r, report);
            }
            if (smaller.LongLength >= data.LongLength * (1 - MinSaving)) return KeepDocument(r, report);
            if (!SamePages(pages, smaller))
            {
                report.CheckFailed = true;
                return KeepDocument(r, report);
            }

            Directory.CreateDirectory(opt.OutputFolder);
            string outPath = ImageShrinker.WriteNew(opt.OutputFolder, Path.GetFileNameWithoutExtension(path) + opt.Suffix, ".pdf", smaller, path);
            r.Kind = ResultKind.Shrunk;
            r.OutputPath = outPath;
            r.OutputBytes = smaller.LongLength;
            r.OutputFormat = "PDF";
            return r;
        }
        finally
        {
            foreach (IntPtr page in pages)
            {
                if (page != IntPtr.Zero) FPDF_ClosePage(page);
            }
        }
    }

    private static ShrinkResult KeepDocument(ShrinkResult r, DocumentReport report)
    {
        ImageShrinker.Keep(r, false);
        r.Document = report;
        return r;
    }

    private static double Dpi(ImageMetadata m) => Math.Min(m.HorizontalDpi, m.VerticalDpi);

    private static unsafe Picture Describe(IntPtr page, IntPtr obj)
    {
        FPDFImageObj_GetImageMetadata(obj, page, out var meta);
        var filters = new List<string>();
        int count = FPDFImageObj_GetImageFilterCount(obj);
        var buf = new byte[64];
        for (int f = 0; f < count; f++)
        {
            fixed (byte* b = buf)
            {
                nuint n = FPDFImageObj_GetImageFilter(obj, f, b, (nuint)buf.Length);
                filters.Add(n > 1 ? System.Text.Encoding.ASCII.GetString(buf, 0, (int)n - 1) : "");
            }
        }
        nuint rawLen = FPDFImageObj_GetImageDataRaw(obj, null, 0);
        var raw = new byte[(int)rawLen];
        fixed (byte* p = raw) FPDFImageObj_GetImageDataRaw(obj, p, rawLen);
        string key = Convert.ToHexString(SHA256.HashData(raw)) + $"-{meta.Width}x{meta.Height}";
        return new Picture { Key = key, Meta = meta, Filters = filters, RawBytes = raw.LongLength };
    }

    private static void CollectGroupPictures(IntPtr page, IntPtr form, HashSet<string> keys)
    {
        int n = FPDFFormObj_CountObjects(form);
        for (uint i = 0; i < n; i++)
        {
            IntPtr obj = FPDFFormObj_GetObject(form, i);
            int type = FPDFPageObj_GetType(obj);
            if (type == PAGEOBJ_IMAGE) keys.Add(Describe(page, obj).Key);
            else if (type == PAGEOBJ_FORM) CollectGroupPictures(page, obj, keys);
        }
    }

    private enum OutcomeKind { Skipped, Kept, Shrunk }

    private readonly record struct Outcome(OutcomeKind Kind, long NewBytes = 0);

    /// <summary>Colour spaces NACHO re-encodes: grey, RGB (also calibrated and ICC-based with 3 or 1 components) and palettes.</summary>
    private static bool PlainColours(ImageMetadata m) =>
        m.ColorSpace is 1 or 2 or 6 or 7 or 10 || (m.ColorSpace == 5 && m.BitsPerPixel is 8 or 24);

    private static unsafe Outcome TryShrink(PdfDoc doc, Picture pic, int targetDpi, HashSet<IntPtr> dirty)
    {
        var m = pic.Meta;
        if (m.BitsPerPixel <= 1 || pic.Filters.Any(f => f is "JBIG2Decode" or "CCITTFaxDecode") || !PlainColours(m))
        {
            return new Outcome(OutcomeKind.Skipped);
        }
        if (pic.MinDpi <= targetDpi * Tolerance || (long)m.Width * m.Height < MinPixels) return new Outcome(OutcomeKind.Kept);

        var (page, obj) = pic.Uses[0];
        if (HasTransparency(doc, page, obj)) return new Outcome(OutcomeKind.Skipped);

        using var img = Decode(obj);
        if (img is null) return new Outcome(OutcomeKind.Skipped);
        double scale = targetDpi / pic.MinDpi;
        uint w = (uint)Math.Max(1, Math.Round(img.Width * scale)), h = (uint)Math.Max(1, Math.Round(img.Height * scale));
        img.Resize(new MagickGeometry(w, h) { IgnoreAspectRatio = true });

        bool wasLossy = pic.Filters.Any(f => f is "DCTDecode" or "JPXDecode");
        bool graphic = !wasLossy && CountColours(img) <= GraphicColourLimit;
        if (graphic)
        {
            // Lossless: PDFium compresses the pixels itself. Estimated with PNG, which uses the same compression.
            using var probe = img.Clone();
            probe.Format = MagickFormat.Png;
            long estimate = probe.ToByteArray().LongLength;
            if (estimate >= pic.RawBytes * 0.9) return new Outcome(OutcomeKind.Kept);
            byte[] bgr = img.GetPixels().ToByteArray(PixelMapping.BGR) ?? throw new InvalidOperationException("no pixels");
            fixed (byte* p = bgr)
            {
                IntPtr bmp = FPDFBitmap_CreateEx((int)w, (int)h, BitmapBGR, p, (int)w * 3);
                try
                {
                    foreach (var use in pic.Uses)
                    {
                        IntPtr pg = use.Page;
                        if (FPDFImageObj_SetBitmap(&pg, 1, use.Obj, bmp) == 0) return new Outcome(OutcomeKind.Skipped);
                        dirty.Add(use.Page);
                    }
                }
                finally
                {
                    FPDFBitmap_Destroy(bmp);
                }
            }
            return new Outcome(OutcomeKind.Shrunk, estimate);
        }

        img.Format = MagickFormat.Jpeg;
        img.Quality = JpegQuality;
        img.Settings.Interlace = Interlace.NoInterlace;
        img.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:2:0");
        img.Strip();
        byte[] jpeg = img.ToByteArray();
        if (jpeg.LongLength >= pic.RawBytes * 0.9) return new Outcome(OutcomeKind.Kept);
        var pin = GCHandle.Alloc(jpeg, GCHandleType.Pinned);
        var self = GCHandle.Alloc(jpeg);
        try
        {
            var file = new FileRead { FileLen = new CULong((nuint)jpeg.Length), GetBlock = &PdfDoc.ReadBlock, Param = (void*)GCHandle.ToIntPtr(self) };
            foreach (var use in pic.Uses)
            {
                IntPtr pg = use.Page;
                if (FPDFImageObj_LoadJpegFileInline(&pg, 1, use.Obj, &file) == 0) return new Outcome(OutcomeKind.Skipped);
                dirty.Add(use.Page);
            }
        }
        finally
        {
            self.Free();
            pin.Free();
        }
        return new Outcome(OutcomeKind.Shrunk, jpeg.LongLength);
    }

    /// <summary>True when any part of the picture is see-through (soft mask, mask or colour key), checked on a small rendering.</summary>
    private static unsafe bool HasTransparency(PdfDoc doc, IntPtr page, IntPtr obj)
    {
        IntPtr rb = FPDFImageObj_GetRenderedBitmap(doc.Handle, page, obj);
        if (rb == IntPtr.Zero) return true;
        try
        {
            if (FPDFBitmap_GetFormat(rb) != BitmapBGRA) return false;
            int w = FPDFBitmap_GetWidth(rb), h = FPDFBitmap_GetHeight(rb), stride = FPDFBitmap_GetStride(rb);
            byte* p = FPDFBitmap_GetBuffer(rb);
            for (int y = 0; y < h; y++)
            {
                byte* row = p + (long)y * stride;
                for (int x = 0; x < w; x++)
                {
                    if (row[x * 4 + 3] != 255) return true;
                }
            }
            return false;
        }
        finally
        {
            FPDFBitmap_Destroy(rb);
        }
    }

    /// <summary>The picture's own pixels (without mask or placement), as an 8-bit Magick image.</summary>
    private static unsafe MagickImage? Decode(IntPtr obj)
    {
        IntPtr bmp = FPDFImageObj_GetBitmap(obj);
        if (bmp == IntPtr.Zero) return null;
        try
        {
            int w = FPDFBitmap_GetWidth(bmp), h = FPDFBitmap_GetHeight(bmp), stride = FPDFBitmap_GetStride(bmp), fmt = FPDFBitmap_GetFormat(bmp);
            int channels = fmt switch { BitmapGray => 1, BitmapBGR => 3, BitmapBGRx or BitmapBGRA => 4, _ => 0 };
            if (channels == 0 || w <= 0 || h <= 0) return null;
            byte* src = FPDFBitmap_GetBuffer(bmp);
            var px = new byte[w * h * channels];
            for (int y = 0; y < h; y++) Marshal.Copy((IntPtr)(src + (long)y * stride), px, y * w * channels, w * channels);
            string map = channels switch { 1 => "R", 3 => "BGR", _ => "BGRA" };
            var img = new MagickImage();
            img.ReadPixels(px, new PixelReadSettings((uint)w, (uint)h, StorageType.Char, map));
            if (channels == 1)
            {
                img.ColorType = ColorType.Grayscale;
            }
            else if (channels == 4)
            {
                img.Alpha(AlphaAction.Off);
            }
            return img;
        }
        finally
        {
            FPDFBitmap_Destroy(bmp);
        }
    }

    private static uint CountColours(IMagickImage<byte> img)
    {
        using var probe = img.Clone();
        probe.Sample(new MagickGeometry(400, 400) { Greater = true });
        return probe.TotalColors;
    }

    /// <summary>
    /// The page-by-page check: the new file opens, has the same pages at the same size with the same text, and every
    /// drawn page stays within <see cref="MinPagePsnr"/> of the original.
    /// </summary>
    private static bool SamePages(IntPtr[] originalPages, byte[] smaller)
    {
        using var copy = PdfDoc.Open(smaller, out _);
        if (copy is null || copy.PageCount != originalPages.Length) return false;
        for (int i = 0; i < originalPages.Length; i++)
        {
            IntPtr a = originalPages[i];
            IntPtr b = FPDF_LoadPage(copy.Handle, i);
            if (b == IntPtr.Zero) return false;
            try
            {
                float wa = FPDF_GetPageWidthF(a), ha = FPDF_GetPageHeightF(a);
                if (Math.Abs(wa - FPDF_GetPageWidthF(b)) > 0.01 || Math.Abs(ha - FPDF_GetPageHeightF(b)) > 0.01) return false;
                if (PdfDoc.PageText(a) != PdfDoc.PageText(b)) return false;
                double s = CheckLongEdge / Math.Max(wa, ha);
                int w = Math.Max(1, (int)Math.Round(wa * s)), h = Math.Max(1, (int)Math.Round(ha * s));
                if (Psnr(PdfDoc.RenderPage(a, w, h), PdfDoc.RenderPage(b, w, h)) < MinPagePsnr) return false;
            }
            finally
            {
                FPDF_ClosePage(b);
            }
        }
        return true;
    }

    internal static double Psnr(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return 0;
        double sum = 0;
        for (int i = 0; i < a.Length; i += 4)
        {
            for (int c = 0; c < 3; c++)
            {
                int d = a[i + c] - b[i + c];
                sum += d * d;
            }
        }
        double mse = sum / (a.Length / 4 * 3);
        return mse <= 1e-10 ? 100 : 10 * Math.Log10(255.0 * 255.0 / mse);
    }
}
