using System.Globalization;
using ImageMagick;

namespace Nacho.Core.Google;

public enum GooglePictureState
{
    /// <summary>Not looked at yet.</summary>
    Pending,
    /// <summary>A smaller copy is ready and will replace the picture.</summary>
    WillShrink,
    /// <summary>Already Docs-ready; left as it is.</summary>
    Kept,
    /// <summary>A drawing, a linked chart or a picture with Docs effects NACHO would lose; left as it is.</summary>
    Skipped,
    /// <summary>Could not be downloaded or read; left as it is.</summary>
    Failed,
    /// <summary>Replaced in the Doc by the smaller copy.</summary>
    Replaced,
    /// <summary>Changed or removed in the Doc after the check; left as it is.</summary>
    Changed,
}

/// <summary>One picture of the Doc and what NACHO did or will do with it.</summary>
public sealed class GooglePictureJob
{
    public GooglePictureJob(GooglePicture picture, int index)
    {
        Picture = picture;
        Index = index;
    }

    public GooglePicture Picture { get; }
    public int Index { get; }
    public GooglePictureState State { get; set; } = GooglePictureState.Pending;
    public long OriginalBytes { get; set; }
    public long NewBytes { get; set; }
    public int OriginalWidth { get; set; }
    public int OriginalHeight { get; set; }
    public int NewWidth { get; set; }
    public int NewHeight { get; set; }
    public string NewFormat { get; set; } = "";
    public string? NewPath { get; set; }
    public string? OriginalPath { get; set; }
    public string? Error { get; set; }

    /// <summary>Where the replacement goes: the same picture in the Doc, or the matching picture in NACHO's copy.</summary>
    public string TargetObjectId { get; internal set; } = "";
    public string TargetTabId { get; internal set; } = "";

    public string Mime => NewFormat switch { "PNG" => "image/png", "GIF" => "image/gif", _ => "image/jpeg" };

    /// <summary>The bytes this picture takes in the Doc now (after Apply) or will take (after Check).</summary>
    public long BytesAfter => State is GooglePictureState.WillShrink or GooglePictureState.Replaced ? NewBytes : OriginalBytes;
}

/// <summary>What a check of the Doc found: everything NACHO needs to replace the pictures, nothing changed yet.</summary>
public sealed class GoogleDocPlan : IDisposable
{
    internal GoogleDocPlan(GoogleDocInfo doc, List<GooglePictureJob> jobs, string work, string? originals)
    {
        Doc = doc;
        Jobs = jobs;
        WorkFolder = work;
        OriginalsFolder = originals;
        foreach (var j in jobs)
        {
            j.TargetObjectId = j.Picture.ObjectId;
            j.TargetTabId = j.Picture.TabId;
        }
    }

    public GoogleDocInfo Doc { get; }
    public IReadOnlyList<GooglePictureJob> Jobs { get; }
    public string WorkFolder { get; }

    /// <summary>Where copies of the original pictures were saved, or null when the user turned that off.</summary>
    public string? OriginalsFolder { get; }

    public int Count(GooglePictureState s) => Jobs.Count(j => j.State == s);
    public int ToReplace => Count(GooglePictureState.WillShrink);
    public long BytesBefore => Jobs.Sum(j => j.OriginalBytes);
    public long BytesAfter => Jobs.Sum(j => j.BytesAfter);
    public bool Cancelled { get; internal set; }

    /// <summary>The Doc the pictures go into: the checked Doc, or NACHO's copy of it after <see cref="GoogleDocShrinker.CopyAsync"/>.</summary>
    public string TargetDocId => CopyId ?? Doc.Id;

    /// <summary>NACHO's copy of the Doc (null when working in the Doc itself). The original is then never written to.</summary>
    public string? CopyId { get; internal set; }
    public string? CopyName { get; internal set; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(WorkFolder)) Directory.Delete(WorkFolder, recursive: true);
        }
        catch (IOException)
        {
            // Temp folder; best effort.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Progress of a check or a replacement. <paramref name="Job"/> is the picture that just finished (null for
/// the first report); <paramref name="Doc"/> is set once the Doc has been read, so the window can show it at once.</summary>
public sealed record GoogleProgress(int Done, int Total, GooglePictureJob? Job = null, GoogleDocInfo? Doc = null);

/// <summary>
/// Shrinks the pictures inside a Google Doc, in steps the user sees:
/// <see cref="CheckAsync"/> reads the Doc, downloads every picture and makes the smaller copies on this computer
/// without changing anything; <see cref="CopyAsync"/> (the default in the apps) makes a copy of the Doc so the
/// original stays as it is; <see cref="ApplyAsync"/> then replaces the pictures in the copy, or in the Doc itself. Each replacement keeps the
/// picture's place, size and wrapping. Docs crops are cut into the copy first because a replacement resets them;
/// pictures with other Docs effects (brightness, contrast, transparency, rotation) are left alone.
/// </summary>
public sealed class GoogleDocShrinker
{
    private readonly GoogleApi _api;

    public GoogleDocShrinker(GoogleApi api) => _api = api;

    /// <summary>Pictures downloaded or uploaded at the same time.</summary>
    public int Parallel { get; set; } = 4;

    /// <summary>Pictures replaced per Docs request.</summary>
    public int BatchSize { get; set; } = 10;

    public async Task<GoogleDocPlan> CheckAsync(string docId, Preset preset, ShrinkOptions options, string? originalsRoot,
        IProgress<GoogleProgress>? progress = null, CancellationToken ct = default)
    {
        var doc = await _api.GetDocumentAsync(docId, ct).ConfigureAwait(false);
        var jobs = doc.Pictures.Select((p, i) => new GooglePictureJob(p, i + 1)).ToList();
        string work = Path.Combine(Path.GetTempPath(), "nacho-gdoc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string? originals = originalsRoot is null ? null : OriginalsFolderFor(originalsRoot, doc.Title, DateTime.Now);
        var plan = new GoogleDocPlan(doc, jobs, work, originals);

        foreach (var j in jobs.Where(j => j.Picture.Skip != GoogleSkip.None)) j.State = GooglePictureState.Skipped;
        var todo = jobs.Where(j => j.State == GooglePictureState.Pending).ToList();
        int done = 0;
        progress?.Report(new GoogleProgress(0, todo.Count, null, doc));
        var pictureOptions = new ShrinkOptions
        {
            Format = options.Format,
            RemoveLocation = options.RemoveLocation,
            RemoveCamera = options.RemoveCamera,
            OutputFolder = Path.Combine(work, "new"),
            Suffix = "",
        };
        try
        {
            await System.Threading.Tasks.Parallel.ForEachAsync(todo, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Parallel), CancellationToken = ct },
                async (job, token) =>
                {
                    await PrepareAsync(job, preset, pictureOptions, work, originals, token).ConfigureAwait(false);
                    progress?.Report(new GoogleProgress(Interlocked.Increment(ref done), todo.Count, job, doc));
                }).ConfigureAwait(false);
        }
        catch
        {
            // Cancelled, disk full or signed out: nothing half-made is left behind.
            plan.Dispose();
            throw;
        }
        return plan;
    }

    /// <summary>NACHO stops the check when the disk has less than this free (a picture from Docs can be 7 MB).</summary>
    public const long MinFreeBytes = 300L * 1024 * 1024;

    /// <summary>Free bytes on the disk that holds a folder, or null when it can't be told; tests replace it.</summary>
    public static Func<string, long?> FreeSpace { get; set; } = folder =>
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(folder));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    };

    private static void EnsureSpace(string folder)
    {
        if (FreeSpace(folder) is long free && free < MinFreeBytes)
        {
            throw new GoogleException(GoogleError.DiskFull, $"Only {free / 1048576} MB free on the disk of {folder}.");
        }
    }

    /// <summary>Windows ERROR_DISK_FULL / ERROR_HANDLE_DISK_FULL, Linux ENOSPC.</summary>
    private static bool IsDiskFull(IOException ex) =>
        (ex.HResult & 0xFFFF) is 0x70 or 0x27
        || ex.Message.Contains("No space left", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("not enough space", StringComparison.OrdinalIgnoreCase);

    private async Task PrepareAsync(GooglePictureJob job, Preset preset, ShrinkOptions opt, string work, string? originals, CancellationToken ct)
    {
        string? temp = null, shown = null;
        try
        {
            EnsureSpace(work);
            if (originals is not null) EnsureSpace(originals);
            byte[] data = await _api.DownloadAsync(job.Picture.ContentUri!, ct).ConfigureAwait(false);
            job.OriginalBytes = data.LongLength;
            string ext = Extension(data);
            string name = job.Index.ToString("000", CultureInfo.InvariantCulture);
            string source;
            if (originals is not null)
            {
                // The kept original is also what NACHO reads, so the picture is on disk only once.
                Directory.CreateDirectory(originals);
                job.OriginalPath = Path.Combine(originals, $"picture {name}{ext}");
                await File.WriteAllBytesAsync(job.OriginalPath, data, ct).ConfigureAwait(false);
                source = job.OriginalPath;
            }
            else
            {
                temp = Path.Combine(work, name + ext);
                await File.WriteAllBytesAsync(temp, data, ct).ConfigureAwait(false);
                source = temp;
            }

            var info = new MagickImageInfo(data);
            job.OriginalWidth = (int)info.Width;
            job.OriginalHeight = (int)info.Height;
            if (!job.Picture.Crop.IsNone)
            {
                // The replacement resets the Docs crop, so the copy carries only the part the Doc shows.
                shown = Path.Combine(work, name + "-shown.png");
                CropTo(data, job.Picture.Crop, shown);
                source = shown;
            }

            var r = new ImageShrinker().Shrink(source, preset, opt);
            if (r.Kind == ResultKind.Shrunk && r.OutputBytes < job.OriginalBytes && r.OutputFormat is "JPEG" or "PNG" or "GIF")
            {
                job.State = GooglePictureState.WillShrink;
                job.NewPath = r.OutputPath;
                job.NewBytes = r.OutputBytes;
                job.NewWidth = r.OutputWidth;
                job.NewHeight = r.OutputHeight;
                job.NewFormat = r.OutputFormat;
            }
            else if (r.Kind == ResultKind.Error)
            {
                job.State = GooglePictureState.Failed;
                job.Error = r.ErrorDetail ?? r.Error.ToString();
            }
            else
            {
                job.State = GooglePictureState.Kept;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GoogleException ex) when (ex.Kind is GoogleError.DiskFull or GoogleError.SignInNeeded)
        {
            // Every other picture would fail the same way: stop the whole check with one clear message.
            throw;
        }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            throw new GoogleException(GoogleError.DiskFull, ex.Message, 0, ex);
        }
        catch (Exception ex) when (ex is GoogleException or IOException or MagickException or UnauthorizedAccessException)
        {
            job.State = GooglePictureState.Failed;
            job.Error = ex.Message;
        }
        finally
        {
            foreach (string? f in new[] { temp, shown })
            {
                try
                {
                    if (f is not null && File.Exists(f)) File.Delete(f);
                }
                catch (IOException)
                {
                    // In the work folder, which goes with the plan anyway.
                }
            }
        }
    }

    /// <summary>Cuts the picture to the part a Docs crop shows (offsets are fractions of each side).</summary>
    public static void CropTo(byte[] data, GoogleCrop crop, string path)
    {
        crop = crop.Clean();
        using var img = new MagickImage(data);
        img.AutoOrient();
        int w = (int)img.Width, h = (int)img.Height;
        int x = (int)Math.Round(crop.Left * w), y = (int)Math.Round(crop.Top * h);
        int cw = Math.Max(1, (int)Math.Round(w * (1 - crop.Left - crop.Right)));
        int ch = Math.Max(1, (int)Math.Round(h * (1 - crop.Top - crop.Bottom)));
        cw = Math.Min(cw, w - x);
        ch = Math.Min(ch, h - y);
        img.Crop(new MagickGeometry(x, y, (uint)cw, (uint)ch));
        img.ResetPage();
        img.Format = MagickFormat.Png;
        img.Write(path);
    }

    /// <summary>
    /// Makes a copy of the checked Doc and points every picture at the same picture in the copy, so
    /// <see cref="ApplyAsync"/> changes the copy and only reads the original. Google keeps the tab and picture IDs
    /// in a copy (tested: 251 of 251 on a real book); if they ever differ, pictures are matched by order and size,
    /// and a picture that can't be matched is left alone (Changed).
    /// </summary>
    public async Task CopyAsync(GoogleDocPlan plan, string name, CancellationToken ct = default)
    {
        if (plan.CopyId is not null) return;
        var (id, copyName) = await _api.CopyDocAsync(plan.Doc.Id, name, ct).ConfigureAwait(false);
        plan.CopyId = id;
        plan.CopyName = copyName;
        var copy = await _api.GetDocumentAsync(id, CancellationToken.None).ConfigureAwait(false);
        MapToCopy(plan, copy);
    }

    internal static void MapToCopy(GoogleDocPlan plan, GoogleDocInfo copy)
    {
        var byId = copy.Pictures.GroupBy(p => (p.TabId, p.ObjectId)).ToDictionary(g => g.Key, g => g.First());
        bool sameShape = copy.Pictures.Count == plan.Doc.Pictures.Count;
        foreach (var job in plan.Jobs)
        {
            var p = job.Picture;
            if (byId.ContainsKey((p.TabId, p.ObjectId))) continue; // same IDs: nothing to change
            var other = sameShape ? copy.Pictures[job.Index - 1] : null;
            if (other is not null && other.Positioned == p.Positioned
                && Math.Abs(other.WidthPt - p.WidthPt) < 0.5 && Math.Abs(other.HeightPt - p.HeightPt) < 0.5)
            {
                job.TargetObjectId = other.ObjectId;
                job.TargetTabId = other.TabId;
            }
            else if (job.State == GooglePictureState.WillShrink)
            {
                job.State = GooglePictureState.Changed;
                job.Error = "Not found in the copy.";
            }
        }
    }

    /// <summary>Replaces every picture the check made a smaller copy of, in the Doc or in NACHO's copy of it.
    /// Cancelling stops after the current group.</summary>
    public async Task ApplyAsync(GoogleDocPlan plan, IProgress<GoogleProgress>? progress = null, CancellationToken ct = default)
    {
        await CleanLeftoversAsync(ct).ConfigureAwait(false);
        var todo = plan.Jobs.Where(j => j.State == GooglePictureState.WillShrink).ToList();
        int done = 0;
        progress?.Report(new GoogleProgress(0, todo.Count));
        foreach (var group in todo.Chunk(Math.Max(1, BatchSize)))
        {
            if (ct.IsCancellationRequested)
            {
                plan.Cancelled = true;
                break;
            }
            await ReplaceGroupAsync(plan.TargetDocId, group, ct).ConfigureAwait(false);
            foreach (var job in group) progress?.Report(new GoogleProgress(++done, todo.Count, job));
        }
    }

    private async Task ReplaceGroupAsync(string docId, GooglePictureJob[] group, CancellationToken ct)
    {
        var uploaded = new Dictionary<GooglePictureJob, string>();
        try
        {
            // The group is finished even when the user cancels, so no temporary file is left behind shared.
            var finish = CancellationToken.None;
            await System.Threading.Tasks.Parallel.ForEachAsync(group, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Parallel) },
                async (job, _) =>
                {
                    try
                    {
                        byte[] data = await File.ReadAllBytesAsync(job.NewPath!, finish).ConfigureAwait(false);
                        string id = await _api.UploadTempAsync(data, job.Mime, finish).ConfigureAwait(false);
                        lock (uploaded) uploaded[job] = id;
                        await _api.ShareAsync(id, finish).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is GoogleException or IOException)
                    {
                        // A file that was uploaded but not shared is still deleted below.
                        job.State = GooglePictureState.Failed;
                        job.Error = ex.Message;
                    }
                }).ConfigureAwait(false);

            var ready = group.Where(j => j.State == GooglePictureState.WillShrink && uploaded.ContainsKey(j)).ToList();
            if (ready.Count == 0) return;
            try
            {
                await _api.ReplaceAsync(docId, ready.Select(j => (j.TargetObjectId, j.TargetTabId, GoogleApi.TempUrl(uploaded[j]))), finish).ConfigureAwait(false);
                foreach (var j in ready) j.State = GooglePictureState.Replaced;
            }
            catch (GoogleException ex) when (ex.Status == 400 && ready.Count > 1)
            {
                // One picture in the group was changed or removed since the check: do them one by one.
                foreach (var j in ready) await ReplaceOneAsync(docId, j, uploaded[j], finish).ConfigureAwait(false);
            }
            catch (GoogleException ex) when (ex.Status == 400)
            {
                ready[0].State = GooglePictureState.Changed;
                ready[0].Error = ex.Message;
            }
        }
        finally
        {
            foreach (var id in uploaded.Values)
            {
                try
                {
                    await _api.DeleteAsync(id, CancellationToken.None).ConfigureAwait(false);
                }
                catch (GoogleException)
                {
                    // Removed at the start of the next run (CleanLeftoversAsync).
                }
            }
        }
    }

    private async Task ReplaceOneAsync(string docId, GooglePictureJob job, string fileId, CancellationToken ct)
    {
        try
        {
            await _api.ReplaceAsync(docId, new[] { (job.TargetObjectId, job.TargetTabId, GoogleApi.TempUrl(fileId)) }, ct).ConfigureAwait(false);
            job.State = GooglePictureState.Replaced;
        }
        catch (GoogleException ex) when (ex.Status == 400)
        {
            job.State = GooglePictureState.Changed;
            job.Error = ex.Message;
        }
    }

    private async Task CleanLeftoversAsync(CancellationToken ct)
    {
        try
        {
            foreach (string id in await _api.ListTempFilesAsync(ct).ConfigureAwait(false))
            {
                await _api.DeleteAsync(id, ct).ConfigureAwait(false);
            }
        }
        catch (GoogleException)
        {
            // Not worth stopping the real work for.
        }
    }

    /// <summary>"Docs-Ready\Google Docs originals\{title} {date time}", with characters Windows forbids replaced.</summary>
    public static string OriginalsFolderFor(string root, string title, DateTime when)
    {
        string safe = new string((string.IsNullOrWhiteSpace(title) ? "Google Doc" : title.Trim())
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' ? '_' : c).ToArray());
        if (safe.Length > 80) safe = safe[..80].TrimEnd();
        safe = safe.TrimEnd('.', ' ');
        return Path.Combine(root, "Google Docs originals", $"{safe} {when.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture)}");
    }

    private static string Extension(byte[] d)
    {
        if (d.Length > 3 && d[0] == 0x89 && d[1] == 0x50) return ".png";
        if (d.Length > 2 && d[0] == 0xFF && d[1] == 0xD8) return ".jpg";
        if (d.Length > 3 && d[0] == (byte)'G' && d[1] == (byte)'I' && d[2] == (byte)'F') return ".gif";
        return ".img";
    }
}
