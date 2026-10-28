using System.Globalization;
using Nacho.Core.Google;

namespace Nacho.Core.Word;

/// <summary>Where the Word file lives.</summary>
public enum WordWhere { ThisPc, Drive }

public enum WordError
{
    None,
    /// <summary>Not a .docx (an old .doc, or a password-protected file).</summary>
    Locked,
    Damaged,
    /// <summary>Word (or another program) has the file open, so it can't be replaced.</summary>
    InUse,
    /// <summary>The file changed after the check: check it again.</summary>
    Changed,
    /// <summary>The picked Drive file is not a Word file.</summary>
    NotWord,
    /// <summary>The user may only view this Drive file.</summary>
    ReadOnly,
    /// <summary>The backup of the original could not be written, so nothing was changed.</summary>
    BackupFailed,
    Gone,
}

public sealed class WordException : Exception
{
    public WordError Kind { get; }

    public WordException(WordError kind, string message, Exception? inner = null) : base(message, inner) => Kind = kind;
}

/// <summary>What the check found. The smaller file waits in a private work folder until the user decides;
/// nothing is written next to the original or in Drive before that.</summary>
public sealed class WordCheck : IDisposable
{
    public required WordWhere Where { get; init; }

    /// <summary>The file name, with .docx.</summary>
    public required string Name { get; init; }

    /// <summary>On this PC: the original. In Drive: the downloaded copy in the work folder.</summary>
    public required string SourcePath { get; init; }

    public DriveFile? Drive { get; init; }

    public required ShrinkResult Result { get; init; }

    public required string WorkFolder { get; init; }

    /// <summary>The original's size and last change at check time, to notice a change before writing.</summary>
    public long SourceLength { get; init; }

    public DateTime SourceWriteUtc { get; init; }

    public DateTime CheckedAt { get; init; } = DateTime.Now;

    public DocumentReport Report => Result.Document ?? new DocumentReport();

    public long BytesBefore => Result.OriginalBytes;

    public long BytesAfter => Result.Kind == ResultKind.Shrunk ? Result.OutputBytes : Result.OriginalBytes;

    /// <summary>A smaller file was made (false: the pictures are already small, or the file can't be read).</summary>
    public bool CanShrink => Result.Kind == ResultKind.Shrunk && Result.OutputPath is not null;

    public string Stem => Path.GetFileNameWithoutExtension(Name);

    public string Extension => Path.GetExtension(Name) is { Length: > 0 } e ? e.ToLowerInvariant() : ".docx";

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(WorkFolder)) Directory.Delete(WorkFolder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>What was written.</summary>
public sealed record WordOutcome(
    WordWhere Where,
    bool IntoCopy,
    string Name,
    string? LocalPath,
    DriveFile? DriveResult,
    string? BackupPath,
    bool WentToFallbackFolder,
    long BytesBefore,
    long BytesAfter,
    int Replaced);

/// <summary>
/// The same five steps as a Google Doc, for a Word file on this PC or in Google Drive: check (reads only), then a new
/// copy beside the original (the default) or the file itself. Changing the file itself on this PC first saves the
/// original in Docs-Ready\Word originals, so Restore can put it back; in Drive, the old content stays in Drive's
/// version history and a copy is saved on this PC too.
/// </summary>
public sealed class WordShrinker
{
    private readonly ImageShrinker _images;

    public WordShrinker(ImageShrinker images) => _images = images;

    /// <summary>Checks a Word file on this PC. The original is only read.</summary>
    public WordCheck CheckLocal(string path, Preset preset, ShrinkOptions options, Action<int, int>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(path)) throw new WordException(WordError.Gone, "The file is not there any more: " + path);
        var info = new FileInfo(path);
        return Check(WordWhere.ThisPc, info.Name, path, null, info.Length, info.LastWriteTimeUtc, preset, options, progress, ct, NewWorkFolder());
    }

    /// <summary>Downloads a Word file the user picked in Drive and checks it. Nothing in Drive changes.</summary>
    public async Task<WordCheck> CheckDriveAsync(GoogleApi api, string fileId, Preset preset, ShrinkOptions options,
        Action<int, int>? progress = null, CancellationToken ct = default)
    {
        var file = await api.GetFileAsync(fileId, ct).ConfigureAwait(false);
        if (!file.IsWord) throw new WordException(WordError.NotWord, "Not a Word file: " + file.MimeType);
        string work = NewWorkFolder();
        try
        {
            string local = Path.Combine(work, SafeName(file.Name, ".docx"));
            await api.DownloadFileAsync(fileId, local, ct).ConfigureAwait(false);
            var info = new FileInfo(local);
            return await Task.Run(() => Check(WordWhere.Drive, file.Name, local, file, info.Length, info.LastWriteTimeUtc, preset, options, progress, ct, work), ct)
                .ConfigureAwait(false);
        }
        catch
        {
            TryDeleteFolder(work);
            throw;
        }
    }

    private WordCheck Check(WordWhere where, string name, string path, DriveFile? drive, long length, DateTime writeUtc,
        Preset preset, ShrinkOptions options, Action<int, int>? progress, CancellationToken ct, string work)
    {
        try
        {
            string outFolder = Path.Combine(work, "smaller");
            Directory.CreateDirectory(outFolder);
            var opt = new ShrinkOptions
            {
                Format = options.Format,
                RemoveLocation = options.RemoveLocation,
                RemoveCamera = options.RemoveCamera,
                OutputFolder = outFolder,
                Suffix = "",
            };
            var r = new ShrinkResult { SourcePath = path, OriginalBytes = length };
            var docx = new DocxShrinker(_images) { Progress = progress, Cancel = ct };
            r = docx.Shrink(path, preset, opt, r);
            if (r.Kind == ResultKind.Error)
            {
                throw new WordException(r.Error == ErrorKind.DocLocked ? WordError.Locked : WordError.Damaged, r.ErrorDetail ?? r.Error.ToString());
            }
            return new WordCheck
            {
                Where = where,
                Name = name,
                SourcePath = path,
                Drive = drive,
                Result = r,
                WorkFolder = work,
                SourceLength = length,
                SourceWriteUtc = writeUtc,
            };
        }
        catch
        {
            TryDeleteFolder(work);
            throw;
        }
    }

    // ---------- this PC ----------

    /// <summary>Writes the result on this PC. <paramref name="copyPattern"/> is the localised "{0} (smaller)".
    /// <paramref name="fallbackFolder"/> takes the copy when the original's folder can't be written (Docs-Ready).
    /// <paramref name="backupFolder"/> keeps the original before the file itself is changed (Docs-Ready\Word originals).</summary>
    public WordOutcome ApplyLocal(WordCheck check, bool intoCopy, string copyPattern, string fallbackFolder, string backupFolder)
    {
        if (check.Where != WordWhere.ThisPc || !check.CanShrink) throw new InvalidOperationException("Nothing to write.");
        string smaller = check.Result.OutputPath!;
        string original = check.SourcePath;
        string folder = Path.GetDirectoryName(Path.GetFullPath(original))!;

        if (intoCopy)
        {
            string stem = GoogleText.CopyName(copyPattern, check.Stem);
            bool fallback = false;
            string dest;
            try
            {
                dest = CopyToFree(smaller, folder, stem, check.Extension, original);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                Directory.CreateDirectory(fallbackFolder);
                dest = CopyToFree(smaller, fallbackFolder, stem, check.Extension, original);
                fallback = true;
            }
            return new WordOutcome(WordWhere.ThisPc, true, Path.GetFileName(dest), dest, null, null, fallback,
                check.BytesBefore, new FileInfo(dest).Length, check.Report.Shrunk);
        }

        // The file itself: unchanged since the check, not open in Word, original saved first.
        EnsureUnchanged(check);
        EnsureNotInUse(original);
        string backup;
        try
        {
            Directory.CreateDirectory(backupFolder);
            string stamp = check.CheckedAt.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture);
            backup = CopyToFree(original, backupFolder, check.Stem + " " + stamp, check.Extension, null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new WordException(WordError.BackupFailed, ex.Message, ex);
        }

        // Put the new content beside the original first, then swap: the original is never half written.
        string temp = Path.Combine(folder, ".~nacho-" + Guid.NewGuid().ToString("N")[..8] + check.Extension);
        try
        {
            File.Copy(smaller, temp, overwrite: false);
            File.SetAttributes(temp, FileAttributes.Normal);
            File.Replace(temp, original, null, ignoreMetadataErrors: true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            TryDelete(temp);
            if (IsInUse(original)) throw new WordException(WordError.InUse, ex.Message, ex);
            throw;
        }
        return new WordOutcome(WordWhere.ThisPc, false, check.Name, original, null, backup, false,
            check.BytesBefore, new FileInfo(original).Length, check.Report.Shrunk);
    }

    /// <summary>Puts the original back from its backup (the Word window's Restore original).</summary>
    public static void Restore(WordOutcome outcome)
    {
        if (outcome.IntoCopy || outcome.LocalPath is not { } path || outcome.BackupPath is not { } backup) throw new InvalidOperationException("Nothing to restore.");
        if (!File.Exists(backup)) throw new WordException(WordError.Gone, "The saved original is not there any more: " + backup);
        EnsureNotInUse(path);
        string temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, ".~nacho-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(path));
        try
        {
            File.Copy(backup, temp, overwrite: false);
            if (File.Exists(path)) File.Replace(temp, path, null, ignoreMetadataErrors: true);
            else File.Move(temp, path);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void EnsureUnchanged(WordCheck check)
    {
        var info = new FileInfo(check.SourcePath);
        if (!info.Exists) throw new WordException(WordError.Gone, "The file is not there any more.");
        if (info.Length != check.SourceLength || info.LastWriteTimeUtc != check.SourceWriteUtc)
        {
            throw new WordException(WordError.Changed, "The file changed after the check.");
        }
    }

    private static void EnsureNotInUse(string path)
    {
        if (IsInUse(path)) throw new WordException(WordError.InUse, "The file is open in another program.");
    }

    /// <summary>Word on Windows locks the file; LibreOffice on Linux leaves a ".~lock.name#" file beside it.</summary>
    public static bool IsInUse(string path)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string name = Path.GetFileName(path);
        if (File.Exists(Path.Combine(dir, ".~lock." + name + "#"))) return true;
        if (!File.Exists(path)) return false;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ---------- Google Drive ----------

    /// <summary>Uploads the result: a new file in the same Drive folder (My Drive when NACHO may not write there),
    /// or a new version of the same file. In both cases nothing else in Drive changes.</summary>
    public async Task<WordOutcome> ApplyDriveAsync(GoogleApi api, WordCheck check, bool intoCopy, string copyPattern, string backupFolder,
        bool keepOriginalOnPc, CancellationToken ct = default)
    {
        if (check.Where != WordWhere.Drive || check.Drive is null || !check.CanShrink) throw new InvalidOperationException("Nothing to upload.");
        string smaller = check.Result.OutputPath!;
        var drive = check.Drive;

        if (intoCopy)
        {
            string name = GoogleText.CopyName(copyPattern, check.Stem) + check.Extension;
            bool fallback = false;
            DriveFile made;
            try
            {
                made = await api.UploadNewFileAsync(name, drive.ParentId, GoogleAuth.WordMime, smaller, ct).ConfigureAwait(false);
            }
            catch (GoogleException g) when (drive.ParentId is not null && g.Kind is GoogleError.NotFound or GoogleError.NoPermission or GoogleError.ReadOnly)
            {
                // A file shared from someone else's folder: put the copy in My Drive instead.
                made = await api.UploadNewFileAsync(name, null, GoogleAuth.WordMime, smaller, ct).ConfigureAwait(false);
                fallback = true;
            }
            return new WordOutcome(WordWhere.Drive, true, made.Name, null, made, null, fallback, check.BytesBefore, new FileInfo(smaller).Length, check.Report.Shrunk);
        }

        var now = await api.GetFileAsync(drive.Id, ct).ConfigureAwait(false);
        if (now.Modified != drive.Modified) throw new WordException(WordError.Changed, "The file changed in Drive after the check.");
        if (!now.CanEdit) throw new WordException(WordError.ReadOnly, "This Drive file can only be viewed.");
        string? backup = null;
        if (keepOriginalOnPc)
        {
            try
            {
                Directory.CreateDirectory(backupFolder);
                string stamp = check.CheckedAt.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture);
                backup = CopyToFree(check.SourcePath, backupFolder, check.Stem + " " + stamp, check.Extension, null);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                throw new WordException(WordError.BackupFailed, ex.Message, ex);
            }
        }
        DriveFile updated;
        try
        {
            updated = await api.UpdateFileContentAsync(drive.Id, GoogleAuth.WordMime, smaller, ct).ConfigureAwait(false);
        }
        catch (GoogleException g) when (g.Kind is GoogleError.ReadOnly or GoogleError.NoPermission)
        {
            throw new WordException(WordError.ReadOnly, g.Message, g);
        }
        return new WordOutcome(WordWhere.Drive, false, updated.Name.Length > 0 ? updated.Name : check.Name, null, updated, backup, false,
            check.BytesBefore, new FileInfo(smaller).Length, check.Report.Shrunk);
    }

    // ---------- helpers ----------

    private static string NewWorkFolder()
    {
        string work = Path.Combine(Path.GetTempPath(), "nacho-word-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        return work;
    }

    /// <summary>A Drive name made safe for this PC's file system.</summary>
    public static string SafeName(string name, string fallbackExt)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
        string clean = new string(name.Select(c => bad.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (clean.Length == 0) clean = "document";
        if (clean.Length > 120) clean = clean[..120];
        return Path.HasExtension(clean) ? clean : clean + fallbackExt;
    }

    /// <summary>Copies to "stem.ext", or "stem (2).ext" and so on; never replaces a file and never the source.</summary>
    private static string CopyToFree(string from, string folder, string stem, string ext, string? never)
    {
        string? avoid = never is null ? null : Path.GetFullPath(never);
        for (int i = 1; i < 10_000; i++)
        {
            string candidate = Path.Combine(folder, i == 1 ? stem + ext : $"{stem} ({i}){ext}");
            if (avoid is not null && string.Equals(Path.GetFullPath(candidate), avoid, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(candidate)) continue;
            try
            {
                File.Copy(from, candidate, overwrite: false);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
            }
        }
        throw new IOException("No free file name in " + folder);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>A Word file shrunk recently, for "Recently shrunk" and the start screen's "Last time".</summary>
public sealed record WordRecent(WordWhere Where, string Name, string Target, DateTime When, int Replaced, long BytesBefore, long BytesAfter, bool Copy)
{
    public const int Keep = 5;

    public static List<WordRecent> Add(IEnumerable<WordRecent>? list, WordRecent item) =>
        new[] { item }.Concat((list ?? Enumerable.Empty<WordRecent>()).Where(r => r is not null && !(r.Where == item.Where && r.Target == item.Target)))
            .Take(Keep).ToList();

    /// <summary>Only entries NACHO could have written come back from the settings file: a full .docx path, or a Drive file ID.</summary>
    public static bool IsSafe(WordRecent? r) => r is not null && !string.IsNullOrWhiteSpace(r.Name) && r.Where switch
    {
        WordWhere.ThisPc => Path.IsPathFullyQualified(r.Target) && r.Target.EndsWith(".docx", StringComparison.OrdinalIgnoreCase),
        WordWhere.Drive => System.Text.RegularExpressions.Regex.IsMatch(r.Target, "^[A-Za-z0-9_-]{10,200}$"),
        _ => false,
    };

    public static List<WordRecent> Clean(IEnumerable<WordRecent>? list) => (list ?? Enumerable.Empty<WordRecent>()).Where(IsSafe).Take(Keep).ToList();
}

/// <summary>Texts both Word windows share.</summary>
public static class WordText
{
    /// <summary>The string key for a Word error; Wd.ErrInUse takes the file name as {0}.</summary>
    public static string ErrorKey(WordError kind) => kind switch
    {
        WordError.Locked => "Err.DocLocked",
        WordError.Damaged => "Err.Damaged",
        WordError.InUse => "Wd.ErrInUse",
        WordError.Changed => "Wd.ErrChanged",
        WordError.NotWord => "Wd.ErrNotWord",
        WordError.ReadOnly => "Wd.ErrReadOnly",
        WordError.BackupFailed => "Wd.ErrBackup",
        WordError.Gone => "Wd.ErrGone",
        _ => "Gd.ErrOther",
    };

    /// <summary>Docs-Ready\Word originals: where the original goes before the file itself is changed.</summary>
    public static string BackupFolder(string outputFolder) => Path.Combine(outputFolder, "Word originals");

    /// <summary>"Docs-Ready › Word originals › name.docx": the last few parts of a path, enough to recognise it.</summary>
    public static string ShortPath(string path, int parts = 3)
    {
        var bits = Path.GetFullPath(path).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" › ", bits.Skip(Math.Max(0, bits.Length - parts)));
    }

    public static bool IsWordFile(string path) => path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);
}
