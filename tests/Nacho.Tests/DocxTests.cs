using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using ImageMagick;
using Nacho.Core;
using Xunit;

namespace Nacho.Tests;

/// <summary>Whole Word documents: every picture shrunk, links and content types kept right, the original untouched.</summary>
[Collection("Engine")]
public sealed class DocxTests : IClassFixture<SamplesFixture>
{
    private static readonly Preset Balanced = new("balanced", 1280, 300);
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Types = "http://schemas.openxmlformats.org/package/2006/content-types";
    private readonly SamplesFixture _f;
    private readonly ImageShrinker _s = new();

    public DocxTests(SamplesFixture f) => _f = f;

    private string Sample(string name) => SampleImages.WordDocument(Path.Combine(_f.Root, "docx-" + Guid.NewGuid().ToString("N")[..6], name));

    private ShrinkOptions Opt() => new() { OutputFolder = Path.Combine(_f.Root, "docx-out-" + Guid.NewGuid().ToString("N")[..6]) };

    private static Dictionary<string, byte[]> Parts(string docx)
    {
        using var z = ZipFile.OpenRead(docx);
        return z.Entries.Where(e => !e.FullName.EndsWith('/')).ToDictionary(e => e.FullName, e =>
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        });
    }

    private static XDocument Xml(Dictionary<string, byte[]> parts, string name) => XDocument.Load(new MemoryStream(parts[name]));

    [Fact]
    public void Every_picture_is_handled_and_the_document_gets_smaller()
    {
        string src = Sample("Team plan.docx");
        string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(src)));
        var r = _s.Shrink(src, Balanced, Opt());

        Assert.Equal(ResultKind.Shrunk, r.Kind);
        Assert.True(r.IsDocument);
        Assert.Equal("DOCX", r.OutputFormat);
        Assert.EndsWith("Team plan-docs.docx", r.OutputPath);
        Assert.True(r.OutputBytes < r.OriginalBytes / 4, $"{r.OriginalBytes} -> {r.OutputBytes}");
        Assert.Equal(new FileInfo(r.OutputPath!).Length, r.OutputBytes);

        var d = r.Document!;
        Assert.Equal(4, d.Pictures);
        Assert.Equal(3, d.Shrunk);
        Assert.Equal(1, d.Kept);
        Assert.Equal(1, d.Skipped);
        Assert.Equal(1, d.LocationRemoved);
        Assert.True(d.PicturesAfter < d.PicturesBefore);
        Assert.Contains(Badge.LocationRemoved, r.Badges);

        Assert.Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(src))));
    }

    [Fact]
    public void A_png_photo_becomes_a_jpeg_and_every_link_follows_it()
    {
        string src = Sample("photo-doc.docx");
        var r = _s.Shrink(src, Balanced, Opt());
        var parts = Parts(r.OutputPath!);
        var original = Parts(src);

        Assert.False(parts.ContainsKey("word/media/image1.png"));
        Assert.True(parts.ContainsKey("word/media/image1.jpg"));
        using (var jpg = new MagickImage(parts["word/media/image1.jpg"]))
        {
            Assert.Equal(MagickFormat.Jpeg, jpg.Format);
            Assert.Equal(1280u, jpg.Width);
            Assert.Equal(960u, jpg.Height);
        }

        var docRels = Xml(parts, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship").ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!);
        Assert.Equal("media/image1.jpg", docRels["rId10"]);
        Assert.Equal("media/image2.png", docRels["rId11"]);
        Assert.Equal("https://example.org/media/image1.png", docRels["rId20"]);
        var headerRel = Xml(parts, "word/_rels/header1.xml.rels").Root!.Element(Rel + "Relationship")!;
        Assert.Equal("/word/media/image1.jpg", (string)headerRel.Attribute("Target")!);

        var types = Xml(parts, "[Content_Types].xml").Root!;
        Assert.Contains(types.Elements(Types + "Default"), e => (string)e.Attribute("Extension")! == "jpg" && (string)e.Attribute("ContentType")! == "image/jpeg");
        var over = types.Elements(Types + "Override").Single(e => ((string)e.Attribute("PartName")!).Contains("image1"));
        Assert.Equal("/word/media/image1.jpg", (string)over.Attribute("PartName")!);
        Assert.Equal("image/jpeg", (string)over.Attribute("ContentType")!);

        // The text and the layout are copied byte for byte; so are the drawing and the logo that was already fine.
        Assert.Equal(original["word/document.xml"], parts["word/document.xml"]);
        Assert.Equal(original["word/header1.xml"], parts["word/header1.xml"]);
        Assert.Equal(original["word/media/image5.emf"], parts["word/media/image5.emf"]);
        Assert.Equal(original["word/media/image3.png"], parts["word/media/image3.png"]);
        Assert.Equal(original.Count, parts.Count);
    }

    [Fact]
    public void Pictures_keep_their_shape_and_lose_their_location()
    {
        var r = _s.Shrink(Sample("shape.docx"), Balanced, Opt());
        var parts = Parts(r.OutputPath!);
        using var shot = new MagickImage(parts["word/media/image2.png"]);
        Assert.Equal(MagickFormat.Png, shot.Format);
        Assert.Equal(1280u, shot.Width);
        Assert.Equal(768u, shot.Height);
        using var gps = new MagickImage(parts["word/media/image4.jpeg"]);
        Assert.False(ImageShrinker.HasGps(gps.GetExifProfile()));
        Assert.Equal(1280u, gps.Width);
        Assert.Equal(960u, gps.Height);
    }

    [Fact]
    public void A_document_with_nothing_to_shrink_is_kept()
    {
        string src = Path.Combine(_f.Root, "small-" + Guid.NewGuid().ToString("N")[..6] + ".docx");
        using (var z = ZipFile.Open(src, ZipArchiveMode.Create))
        {
            void Add(string n, string t) { using var w = new StreamWriter(z.CreateEntry(n).Open()); w.Write(t); }
            Add("[Content_Types].xml", """<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="xml" ContentType="application/xml"/></Types>""");
            Add("word/document.xml", """<?xml version="1.0"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body/></w:document>""");
        }
        var r = _s.Shrink(src, Balanced, Opt());
        Assert.Equal(ResultKind.Kept, r.Kind);
        Assert.True(r.IsDocument);
        Assert.Equal(0, r.Document!.Pictures);
        Assert.Null(r.OutputPath);
        Assert.Equal(src, r.DeliverPath);
    }

    [Fact]
    public void Protected_old_and_broken_word_files_get_clear_errors()
    {
        string dir = Path.Combine(_f.Root, "bad-docs-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        string locked = Path.Combine(dir, "locked.docx");
        File.WriteAllBytes(locked, new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0 });
        string old = Path.Combine(dir, "old.doc");
        File.WriteAllBytes(old, new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0 });
        string broken = Path.Combine(dir, "broken.docx");
        File.WriteAllBytes(broken, "PK\u0003\u0004 this is not really a zip"u8.ToArray());
        string notWord = Path.Combine(dir, "photos.docx");
        using (var z = ZipFile.Open(notWord, ZipArchiveMode.Create)) z.CreateEntry("hello.txt");

        Assert.Equal(ErrorKind.DocLocked, _s.Shrink(locked, Balanced, Opt()).Error);
        Assert.Equal(ErrorKind.DocLocked, _s.Shrink(old, Balanced, Opt()).Error);
        Assert.Equal(ErrorKind.Damaged, _s.Shrink(broken, Balanced, Opt()).Error);
        Assert.Equal(ErrorKind.Damaged, _s.Shrink(notWord, Balanced, Opt()).Error);
    }

    [Fact]
    public void A_second_run_never_replaces_the_first_copy()
    {
        string src = Sample("twice.docx");
        var o = Opt();
        var a = _s.Shrink(src, Balanced, o);
        var b = _s.Shrink(src, Balanced, o);
        Assert.NotEqual(a.OutputPath, b.OutputPath);
        Assert.EndsWith("twice-docs (2).docx", b.OutputPath);
        Assert.True(File.Exists(a.OutputPath));
    }

    [Theory]
    [InlineData("word", "media/image1.png", "word/media/image1.png")]
    [InlineData("word", "/word/media/image1.png", "word/media/image1.png")]
    [InlineData("word/glossary", "../media/a%20b.png", "word/media/a b.png")]
    [InlineData("", "word/document.xml", "word/document.xml")]
    [InlineData("word", "./media/x.gif", "word/media/x.gif")]
    public void Relationship_targets_resolve_like_Word_does(string folder, string target, string expected)
    {
        Assert.Equal(expected, DocxShrinker.Resolve(folder, target));
    }
}
