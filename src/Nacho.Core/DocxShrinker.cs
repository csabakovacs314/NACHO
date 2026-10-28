using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Nacho.Core;

/// <summary>
/// Makes a Docs-ready copy of a whole Word document: every picture inside goes through the same engine as a
/// dropped image, and the result is a new .docx beside the other copies. The original file is never written.
/// Typical use: Google Docs > File > Download > Microsoft Word (.docx), NACHO, then upload and open with Docs.
/// </summary>
/// <remarks>
/// A .docx is a zip of XML parts. Pictures live under word/ (normally word/media) and are found through
/// relationship files (*.rels) and [Content_Types].xml. When a picture changes type (a PNG photo becomes a
/// JPEG) its part is renamed and both are updated; the document text itself is copied untouched. Sizes on the
/// page are stored in the document, so a smaller picture with the same shape shows exactly where it was.
/// </remarks>
public sealed class DocxShrinker
{
    private const int MaxEntries = 20_000;
    private const long MaxUnpackedBytes = 2L * 1024 * 1024 * 1024;
    private const long MaxPictureBytes = 256L * 1024 * 1024;

    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace TypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    private static readonly HashSet<string> PictureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif", ".avif",
    };

    /// <summary>Already compressed, so the zip stores them as they are.</summary>
    private static readonly HashSet<string> StoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif",
    };

    private readonly ImageShrinker _images;

    public DocxShrinker(ImageShrinker images) => _images = images;

    /// <summary>Called after each picture inside the document: (pictures looked at, pictures in the document).</summary>
    public Action<int, int>? Progress { get; set; }

    /// <summary>Stops between pictures when cancelled (the Word window's Stop).</summary>
    public CancellationToken Cancel { get; set; }

    /// <summary>True for .docx and .docm, and for the old .doc (which gets a clear message instead of a shrink).</summary>
    public static bool IsWordFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".docx" or ".docm" or ".doc";

    public ShrinkResult Shrink(string path, Preset preset, ShrinkOptions opt, ShrinkResult r)
    {
        r.SourceFormat = "DOCX";
        byte[] head = ReadHead(path, 8);
        // Old .doc files and password-protected .docx files are OLE compound files, not zips.
        if (head.Length >= 4 && head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0)
        {
            return ImageShrinker.Fail(r, ErrorKind.DocLocked, null);
        }
        if (Path.GetExtension(path).Equals(".doc", StringComparison.OrdinalIgnoreCase)) return ImageShrinker.Fail(r, ErrorKind.DocLocked, null);
        if (head.Length < 4 || head[0] != (byte)'P' || head[1] != (byte)'K') return ImageShrinker.Fail(r, ErrorKind.Damaged, null);

        string work = Path.Combine(Path.GetTempPath(), "nacho-docx-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            return ShrinkCore(path, preset, opt, r, work);
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or InvalidOperationException or NotSupportedException)
        {
            return ImageShrinker.Fail(r, ErrorKind.Damaged, ex.Message);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp folder, best effort */ }
        }
    }

    private ShrinkResult ShrinkCore(string path, Preset preset, ShrinkOptions opt, ShrinkResult r, string work)
    {
        using var zin = ZipFile.OpenRead(path);
        var entries = zin.Entries.ToList();
        if (entries.Count > MaxEntries || entries.Sum(e => e.Length) > MaxUnpackedBytes) return ImageShrinker.Fail(r, ErrorKind.Damaged, "too big");
        if (Find(entries, "[Content_Types].xml") is null || Find(entries, "word/document.xml") is null)
        {
            return ImageShrinker.Fail(r, ErrorKind.Damaged, "not a Word document");
        }

        var report = new DocumentReport();
        r.Document = report;
        var names = new HashSet<string>(entries.Select(e => e.FullName), StringComparer.OrdinalIgnoreCase);
        var replaced = new Dictionary<string, (string NewName, byte[] Bytes)>(StringComparer.OrdinalIgnoreCase);
        bool camera = false;
        string outDir = Path.Combine(work, "out");
        var pictureOptions = new ShrinkOptions
        {
            Format = FormatMode.Auto,
            RemoveLocation = opt.RemoveLocation,
            RemoveCamera = opt.RemoveCamera,
            OutputFolder = outDir,
            Suffix = "",
        };

        int n = 0, seen = 0;
        int total = entries.Count(e => e.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase) && !e.FullName.EndsWith('/') && e.Length > 0
            && (PictureExtensions.Contains(Path.GetExtension(e.Name)) || Path.GetExtension(e.Name).ToLowerInvariant() is ".emf" or ".wmf" or ".svg"));
        Progress?.Invoke(0, total);
        foreach (var e in entries)
        {
            if (!e.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith('/') || e.Length == 0) continue;
            string ext = Path.GetExtension(e.Name);
            bool counted = PictureExtensions.Contains(ext) || ext.ToLowerInvariant() is ".emf" or ".wmf" or ".svg";
            if (counted)
            {
                Cancel.ThrowIfCancellationRequested();
                Progress?.Invoke(seen++, total);
            }
            if (ext.Equals(".emf", StringComparison.OrdinalIgnoreCase) || ext.Equals(".wmf", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".svg", StringComparison.OrdinalIgnoreCase))
            {
                report.Skipped++;
                continue;
            }
            if (!PictureExtensions.Contains(ext)) continue;
            if (e.Length > MaxPictureBytes)
            {
                report.Skipped++;
                continue;
            }

            string src = Path.Combine(work, $"p{n++}{ext.ToLowerInvariant()}");
            e.ExtractToFile(src);
            var pr = _images.Shrink(src, preset, pictureOptions);
            if (pr.Kind == ResultKind.Error)
            {
                report.Skipped++;
                continue;
            }

            report.Pictures++;
            report.PicturesBefore += e.Length;
            bool privacy = pr.Badges.Contains(Badge.LocationRemoved) || pr.Badges.Contains(Badge.CameraRemoved);
            if (pr.Kind == ResultKind.Shrunk && pr.OutputPath is { } made && (pr.OutputBytes < e.Length || privacy))
            {
                byte[] bytes = File.ReadAllBytes(made);
                File.Delete(made);
                string newExt = Path.GetExtension(made).ToLowerInvariant();
                string newName = SameType(ext, newExt) ? e.FullName : FreeName(e.FullName, newExt, names);
                names.Add(newName);
                replaced[e.FullName] = (newName, bytes);
                report.Shrunk++;
                report.PicturesAfter += bytes.LongLength;
                if (pr.Badges.Contains(Badge.LocationRemoved)) report.LocationRemoved++;
                camera |= pr.Badges.Contains(Badge.CameraRemoved);
            }
            else
            {
                report.Kept++;
                report.PicturesAfter += e.Length;
            }
            TryDelete(src);
        }

        Progress?.Invoke(total, total);
        if (replaced.Count == 0) return KeepDocument(r, report);

        var renames = replaced.Where(kv => !kv.Key.Equals(kv.Value.NewName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value.NewName, StringComparer.OrdinalIgnoreCase);
        var rewritten = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (renames.Count > 0)
        {
            var typesEntry = Find(entries, "[Content_Types].xml")!;
            rewritten[typesEntry.FullName] = Serialise(UpdateContentTypes(Load(typesEntry), renames));
            foreach (var rels in entries.Where(e => e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
            {
                var doc = Load(rels);
                if (UpdateRelationships(doc, SourceFolder(rels.FullName), renames)) rewritten[rels.FullName] = Serialise(doc);
            }
        }

        string temp = Path.Combine(work, "result.docx");
        using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
        using (var zout = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var e in entries)
            {
                if (e.FullName.EndsWith('/'))
                {
                    zout.CreateEntry(e.FullName);
                    continue;
                }
                string name = e.FullName;
                byte[]? data = null;
                if (replaced.TryGetValue(e.FullName, out var rep))
                {
                    name = rep.NewName;
                    data = rep.Bytes;
                }
                else if (rewritten.TryGetValue(e.FullName, out var xml))
                {
                    data = xml;
                }
                var level = StoredExtensions.Contains(Path.GetExtension(name)) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                var ne = zout.CreateEntry(name, level);
                ne.LastWriteTime = e.LastWriteTime;
                using var o = ne.Open();
                if (data is not null)
                {
                    o.Write(data, 0, data.Length);
                }
                else
                {
                    using var i = e.Open();
                    i.CopyTo(o);
                }
            }
        }

        Verify(temp, entries.Count, replaced, renames);

        long size = new FileInfo(temp).Length;
        bool privacyChange = report.LocationRemoved > 0 || camera;
        if (size >= r.OriginalBytes && !privacyChange) return KeepDocument(r, report);

        Directory.CreateDirectory(opt.OutputFolder);
        string stem = Path.GetFileNameWithoutExtension(path) + opt.Suffix;
        string outPath = MoveToNew(opt.OutputFolder, stem, Path.GetExtension(path).ToLowerInvariant(), temp, path);

        r.Kind = ResultKind.Shrunk;
        r.OutputPath = outPath;
        r.OutputBytes = size;
        r.OutputFormat = "DOCX";
        if (report.LocationRemoved > 0) r.Badges.Add(Badge.LocationRemoved);
        if (camera) r.Badges.Add(Badge.CameraRemoved);
        return r;
    }

    private static ShrinkResult KeepDocument(ShrinkResult r, DocumentReport report)
    {
        ImageShrinker.Keep(r, false);
        r.Document = report;
        return r;
    }

    private static bool SameType(string a, string b) => Kind(a) == Kind(b);

    private static string Kind(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".jpe" or ".jfif" => "jpeg",
        ".tif" or ".tiff" => "tiff",
        ".heic" or ".heif" => "heic",
        var other => other,
    };

    private static string MimeType(string ext) => Kind(ext) switch
    {
        "jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        _ => "application/octet-stream",
    };

    /// <summary>The same folder and stem with the new extension, or stem-2, stem-3... when that name is taken.</summary>
    private static string FreeName(string oldName, string newExt, HashSet<string> names)
    {
        int slash = oldName.LastIndexOf('/');
        string folder = oldName[..(slash + 1)];
        string stem = Path.GetFileNameWithoutExtension(oldName[(slash + 1)..]);
        string candidate = folder + stem + newExt;
        for (int i = 2; names.Contains(candidate); i++) candidate = $"{folder}{stem}-{i}{newExt}";
        return candidate;
    }

    private static ZipArchiveEntry? Find(IEnumerable<ZipArchiveEntry> entries, string name) =>
        entries.FirstOrDefault(e => e.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static XDocument Load(ZipArchiveEntry e)
    {
        using var s = e.Open();
        return XDocument.Load(s, LoadOptions.PreserveWhitespace);
    }

    private static byte[] Serialise(XDocument doc)
    {
        using var ms = new MemoryStream();
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false };
        using (var w = XmlWriter.Create(ms, settings)) doc.Save(w);
        return ms.ToArray();
    }

    /// <summary>"word/_rels/document.xml.rels" belongs to word/document.xml, so its targets start in "word/".</summary>
    private static string SourceFolder(string relsPath)
    {
        string dir = relsPath.Contains('/') ? relsPath[..relsPath.LastIndexOf('/')] : "";
        if (dir.EndsWith("_rels", StringComparison.OrdinalIgnoreCase)) dir = dir[..^5];
        return dir.TrimEnd('/');
    }

    /// <summary>A relationship target as a zip entry name: relative to the source part, or absolute with a leading /.</summary>
    public static string Resolve(string sourceFolder, string target)
    {
        string t = Uri.UnescapeDataString(target).Replace('\\', '/');
        int cut = t.IndexOfAny(new[] { '#', '?' });
        if (cut >= 0) t = t[..cut];
        var parts = t.StartsWith('/') ? new List<string>() : sourceFolder.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (string seg in t.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else if (seg != ".") parts.Add(seg);
        }
        return string.Join('/', parts);
    }

    private static bool UpdateRelationships(XDocument doc, string sourceFolder, Dictionary<string, string> renames)
    {
        bool changed = false;
        foreach (var rel in doc.Root?.Elements(RelNs + "Relationship") ?? Enumerable.Empty<XElement>())
        {
            if (string.Equals((string?)rel.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase)) continue;
            if (rel.Attribute("Target") is not { } attr) continue;
            if (!renames.TryGetValue(Resolve(sourceFolder, attr.Value), out string? newName)) continue;
            string target = attr.Value;
            string newFile = newName[(newName.LastIndexOf('/') + 1)..];
            if (target.Contains('%')) newFile = Uri.EscapeDataString(newFile);
            attr.Value = target[..(target.LastIndexOf('/') + 1)] + newFile;
            changed = true;
        }
        return changed;
    }

    private static XDocument UpdateContentTypes(XDocument doc, Dictionary<string, string> renames)
    {
        var root = doc.Root ?? throw new XmlException("Empty [Content_Types].xml");
        foreach (var (oldName, newName) in renames)
        {
            foreach (var o in root.Elements(TypesNs + "Override")
                         .Where(o => string.Equals(((string?)o.Attribute("PartName"))?.TrimStart('/'), oldName, StringComparison.OrdinalIgnoreCase)))
            {
                o.SetAttributeValue("PartName", "/" + newName);
                o.SetAttributeValue("ContentType", MimeType(Path.GetExtension(newName)));
            }
        }
        foreach (string ext in renames.Values.Select(v => Path.GetExtension(v).TrimStart('.').ToLowerInvariant()).Distinct())
        {
            bool known = root.Elements(TypesNs + "Default")
                .Any(d => string.Equals((string?)d.Attribute("Extension"), ext, StringComparison.OrdinalIgnoreCase));
            if (known) continue;
            var def = new XElement(TypesNs + "Default", new XAttribute("Extension", ext), new XAttribute("ContentType", MimeType("." + ext)));
            var lastDefault = root.Elements(TypesNs + "Default").LastOrDefault();
            if (lastDefault is not null) lastDefault.AddAfterSelf(def);
            else root.AddFirst(def);
        }
        return doc;
    }

    /// <summary>
    /// Checks the new file before anyone sees it: same number of parts, every replaced picture present with the
    /// new bytes, no relationship still pointing at a renamed part, and a content type for every picture.
    /// </summary>
    private static void Verify(string file, int entryCount, Dictionary<string, (string NewName, byte[] Bytes)> replaced,
        Dictionary<string, string> renames)
    {
        using var z = ZipFile.OpenRead(file);
        var byName = z.Entries.ToDictionary(e => e.FullName, StringComparer.OrdinalIgnoreCase);
        if (z.Entries.Count != entryCount) throw new InvalidDataException("part count changed");
        foreach (var (_, (newName, bytes)) in replaced)
        {
            if (!byName.TryGetValue(newName, out var e) || e.Length != bytes.LongLength) throw new InvalidDataException("missing " + newName);
        }
        foreach (string gone in renames.Keys)
        {
            if (!renames.ContainsValue(gone) && byName.ContainsKey(gone)) throw new InvalidDataException("old part still there: " + gone);
        }
        foreach (var rels in z.Entries.Where(e => e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var doc = Load(rels);
            string folder = SourceFolder(rels.FullName);
            foreach (var rel in doc.Root?.Elements(RelNs + "Relationship") ?? Enumerable.Empty<XElement>())
            {
                if (string.Equals((string?)rel.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase)) continue;
                string resolved = Resolve(folder, (string?)rel.Attribute("Target") ?? "");
                if (renames.ContainsKey(resolved)) throw new InvalidDataException("link to renamed part: " + resolved);
            }
        }
        var types = Load(byName["[Content_Types].xml"]).Root!;
        var defaults = new HashSet<string>(types.Elements(TypesNs + "Default").Select(d => (string?)d.Attribute("Extension") ?? ""), StringComparer.OrdinalIgnoreCase);
        var overrides = new HashSet<string>(types.Elements(TypesNs + "Override").Select(o => ((string?)o.Attribute("PartName") ?? "").TrimStart('/')), StringComparer.OrdinalIgnoreCase);
        foreach (var (_, (newName, _)) in replaced)
        {
            if (!defaults.Contains(Path.GetExtension(newName).TrimStart('.')) && !overrides.Contains(newName))
            {
                throw new InvalidDataException("no content type for " + newName);
            }
        }
    }

    /// <summary>Moves the finished file to a new name in the output folder; never replaces a file or the source.</summary>
    private static string MoveToNew(string folder, string stem, string ext, string temp, string source)
    {
        string full = Path.GetFullPath(source);
        for (int i = 1; i < 10_000; i++)
        {
            string candidate = Path.Combine(folder, i == 1 ? stem + ext : $"{stem} ({i}){ext}");
            if (string.Equals(Path.GetFullPath(candidate), full, StringComparison.OrdinalIgnoreCase) || File.Exists(candidate)) continue;
            try
            {
                File.Move(temp, candidate, overwrite: false);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Another copy took this name a moment ago; try the next one.
            }
        }
        throw new IOException("No free file name in " + folder);
    }

    private static byte[] ReadHead(string path, int count)
    {
        using var fs = File.OpenRead(path);
        var buf = new byte[count];
        int n = fs.Read(buf, 0, count);
        return buf[..n];
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* temp file, best effort */ }
    }
}
