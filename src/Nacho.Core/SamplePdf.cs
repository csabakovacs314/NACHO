using System.Globalization;
using System.IO.Compression;
using System.Text;
using ImageMagick;
using ImageMagick.Drawing;

namespace Nacho.Core;

public static partial class SampleImages
{
    /// <summary>
    /// A three-page A4 report with the cases the PDF shrinker must get right: a large photo on every page (about 400
    /// dpi), a logo used on every page (one shared picture, about 1000 dpi), a lossless screenshot, a small picture
    /// that is already fine, and three it must leave alone: a CMYK photo, a picture with transparency, and a picture
    /// inside a drawing group (form XObject). Every page has text, so the check can compare it.
    /// </summary>
    public static string PdfReport(string path)
    {
        var b = new PdfWriter();
        int font = b.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        int logo = b.Jpeg(Photo(1000, 1000, 1, 95));
        int shot = b.Flate(ScreenshotPixels(2000, 1250), 2000, 1250, "/DeviceRGB");
        int small = b.Jpeg(Photo(300, 200, 5, 85));
        int cmyk;
        using (var img = new MagickImage(Photo(2000, 1400, 6, 92)))
        {
            img.ColorSpace = ColorSpace.CMYK;
            img.Format = MagickFormat.Jpeg;
            cmyk = b.Add($"<< /Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height} /ColorSpace /DeviceCMYK /BitsPerComponent 8 /Filter /DCTDecode", img.ToByteArray());
        }
        int alpha = b.WithMask(Photo(1600, 1200, 7, 92), 1600, 1200);
        int group = b.Add("<< /Type /XObject /Subtype /Form /BBox [0 0 1 1] /Resources << /XObject << /G1 " + b.Jpeg(Photo(1600, 1100, 8, 92)) + " 0 R >> >>",
            Encoding.ASCII.GetBytes("q 1 0 0 1 0 0 cm /G1 Do Q"));

        var pages = new List<int>();
        for (int p = 0; p < 3; p++)
        {
            int photo = b.Jpeg(Photo(2400, 1600, 20 + p, 92));
            var c = new StringBuilder();
            c.Append(Text(72, 790, $"NACHO PDF sample, page {p + 1}")).Append(Text(72, 772, "The pictures below are bigger than they need to be."));
            c.Append(Place("Ph", 72, 380, 432, 288)).Append(Place("Lg", 480, 760, 72, 72));
            string res = $"/Ph {photo} 0 R /Lg {logo} 0 R";
            if (p == 0) { c.Append(Place("Sh", 72, 120, 432, 270)); res += $" /Sh {shot} 0 R"; }
            if (p == 1) { c.Append(Place("Sm", 72, 200, 216, 144)).Append(Place("Am", 300, 120, 216, 162)); res += $" /Sm {small} 0 R /Am {alpha} 0 R"; }
            if (p == 2) { c.Append(Place("Cm", 72, 120, 288, 202)).Append(Place("Gr", 380, 120, 160, 110)); res += $" /Cm {cmyk} 0 R /Gr {group} 0 R"; }
            c.Append(Text(72, 90, "End of page."));
            pages.Add(b.Page(c.ToString(), $"<< /Font << /F1 {font} 0 R >> /XObject << {res} >> >>"));
        }
        return b.Write(path, pages);
    }

    /// <summary>A one-page PDF with only text: nothing to shrink, so NACHO keeps it.</summary>
    public static string PdfTextOnly(string path)
    {
        var b = new PdfWriter();
        int font = b.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var c = new StringBuilder();
        for (int i = 0; i < 30; i++) c.Append(Text(72, 780 - i * 18, $"Line {i + 1}: a PDF with text only, already as small as it gets."));
        return b.Write(path, new List<int> { b.Page(c.ToString(), $"<< /Font << /F1 {font} 0 R >> >>") });
    }

    /// <summary>The report with a signature field and signature value: a signed PDF NACHO must not change.</summary>
    public static string PdfSigned(string path)
    {
        var b = new PdfWriter();
        int font = b.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        int photo = b.Jpeg(Photo(2400, 1600, 30, 92));
        int sig = b.Add("<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached /ByteRange [0 0 0 0] /Contents <00000000> >>");
        int field = b.Add($"<< /FT /Sig /T (Signature1) /V {sig} 0 R /Type /Annot /Subtype /Widget /Rect [0 0 0 0] /F 132 >>");
        int page = b.Page(Text(72, 790, "Signed sample") + Place("Ph", 72, 380, 432, 288), $"<< /Font << /F1 {font} 0 R >> /XObject << /Ph {photo} 0 R >> >>", $"/Annots [{field} 0 R]");
        b.AcroForm = $"<< /Fields [{field} 0 R] /SigFlags 3 >>";
        return b.Write(path, new List<int> { page });
    }

    private static string Text(int x, int y, string s) => $"BT /F1 11 Tf {x} {y} Td ({s}) Tj ET\n";

    private static string Place(string name, double x, double y, double w, double h) =>
        string.Create(CultureInfo.InvariantCulture, $"q {w} 0 0 {h} {x} {y} cm /{name} Do Q\n");

    private static byte[] Photo(int w, int h, int seed, uint quality)
    {
        using var img = new MagickImage("plasma:", new MagickReadSettings { Width = (uint)w, Height = (uint)h });
        img.Format = MagickFormat.Jpeg;
        img.Quality = quality;
        img.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:2:0");
        img.Strip();
        _ = seed;
        return img.ToByteArray();
    }

    private static byte[] ScreenshotPixels(int w, int h)
    {
        using var img = new MagickImage(MagickColors.White, (uint)w, (uint)h);
        var d = new Drawables().FillColor(new MagickColor("#005FB8")).Rectangle(0, 0, w, 70).FillColor(new MagickColor("#2B2B2B"));
        var rnd = new Random(12);
        for (int row = 110; row < h - 40; row += 32)
        {
            double x = 50;
            while (x < w - 220)
            {
                double ww = 20 + rnd.Next(110);
                d.Rectangle(x, row, x + ww, row + 13);
                x += ww + 12;
            }
        }
        d.Draw(img);
        return img.GetPixels().ToByteArray(PixelMapping.RGB)!;
    }

    /// <summary>Writes small, valid PDF files by hand (objects, cross-reference table, trailer).</summary>
    private sealed class PdfWriter
    {
        private readonly List<byte[]> _objects = new() { Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>() };
        public string? AcroForm { get; set; }

        /// <summary>Adds an object; with a stream, the dictionary must be left open (no closing >>).</summary>
        public int Add(string dict, byte[]? stream = null)
        {
            using var ms = new MemoryStream();
            if (stream is null)
            {
                ms.Write(Encoding.ASCII.GetBytes(dict));
            }
            else
            {
                ms.Write(Encoding.ASCII.GetBytes($"{dict} /Length {stream.Length} >>\nstream\n"));
                ms.Write(stream);
                ms.Write("\nendstream"u8);
            }
            _objects.Add(ms.ToArray());
            return _objects.Count;
        }

        public int Jpeg(byte[] jpeg)
        {
            var info = new MagickImageInfo(jpeg);
            return Add($"<< /Type /XObject /Subtype /Image /Width {info.Width} /Height {info.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode", jpeg);
        }

        public int Flate(byte[] raw, int w, int h, string colourSpace, string extra = "") =>
            Add($"<< /Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace {colourSpace} /BitsPerComponent 8 /Filter /FlateDecode{extra}", Deflate(raw));

        /// <summary>A photo with a soft mask: an oval that is see-through around the edges.</summary>
        public int WithMask(byte[] jpeg, int w, int h)
        {
            var mask = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double dx = (x - w / 2.0) / (w / 2.0), dy = (y - h / 2.0) / (h / 2.0);
                    mask[y * w + x] = dx * dx + dy * dy <= 0.8 ? (byte)255 : (byte)0;
                }
            }
            int smask = Flate(mask, w, h, "/DeviceGray");
            var info = new MagickImageInfo(jpeg);
            return Add($"<< /Type /XObject /Subtype /Image /Width {info.Width} /Height {info.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask {smask} 0 R /Filter /DCTDecode", jpeg);
        }

        public int Page(string content, string resources, string extra = "")
        {
            // Content streams are compressed, like in real PDFs.
            int c = Add("<< /Filter /FlateDecode", Deflate(Encoding.ASCII.GetBytes(content)));
            return Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources {resources} /Contents {c} 0 R {extra} >>");
        }

        public string Write(string path, List<int> pages)
        {
            string kids = string.Join(" ", pages.Select(p => $"{p} 0 R"));
            _objects[0] = Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R" + (AcroForm is null ? "" : " /AcroForm 3 0 R") + " >>");
            _objects[1] = Encoding.ASCII.GetBytes($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>");
            _objects[2] = Encoding.ASCII.GetBytes(AcroForm ?? "<< >>");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var fs = File.Create(path);
            fs.Write("%PDF-1.7\n%âãÏÓ\n"u8);
            var offsets = new long[_objects.Count];
            for (int i = 0; i < _objects.Count; i++)
            {
                offsets[i] = fs.Position;
                fs.Write(Encoding.ASCII.GetBytes($"{i + 1} 0 obj\n"));
                fs.Write(_objects[i]);
                fs.Write("\nendobj\n"u8);
            }
            long xref = fs.Position;
            var sb = new StringBuilder();
            sb.Append("xref\n0 ").Append(_objects.Count + 1).Append("\n0000000000 65535 f \n");
            foreach (long o in offsets) sb.Append(o.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            sb.Append("trailer\n<< /Size ").Append(_objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            fs.Write(Encoding.ASCII.GetBytes(sb.ToString()));
            return path;
        }

        private static byte[] Deflate(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data);
            return ms.ToArray();
        }
    }
}
