using System.Security.Cryptography;
using Nacho.Core;
using Nacho.Core.Pdf;
using Xunit;

namespace Nacho.Tests;

/// <summary>Smaller PDFs: pictures shrunk, everything else the same, and the files NACHO must not touch.</summary>
[Collection("Engine")]
public sealed class PdfTests : IClassFixture<SamplesFixture>
{
    private static readonly Preset Small = new("small", 800, 150);
    private static readonly Preset Balanced = new("balanced", 1280, 300);
    private static readonly Preset Sharp = new("sharp", 1920, 600);
    private readonly SamplesFixture _f;
    private readonly ImageShrinker _s = new();

    public PdfTests(SamplesFixture f) => _f = f;

    private string Dir() => Directory.CreateDirectory(Path.Combine(_f.Root, "pdf-" + Guid.NewGuid().ToString("N")[..6])).FullName;

    private ShrinkOptions Opt() => new() { OutputFolder = Path.Combine(Dir(), "out") };

    [Fact]
    public void Report_gets_much_smaller_and_keeps_its_pages_and_text()
    {
        string src = SampleImages.PdfReport(Path.Combine(Dir(), "Quarterly report.pdf"));
        string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(src)));
        var r = _s.Shrink(src, Balanced, Opt());

        Assert.Equal(ResultKind.Shrunk, r.Kind);
        Assert.Equal("PDF", r.OutputFormat);
        Assert.True(r.IsDocument);
        Assert.EndsWith("Quarterly report-docs.pdf", r.OutputPath);
        Assert.True(r.OutputBytes < r.OriginalBytes * 6 / 10, $"{r.OriginalBytes} -> {r.OutputBytes}");

        var d = r.Document!;
        Assert.Equal("PDF", d.Format);
        Assert.Equal(3, d.Pages);
        Assert.Equal(150, d.Dpi);
        Assert.Equal(4, d.Shrunk);   // three photos and the logo shared by all pages
        Assert.Equal(2, d.Kept);     // the small picture, and the screenshot (a smaller copy would not save bytes)
        Assert.True(d.PicturesAfter < d.PicturesBefore / 5, $"{d.PicturesBefore} -> {d.PicturesAfter}");
        Assert.Equal(3, d.Skipped);  // CMYK, transparency, inside a drawing group
        Assert.False(d.CheckFailed);

        Assert.Equal(3, PdfPages.Count(r.OutputPath!));
        for (int p = 0; p < 3; p++)
        {
            Assert.Equal(PdfPages.Text(src, p), PdfPages.Text(r.OutputPath!, p));
            var a = PdfPages.Render(src, p, 500);
            var b = PdfPages.Render(r.OutputPath!, p, 500);
            Assert.Equal((a.Width, a.Height), (b.Width, b.Height));
            Assert.True(PdfPages.Psnr(a.Bgra, b.Bgra) > 30, $"page {p + 1}: {PdfPages.Psnr(a.Bgra, b.Bgra):0.0} dB");
        }
        Assert.Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(src))));
    }

    [Fact]
    public void Pages_of_a_shrunk_pdf_can_be_compared()
    {
        var r = _s.Shrink(SampleImages.PdfReport(Path.Combine(Dir(), "compare.pdf")), Balanced, Opt());
        Assert.True(ComparePictures.CanCompare(r));
        var c = ComparePictures.Load(r);
        Assert.True(c.IsPdf);
        Assert.Equal(0, c.Page);
        Assert.Equal(3, c.PageCount);
        Assert.Equal(ComparePictures.PdfPageLongEdge, Math.Max(c.Width, c.Height));
        Assert.Equal(c.Width * c.Height * 4, c.Copy.Length);
        Assert.False(c.OriginalResized);
        var last = ComparePictures.LoadPdfPage(r, 9);
        Assert.Equal(2, last.Page);
        Assert.True(PdfPages.Psnr(last.Original, last.Copy) > 30);
    }

    [Fact]
    public void A_sharper_preset_keeps_more_detail()
    {
        string src = SampleImages.PdfReport(Path.Combine(Dir(), "presets.pdf"));
        var small = _s.Shrink(src, Small, Opt());
        var sharp = _s.Shrink(src, Sharp, Opt());
        Assert.Equal(100, small.Document!.Dpi);
        Assert.Equal(220, sharp.Document!.Dpi);
        Assert.True(small.OutputBytes < sharp.OutputBytes, $"small {small.OutputBytes}, sharp {sharp.OutputBytes}");
    }

    [Fact]
    public void Text_only_pdf_is_kept()
    {
        var r = _s.Shrink(SampleImages.PdfTextOnly(Path.Combine(Dir(), "text.pdf")), Balanced, Opt());
        Assert.Equal(ResultKind.Kept, r.Kind);
        Assert.True(r.IsDocument);
        Assert.Equal(0, r.Document!.Pictures);
        Assert.Null(r.OutputPath);
    }

    [Fact]
    public void Signed_pdf_is_not_changed()
    {
        var r = _s.Shrink(SampleImages.PdfSigned(Path.Combine(Dir(), "signed.pdf")), Balanced, Opt());
        Assert.Equal(ResultKind.Error, r.Kind);
        Assert.Equal(ErrorKind.PdfSigned, r.Error);
    }

    [Fact]
    public void Password_protected_pdf_is_not_changed()
    {
        string path = Path.Combine(Dir(), "locked.pdf");
        File.WriteAllBytes(path, Convert.FromBase64String(LockedPdf));
        var r = _s.Shrink(path, Balanced, Opt());
        Assert.Equal(ErrorKind.PdfLocked, r.Error);
    }

    [Fact]
    public void Broken_pdf_gets_the_damaged_message()
    {
        string path = Path.Combine(Dir(), "broken.pdf");
        File.WriteAllText(path, "%PDF-1.4\n% not really a PDF\n");
        Assert.Equal(ErrorKind.Damaged, _s.Shrink(path, Balanced, Opt()).Error);
    }

    [Fact]
    public void Psnr_of_identical_pictures_is_100()
    {
        var a = new byte[400];
        new Random(1).NextBytes(a);
        Assert.Equal(100, PdfPages.Psnr(a, (byte[])a.Clone()));
        var b = (byte[])a.Clone();
        b[0] ^= 0xFF;
        Assert.InRange(PdfPages.Psnr(a, b), 20, 99);
    }

    /// <summary>A one-page PDF encrypted with qpdf (user password "secret", AES-256): NACHO must not open or change it.</summary>
    private const string LockedPdf = "JVBERi0xLjcKJb/3ov4KMSAwIG9iago8PCAvRXh0ZW5zaW9ucyA8PCAvQURCRSA8PCAvQmFzZVZlcnNpb24gLzEuNyAvRXh0ZW5zaW9uTGV2ZWwgOCA+PiA+PiAvUGFnZU1vZGUgL1VzZU5vbmUgL1BhZ2VzIDMgMCBSIC9UeXBlIC9DYXRhbG9nID4+CmVuZG9iagoyIDAgb2JqCjw8IC9BdXRob3IgPGY5Yzk0MGQzYTBjZWNkNjFhYTM4MWNmMmY5NDc4YzczYzA4YTc0ZWZiMGNkY2NiMDBmMzJlZDgwNDZkZjM2OGE+IC9DcmVhdGlvbkRhdGUgPGUxYzVlZTRhYzg1ZjViOGY0N2FhYjcxMWJjNDg4MjUwNzYyZjgyYmFjODY5ZGFkMzQxYWY2YTYzODZiN2YwNTBhY2FmZjY2NmU4OWQzODVkMTYzYWU4YjI4NTU4MDA3Mj4gL0NyZWF0b3IgPDc0MGZlMzM5YzEzYTMxYzM5M2ViNTc1MzRkM2UxMDAzYWNkODgwMjJkOTgyYzZiMzAyMmNmYTU4MzZkMmMxNmU+IC9LZXl3b3JkcyA8MWZjYTIzNWM1MjQ5OTdhYWU2ODYwMDJiMWY0ZTNiNjI4MDhkN2UxOTA0OWMwYWRhY2JjYmUxYmExZTQyZTE5MT4gL01vZERhdGUgPDc2MGJiMDk2NDYxOTZhZjZlYjZkYmU3NDA1MGNhYzI2OWRiODI5YWRlNjk2NjVlZDQ1Yjc2ZjcyMzVlNjFlZDQzZjgwNjdlZTc3ZTEyM2QwOWYzY2I0MzAxNzRlMGY2Mz4gL1Byb2R1Y2VyIDwzYTVjYjVkMzczZGQ0Mzk3ZDc5YTk0ZmI2NGZkZjI4NmVjM2ZmYTk3NDFmNTliN2I3OTJiOTY4NzEyZmMwNjljZDkxNGJhNjZmNDFkNjZkZWNhNjBmOGRlYTQ2M2Y3ZDYwZDBmYTM0YzU5MTE3ZTQyMWQxMjljMDkzMDJiNGU1OD4gL1N1YmplY3QgPDE0ODUxYzg1NjU5MmJiMzE0ODFhNTUzNWExOGUxMTYyZDcyZTZlYTM5OWRkM2I5ZmEwOGM3MjFmZmE2NzY5MDA+IC9UaXRsZSA8ZmYyZjRjZDI5MzdjZWRiNzllZTM1YjljODI0MDFiZDk1ODUyODViYTBlZDhiZTJkN2Q0MmVhY2ZkOGQ5ZmQwND4gL1RyYXBwZWQgL0ZhbHNlID4+CmVuZG9iagozIDAgb2JqCjw8IC9Db3VudCAxIC9LaWRzIFsgNCAwIFIgXSAvVHlwZSAvUGFnZXMgPj4KZW5kb2JqCjQgMCBvYmoKPDwgL0NvbnRlbnRzIDUgMCBSIC9NZWRpYUJveCBbIDAgMCA1OTUuMjc1NiA4NDEuODg5OCBdIC9QYXJlbnQgMyAwIFIgL1Jlc291cmNlcyA8PCAvRm9udCA2IDAgUiAvUHJvY1NldCBbIC9QREYgL1RleHQgL0ltYWdlQiAvSW1hZ2VDIC9JbWFnZUkgXSA+PiAvUm90YXRlIDAgL1RyYW5zIDw8ID4+IC9UeXBlIC9QYWdlID4+CmVuZG9iago1IDAgb2JqCjw8IC9MZW5ndGggMTEyIC9GaWx0ZXIgL0ZsYXRlRGVjb2RlID4+CnN0cmVhbQqfsK7kaTp2sVLFNV+18cBqa8JEh3w4LV+rI7ovlK+CszxlDkXT8uIj8gdEkIpD0e0MxosbweZU1Gy1Bc+WoI0saeNbwPq51F+kLfiCxRSfwrUDIqUj8X8yYkLwwx4Au7hnXgqEcj30kC9mXXprArGfZW5kc3RyZWFtCmVuZG9iago2IDAgb2JqCjw8IC9GMSA3IDAgUiA+PgplbmRvYmoKNyAwIG9iago8PCAvQmFzZUZvbnQgL0hlbHZldGljYSAvRW5jb2RpbmcgL1dpbkFuc2lFbmNvZGluZyAvTmFtZSAvRjEgL1N1YnR5cGUgL1R5cGUxIC9UeXBlIC9Gb250ID4+CmVuZG9iago4IDAgb2JqCjw8IC9DRiA8PCAvU3RkQ0YgPDwgL0F1dGhFdmVudCAvRG9jT3BlbiAvQ0ZNIC9BRVNWMyAvTGVuZ3RoIDMyID4+ID4+IC9GaWx0ZXIgL1N0YW5kYXJkIC9MZW5ndGggMjU2IC9PIDwzMGU3MDIyNzQyZWI4YWY3MjQ0ZDNhMzA3ZWQwYmU4ZGY0YmM0OTc0OGFlMzM1NzlmYmMwZGIzYmZlODNlMWE1NTNjZTNhZDRhOGI0N2NiMTc5ZWM0MmY2Njg3NTA0NmU+IC9PRSA8YmFiZjVlOTA0ZTM4MDMxZjcxNmI5NTAyNzI3NjY3ZGUzYjQxMmRiZTFmMTAyOGUwNzM2MTk1YjgwMjUwZjIyZD4gL1AgLTQgL1Blcm1zIDw1YzI2ZGQwNmE5MTVhMjUwNDIyMjU3ZmYwYWJiOTk0Yz4gL1IgNiAvU3RtRiAvU3RkQ0YgL1N0ckYgL1N0ZENGIC9VIDxjZTAwNjE5N2I0YmY4MjU0ZWQ3NDQ1ZmIxZDAzZTdkZTgwODI4NWMzNzI5MjYyYmE1NWExOTI2OWIyNGRkMTRiNzYwMmFmYTQxNGQ5ZTQxZGY3ZWNlOWJlZmNmZjVkZjY+IC9VRSA8ZThiMTRjMTA0ZjE5YjJhODJmMGJmNmQ1Y2ZmM2ZiMWMwM2I5YWY1NzBkYWI4ZTBmMDE0N2Y2ZjRiYTA0YTJjND4gL1YgNSA+PgplbmRvYmoKeHJlZgowIDkKMDAwMDAwMDAwMCA2NTUzNSBmIAowMDAwMDAwMDE1IDAwMDAwIG4gCjAwMDAwMDAxNDkgMDAwMDAgbiAKMDAwMDAwMDkyNiAwMDAwMCBuIAowMDAwMDAwOTg1IDAwMDAwIG4gCjAwMDAwMDExODQgMDAwMDAgbiAKMDAwMDAwMTM2NyAwMDAwMCBuIAowMDAwMDAxMzk4IDAwMDAwIG4gCjAwMDAwMDE1MDUgMDAwMDAgbiAKdHJhaWxlciA8PCAvSW5mbyAyIDAgUiAvUm9vdCAxIDAgUiAvU2l6ZSA5IC9JRCBbPDg3NmM0ZTgwZDJlODg5NTI4YmU0NGJiOWZjZDQ4YTMyPjwxNGE2NjQ0M2VhNjdhNWNhMjllODU1MWYzM2U3MTY0ZT5dIC9FbmNyeXB0IDggMCBSID4+CnN0YXJ0eHJlZgoyMDUyCiUlRU9GCg==";
}
