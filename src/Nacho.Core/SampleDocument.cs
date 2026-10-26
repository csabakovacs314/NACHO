using System.IO.Compression;
using System.Text;
using ImageMagick;
using ImageMagick.Drawing;

namespace Nacho.Core;

public static partial class SampleImages
{
    /// <summary>
    /// A small Word document like one downloaded from Google Docs, with the cases the document shrinker must get
    /// right: a large PNG photo that becomes a JPEG (so its part is renamed) and is used in both the body and the
    /// header (relative and absolute links), a screenshot that stays PNG, a small logo that is already fine, a
    /// JPEG with GPS, an EMF drawing that must be left alone, and an Override in [Content_Types].xml.
    /// </summary>
    public static string WordDocument(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        byte[] photoPng, screenshotPng, logoPng, gpsJpeg;
        using (var img = new MagickImage("plasma:", new MagickReadSettings { Width = 2400, Height = 1800 }))
        {
            img.AddNoise(NoiseType.Gaussian, 0.3);
            photoPng = img.ToByteArray(MagickFormat.Png);
        }
        using (var img = new MagickImage(MagickColors.White, 2000, 1200))
        {
            var d = new Drawables().FillColor(new MagickColor("#E6E6E6")).Rectangle(0, 0, 2000, 80).FillColor(new MagickColor("#2B2B2B"));
            var rnd = new Random(3);
            for (int line = 0; line < 30; line++)
            {
                double y = 140 + line * 32, x = 80;
                while (x < 1500)
                {
                    double w = 8 + rnd.Next(30);
                    d.Rectangle(x, y, x + w, y + 14);
                    x += w + 6;
                }
            }
            d.Draw(img);
            img.Quality = 0;
            screenshotPng = img.ToByteArray(MagickFormat.Png24);
        }
        using (var img = new MagickImage(MagickColors.White, 300, 300))
        {
            new Drawables().FillColor(new MagickColor("#E8A33D")).Circle(150, 150, 150, 20).Draw(img);
            logoPng = img.ToByteArray(MagickFormat.Png8);
        }
        using (var img = new MagickImage("plasma:", new MagickReadSettings { Width = 1600, Height = 1200 }))
        {
            img.SetProfile(CameraExif(orientation: 1, gps: true));
            img.Quality = 92;
            gpsJpeg = img.ToByteArray(MagickFormat.Jpeg);
        }
        // Not a real drawing: NACHO must leave EMF parts alone without reading them.
        byte[] emf = Encoding.ASCII.GetBytes("EMF placeholder for the NACHO sample document");

        const string types = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="png" ContentType="image/png"/><Default Extension="jpeg" ContentType="image/jpeg"/><Default Extension="emf" ContentType="image/x-emf"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/><Override PartName="/word/media/image1.png" ContentType="image/png"/></Types>
            """;
        const string rootRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
            """;
        const string docRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/><Relationship Id="rId10" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/><Relationship Id="rId11" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image2.png"/><Relationship Id="rId12" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image3.png"/><Relationship Id="rId13" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image4.jpeg"/><Relationship Id="rId14" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image5.emf"/><Relationship Id="rId20" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.org/media/image1.png" TargetMode="External"/></Relationships>
            """;
        const string headerRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="/word/media/image1.png"/></Relationships>
            """;

        string body = Para("NACHO sample document") + Picture("rId10", 1, 5486400, 4114800) + Para("A screenshot:")
            + Picture("rId11", 2, 5486400, 3291840) + Picture("rId12", 3, 914400, 914400) + Picture("rId13", 4, 5486400, 4114800)
            + Para("The end.");
        string document = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document {Ns}><w:body>{body}<w:sectPr><w:headerReference w:type="default" r:id="rId1"/><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="708" w:footer="708" w:gutter="0"/></w:sectPr></w:body></w:document>
            """;
        string header = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr {Ns}>{Picture("rId1", 9, 914400, 685800)}</w:hdr>
            """;

        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Text(string name, string content) => Add(zip, name, Encoding.UTF8.GetBytes(content.Trim()));
        Text("[Content_Types].xml", types);
        Text("_rels/.rels", rootRels);
        Text("word/document.xml", document);
        Text("word/_rels/document.xml.rels", docRels);
        Text("word/header1.xml", header);
        Text("word/_rels/header1.xml.rels", headerRels);
        Add(zip, "word/media/image1.png", photoPng);
        Add(zip, "word/media/image2.png", screenshotPng);
        Add(zip, "word/media/image3.png", logoPng);
        Add(zip, "word/media/image4.jpeg", gpsJpeg);
        Add(zip, "word/media/image5.emf", emf);
        return path;
    }

    private const string Ns = """xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture" """;

    private static string Para(string text) => $"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static string Picture(string rid, int id, long cx, long cy) =>
        $"""<w:p><w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0"><wp:extent cx="{cx}" cy="{cy}"/><wp:docPr id="{id}" name="Picture {id}"/><a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:nvPicPr><pic:cNvPr id="{id}" name="Picture {id}"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="{rid}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>""";

    private static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        e.LastWriteTime = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        using var s = e.Open();
        s.Write(bytes, 0, bytes.Length);
    }
}
