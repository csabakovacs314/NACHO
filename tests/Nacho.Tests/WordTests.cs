using System.Security.Cryptography;
using Nacho.Core;
using Nacho.Core.Google;
using Nacho.Core.Word;
using Xunit;

namespace Nacho.Tests;

/// <summary>The Word window's engine: check (reads only), a new copy beside the original, or the file itself with a
/// backup and Restore; the same for a Word file in Google Drive against the pretend Google.</summary>
public sealed class WordTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nacho-wtest-" + Guid.NewGuid().ToString("N"));
    private static readonly GoogleClientInfo Client = new("123-abc.apps.googleusercontent.com", "GOCSPX-test");
    private readonly WordShrinker _word = new(new ImageShrinker());
    private readonly ShrinkOptions _opt = new();
    private readonly Preset _preset = new("balanced", 1280, 300);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Doc(string name = "Team handbook.docx") => SampleImages.WordDocument(Path.Combine(_dir, "docs-" + Guid.NewGuid().ToString("N")[..6], name));

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private string Fallback => Path.Combine(_dir, "Docs-Ready");

    private string Backups => Path.Combine(_dir, "Docs-Ready", "Word originals");

    [Fact]
    public void The_check_reads_only_and_counts_the_pictures()
    {
        string doc = Doc();
        string before = Hash(doc);
        int calls = 0, lastTotal = 0;
        using var check = _word.CheckLocal(doc, _preset, _opt, (_, total) => { calls++; lastTotal = total; });
        Assert.True(check.CanShrink);
        Assert.Equal(before, Hash(doc));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(doc)!));
        Assert.True(check.BytesAfter < check.BytesBefore);
        Assert.True(check.Report.Shrunk >= 2);
        Assert.True(calls > 2 && lastTotal >= 4);
        Assert.Equal(WordWhere.ThisPc, check.Where);
        string work = check.WorkFolder;
        check.Dispose();
        Assert.False(Directory.Exists(work));
    }

    [Fact]
    public void A_new_copy_goes_beside_the_original_and_never_replaces_a_file()
    {
        string doc = Doc();
        string before = Hash(doc);
        using var check = _word.CheckLocal(doc, _preset, _opt);
        var a = _word.ApplyLocal(check, intoCopy: true, "{0} (smaller)", Fallback, Backups);
        var b = _word.ApplyLocal(check, intoCopy: true, "{0} (smaller)", Fallback, Backups);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(doc)!, "Team handbook (smaller).docx"), a.LocalPath);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(doc)!, "Team handbook (smaller) (2).docx"), b.LocalPath);
        Assert.False(a.WentToFallbackFolder);
        Assert.Null(a.BackupPath);
        Assert.Equal(before, Hash(doc));
        Assert.True(new FileInfo(a.LocalPath!).Length < new FileInfo(doc).Length);
    }

    [Fact]
    public void The_file_itself_is_changed_only_after_a_backup_and_Restore_puts_it_back()
    {
        string doc = Doc();
        string before = Hash(doc);
        long size = new FileInfo(doc).Length;
        using var check = _word.CheckLocal(doc, _preset, _opt);
        var o = _word.ApplyLocal(check, intoCopy: false, "{0} (smaller)", Fallback, Backups);
        Assert.Equal(doc, o.LocalPath);
        Assert.NotNull(o.BackupPath);
        Assert.StartsWith(Backups, o.BackupPath);
        Assert.Equal(before, Hash(o.BackupPath!));
        Assert.True(new FileInfo(doc).Length < size);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(doc)!)); // no temporary file left beside it
        using (var z = System.IO.Compression.ZipFile.OpenRead(doc)) Assert.NotNull(z.GetEntry("word/document.xml"));

        WordShrinker.Restore(o);
        Assert.Equal(before, Hash(doc));
    }

    [Fact]
    public void A_file_that_changed_after_the_check_is_not_overwritten()
    {
        string doc = Doc();
        using var check = _word.CheckLocal(doc, _preset, _opt);
        File.SetLastWriteTimeUtc(doc, DateTime.UtcNow.AddMinutes(1));
        var ex = Assert.Throws<WordException>(() => _word.ApplyLocal(check, intoCopy: false, "{0} (smaller)", Fallback, Backups));
        Assert.Equal(WordError.Changed, ex.Kind);
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public void A_file_open_in_another_program_is_not_overwritten()
    {
        string doc = Doc();
        string before = Hash(doc);
        using var check = _word.CheckLocal(doc, _preset, _opt);
        using (new FileStream(doc, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var ex = Assert.Throws<WordException>(() => _word.ApplyLocal(check, intoCopy: false, "{0} (smaller)", Fallback, Backups));
            Assert.Equal(WordError.InUse, ex.Kind);
        }
        Assert.Equal(before, Hash(doc));

        // LibreOffice's lock file counts too.
        string lockFile = Path.Combine(Path.GetDirectoryName(doc)!, ".~lock." + Path.GetFileName(doc) + "#");
        File.WriteAllText(lockFile, "lock");
        Assert.True(WordShrinker.IsInUse(doc));
        File.Delete(lockFile);
        Assert.False(WordShrinker.IsInUse(doc));
    }

    [Fact]
    public void A_password_protected_or_old_doc_file_gets_a_clear_error()
    {
        string old = Path.Combine(_dir, "old.doc");
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(old, new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0 });
        var ex = Assert.Throws<WordException>(() => _word.CheckLocal(old, _preset, _opt));
        Assert.Equal(WordError.Locked, ex.Kind);
    }

    // ---------- Google Drive ----------

    private (SampleGoogle Fake, GoogleApi Api) Drive(out byte[] original)
    {
        var fake = new SampleGoogle();
        original = File.ReadAllBytes(Doc());
        fake.WithWordFile(original);
        var store = new MemoryTokenStore();
        store.Save("rt-1");
        return (fake, new GoogleApi(new GoogleAuth(Client, store, fake)) { Delay = (_, _) => Task.CompletedTask });
    }

    [Fact]
    public async Task A_Drive_copy_goes_into_the_same_folder_and_the_original_stays()
    {
        var (fake, api) = Drive(out var original);
        using var check = await _word.CheckDriveAsync(api, SampleGoogle.PickerWordDefault, _preset, _opt);
        Assert.Equal(WordWhere.Drive, check.Where);
        Assert.Equal("Team handbook.docx", check.Name);
        Assert.True(check.CanShrink);
        var o = await _word.ApplyDriveAsync(api, check, intoCopy: true, "{0} (smaller)", Backups, keepOriginalOnPc: true);
        Assert.Equal("Team handbook (smaller).docx", o.DriveResult!.Name);
        var copy = fake.DriveFiles[o.DriveResult.Id];
        Assert.Equal("folder-reports", copy.Parent);
        Assert.True(copy.Data.Length < original.Length);
        Assert.Equal(original, fake.DriveFiles[SampleGoogle.PickerWordDefault].Data);
        Assert.Equal(1, fake.DriveFiles[SampleGoogle.PickerWordDefault].Version);
        Assert.False(o.WentToFallbackFolder);
    }

    [Fact]
    public async Task A_copy_of_a_file_in_someone_elses_folder_goes_to_My_Drive()
    {
        var (fake, api) = Drive(out _);
        fake.ParentWritable = false;
        using var check = await _word.CheckDriveAsync(api, SampleGoogle.PickerWordDefault, _preset, _opt);
        var o = await _word.ApplyDriveAsync(api, check, intoCopy: true, "{0} (smaller)", Backups, keepOriginalOnPc: false);
        Assert.True(o.WentToFallbackFolder);
        Assert.Null(fake.DriveFiles[o.DriveResult!.Id].Parent);
    }

    [Fact]
    public async Task The_Drive_file_itself_gets_a_new_version_and_a_copy_stays_on_this_PC()
    {
        var (fake, api) = Drive(out var original);
        using var check = await _word.CheckDriveAsync(api, SampleGoogle.PickerWordDefault, _preset, _opt);
        var o = await _word.ApplyDriveAsync(api, check, intoCopy: false, "{0} (smaller)", Backups, keepOriginalOnPc: true);
        var f = fake.DriveFiles[SampleGoogle.PickerWordDefault];
        Assert.Equal(2, f.Version);
        Assert.True(f.Data.Length < original.Length);
        Assert.Equal(SampleGoogle.PickerWordDefault, o.DriveResult!.Id);
        Assert.Equal(original, File.ReadAllBytes(o.BackupPath!));
        Assert.Single(fake.DriveFiles);
    }

    [Fact]
    public async Task A_Drive_file_that_changed_or_is_view_only_is_not_written()
    {
        var (fake, api) = Drive(out var original);
        using (var check = await _word.CheckDriveAsync(api, SampleGoogle.PickerWordDefault, _preset, _opt))
        {
            fake.DriveFiles[SampleGoogle.PickerWordDefault].Version = 5; // someone saved a new version meanwhile
            var ex = await Assert.ThrowsAsync<WordException>(() => _word.ApplyDriveAsync(api, check, intoCopy: false, "{0} (smaller)", Backups, true));
            Assert.Equal(WordError.Changed, ex.Kind);
        }
        fake.DriveFiles[SampleGoogle.PickerWordDefault].CanEdit = false;
        using (var check = await _word.CheckDriveAsync(api, SampleGoogle.PickerWordDefault, _preset, _opt))
        {
            var ex = await Assert.ThrowsAsync<WordException>(() => _word.ApplyDriveAsync(api, check, intoCopy: false, "{0} (smaller)", Backups, true));
            Assert.Equal(WordError.ReadOnly, ex.Kind);
            // A copy still works for a view-only file.
            var o = await _word.ApplyDriveAsync(api, check, intoCopy: true, "{0} (smaller)", Backups, true);
            Assert.NotNull(o.DriveResult);
        }
        Assert.Equal(original, fake.DriveFiles[SampleGoogle.PickerWordDefault].Data);
    }

    [Fact]
    public async Task A_Google_Doc_picked_by_mistake_is_not_treated_as_a_Word_file()
    {
        var (fake, api) = Drive(out _);
        fake.DriveFiles["gdoc"] = new SampleGoogle.DriveEntry { Name = "A Google Doc", Mime = GoogleAuth.DocMime };
        var ex = await Assert.ThrowsAsync<WordException>(() => _word.CheckDriveAsync(api, "gdoc", _preset, _opt));
        Assert.Equal(WordError.NotWord, ex.Kind);
    }

    [Fact]
    public void The_picker_asks_for_Word_files_when_the_Word_window_opens_it()
    {
        var auth = new GoogleAuth(Client, new MemoryTokenStore());
        string url = auth.AuthUrl("http://127.0.0.1:1/", "c", "s", pick: true, mimeTypes: GoogleAuth.WordMime);
        var q = SampleGoogle.Query(url);
        Assert.Equal(GoogleAuth.WordMime, q["mimetypes"]);
        Assert.Equal(GoogleAuth.Scopes, q["scope"]);
    }

    [Fact]
    public void Drive_names_are_made_safe_and_recent_files_are_kept_short()
    {
        Assert.Equal("a_b_c.docx", WordShrinker.SafeName("a/b:c.docx", ".docx"));
        Assert.Equal("Report.docx", WordShrinker.SafeName("Report", ".docx"));
        var list = new List<WordRecent>();
        for (int i = 0; i < 7; i++) list = WordRecent.Add(list, new WordRecent(WordWhere.ThisPc, $"f{i}.docx", $"C:\\f{i}.docx", DateTime.Now, 1, 10, 5, true));
        list = WordRecent.Add(list, new WordRecent(WordWhere.ThisPc, "f6.docx", "C:\\f6.docx", DateTime.Now, 2, 10, 5, false));
        Assert.Equal(WordRecent.Keep, list.Count);
        Assert.Equal(2, list[0].Replaced);
        Assert.Single(list, r => r.Target == "C:\\f6.docx");
    }
}
