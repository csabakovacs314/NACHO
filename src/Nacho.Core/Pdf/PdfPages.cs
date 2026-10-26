using static Nacho.Core.Pdf.Pdfium;

namespace Nacho.Core.Pdf;

/// <summary>Reading PDF pages for the compare window and the tests: count, text and a drawn page.</summary>
public static class PdfPages
{
    /// <summary>Number of pages, or 0 when the file can't be opened.</summary>
    public static int Count(string path)
    {
        if (!PdfDoc.EnsureLibrary()) return 0;
        byte[] data = File.ReadAllBytes(path);
        lock (PdfDoc.Gate)
        {
            using var doc = PdfDoc.Open(data, out _);
            return doc?.PageCount ?? 0;
        }
    }

    /// <summary>All text of one page (0-based), as PDFium reads it.</summary>
    public static string Text(string path, int page)
    {
        if (!PdfDoc.EnsureLibrary()) return "";
        byte[] data = File.ReadAllBytes(path);
        lock (PdfDoc.Gate)
        {
            using var doc = PdfDoc.Open(data, out _) ?? throw new InvalidDataException("Can't open " + path);
            IntPtr p = FPDF_LoadPage(doc.Handle, page);
            try
            {
                return PdfDoc.PageText(p);
            }
            finally
            {
                FPDF_ClosePage(p);
            }
        }
    }

    /// <summary>One page (0-based) drawn on white with its long edge at <paramref name="longEdge"/> pixels, as BGRA.</summary>
    public static (byte[] Bgra, int Width, int Height) Render(string path, int page, int longEdge)
    {
        if (!PdfDoc.EnsureLibrary()) throw new InvalidOperationException("PDFium library not found.");
        byte[] data = File.ReadAllBytes(path);
        lock (PdfDoc.Gate)
        {
            using var doc = PdfDoc.Open(data, out _) ?? throw new InvalidDataException("Can't open " + path);
            IntPtr p = FPDF_LoadPage(doc.Handle, page);
            if (p == IntPtr.Zero) throw new InvalidDataException($"No page {page + 1} in {path}");
            try
            {
                float w = FPDF_GetPageWidthF(p), h = FPDF_GetPageHeightF(p);
                double s = longEdge / Math.Max(w, h);
                int pw = Math.Max(1, (int)Math.Round(w * s)), ph = Math.Max(1, (int)Math.Round(h * s));
                return (PdfDoc.RenderPage(p, pw, ph), pw, ph);
            }
            finally
            {
                FPDF_ClosePage(p);
            }
        }
    }

    /// <summary>Peak signal-to-noise ratio of two BGRA pictures of the same size, in dB.</summary>
    public static double Psnr(byte[] a, byte[] b) => PdfShrinker.Psnr(a, b);
}
