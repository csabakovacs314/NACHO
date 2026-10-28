using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using ImageMagick;
using Nacho.Core;
using Nacho.Core.Google;
using Xunit;

namespace Nacho.Tests;

public sealed class GoogleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nacho-gtest-" + Guid.NewGuid().ToString("N"));
    private static readonly GoogleClientInfo Client = new("123-abc.apps.googleusercontent.com", "GOCSPX-test");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static (SampleGoogle Fake, GoogleApi Api, MemoryTokenStore Store) Signed()
    {
        var fake = new SampleGoogle();
        var store = new MemoryTokenStore();
        store.Save("rt-1");
        var auth = new GoogleAuth(Client, store, fake);
        var api = new GoogleApi(auth) { Delay = (_, _) => Task.CompletedTask };
        return (fake, api, store);
    }

    [Theory]
    [InlineData("https://docs.google.com/document/d/1Go0DSEPjSepxQ5O5w-R7YEh55xUJCXDhGRB16mPWo9Y/edit?tab=t.0", "1Go0DSEPjSepxQ5O5w-R7YEh55xUJCXDhGRB16mPWo9Y")]
    [InlineData(" https://docs.google.com/document/d/1Go0DSEPjSepxQ5O5w-R7YEh55xUJCXDhGRB16mPWo9Y/edit?usp=sharing ", "1Go0DSEPjSepxQ5O5w-R7YEh55xUJCXDhGRB16mPWo9Y")]
    [InlineData("https://docs.google.com/document/u/1/d/1Go0DSEPjSepxQ5O5w-R7YEh55xUJCXDhGRB16mPWo9Y", "1Go0DSEPjSepxQ5O5w-R7YEh55xUJCXDhGRB16mPWo9Y")]
    [InlineData("docs.google.com/document/d/1Go0DSEPjSepxQ5O5w-R7YEh55xUJCXDhGRB16mPWo9Y/", "1Go0DSEPjSepxQ5O5w-R7YEh55xUJCXDhGRB16mPWo9Y")]
    [InlineData("https://docs.google.com/spreadsheets/d/1Go0DSEPjSepxQ5O5w-R7YEh55xUJCXDhGRB16mPWo9Y/edit", null)]
    [InlineData("https://evil.example/document/d/1Go0DSEPjSepxQ5O5w-R7YEh55xUJCXDhGRB16mPWo9Y", null)]
    [InlineData("https://docs.google.com.evil.example/document/d/1Go0DSEPjSepxQ5O5w-R7YEh55xUJCXDhGRB16mPWo9Y", null)]
    [InlineData("C:\\Pictures\\cat.png", null)]
    [InlineData("", null)]
    public void Docs_links_are_recognised_and_nothing_else(string text, string? id)
    {
        Assert.Equal(id, GoogleDocLink.TryGetId(text));
        if (id is not null) Assert.Equal($"https://docs.google.com/document/d/{id}/edit", GoogleDocLink.EditUrl(id));
    }

    [Fact]
    public void Client_files_are_checked()
    {
        Assert.Equal(Client.ClientId, GoogleClientInfo.Parse("""{"installed":{"client_id":"123-abc.apps.googleusercontent.com","client_secret":"GOCSPX-test","redirect_uris":["http://localhost"]}}""")!.ClientId);
        Assert.Null(GoogleClientInfo.Parse("""{"web":{"client_id":"123-abc.apps.googleusercontent.com","client_secret":"x"}}"""));
        Assert.Null(GoogleClientInfo.Parse("""{"installed":{"client_id":"something-else","client_secret":"x"}}"""));
        Assert.Null(GoogleClientInfo.Parse("not json"));
        Assert.Null(GoogleClientInfo.FromFile(Path.Combine(_dir, "missing.json")));
    }

    [Fact]
    public void The_parser_finds_pictures_in_every_tab_and_flags_what_it_must_leave_alone()
    {
        var child = SampleGoogle.Tab("t.2", "Appendix", new JsonObject { ["kix.c1"] = SampleGoogle.Picture("c1", 200, 150) }, new JsonObject());
        var doc = new JsonObject
        {
            ["title"] = "Book",
            ["tabs"] = new JsonArray(SampleGoogle.Tab("t.0", "Main",
                new JsonObject
                {
                    ["kix.a"] = SampleGoogle.Picture("a", 154.2, 243),
                    ["kix.bright"] = SampleGoogle.Picture("b", 100, 100, new JsonObject { ["brightness"] = 0.2 }),
                    ["kix.draw"] = SampleGoogle.Picture("d", 100, 100, picture: false),
                },
                new JsonObject
                {
                    ["kix.crop"] = SampleGoogle.Picture("p", 216, 279, new JsonObject { ["cropProperties"] = new JsonObject { ["offsetLeft"] = 0.03, ["offsetRight"] = 0.03 } }),
                    ["kix.out"] = SampleGoogle.Picture("o", 216, 279, new JsonObject { ["cropProperties"] = new JsonObject { ["offsetLeft"] = -0.1 } }),
                    ["kix.rot"] = SampleGoogle.Picture("r", 216, 279, new JsonObject { ["angle"] = 1.57 }),
                    // Seen in a real Doc: a side Docs never cropped comes back as -3.5E-14.
                    ["kix.noise"] = SampleGoogle.Picture("n", 216, 279, new JsonObject { ["cropProperties"] = new JsonObject { ["offsetLeft"] = 0.028406005, ["offsetRight"] = 0.030430755, ["offsetTop"] = -3.5320183E-14 } }),
                },
                new JsonArray(child))),
        };
        var info = GoogleDocParser.Parse("id", doc);
        Assert.Equal("Book", info.Title);
        Assert.Equal(2, info.Tabs);
        Assert.Equal(8, info.Pictures.Count);
        var by = info.Pictures.ToDictionary(p => p.ObjectId);
        Assert.Equal(GoogleSkip.None, by["kix.a"].Skip);
        Assert.False(by["kix.a"].Positioned);
        Assert.Equal(154.2, by["kix.a"].WidthPt, 3);
        Assert.Equal(GoogleSkip.Effects, by["kix.bright"].Skip);
        Assert.Equal(GoogleSkip.NotPicture, by["kix.draw"].Skip);
        Assert.Equal(GoogleSkip.None, by["kix.crop"].Skip);
        Assert.True(by["kix.crop"].Positioned);
        Assert.Equal(0.03, by["kix.crop"].Crop.Left, 6);
        Assert.Equal(GoogleSkip.Effects, by["kix.out"].Skip);
        Assert.Equal(GoogleSkip.Effects, by["kix.rot"].Skip);
        Assert.Equal(GoogleSkip.None, by["kix.noise"].Skip);
        Assert.Equal(0, by["kix.noise"].Crop.Clean().Top);
        Assert.Equal("t.2", by["kix.c1"].TabId);
        Assert.Equal("Appendix", by["kix.c1"].TabTitle);
    }

    private static SampleGoogle Book(SampleGoogle fake)
    {
        fake.Pictures["big"] = SampleGoogle.Png(1300, 2048);
        fake.Pictures["crop"] = SampleGoogle.Png(1692, 2048);
        fake.Pictures["small"] = Jpeg(400, 300);
        fake.Pictures["fx"] = SampleGoogle.Png(1300, 2048);
        fake.Doc = new JsonObject
        {
            ["title"] = "Plantas: libro/2026",
            ["tabs"] = new JsonArray(SampleGoogle.Tab("t.0", "Main",
                new JsonObject
                {
                    ["kix.big"] = SampleGoogle.Picture("big", 154.2, 243.0),
                    ["kix.small"] = SampleGoogle.Picture("small", 200, 150),
                    ["kix.fx"] = SampleGoogle.Picture("fx", 154.2, 243.0, new JsonObject { ["contrast"] = 0.3 }),
                },
                new JsonObject
                {
                    ["kix.crop"] = SampleGoogle.Picture("crop", 215.6, 278.6, new JsonObject { ["cropProperties"] = new JsonObject { ["offsetLeft"] = 0.031348553, ["offsetRight"] = 0.030430755, ["offsetTop"] = -3.5320183E-14 } }),
                })),
        };
        return fake;
    }

    private static byte[] Jpeg(int w, int h)
    {
        using var img = new MagickImage("plasma:", new MagickReadSettings { Width = (uint)w, Height = (uint)h });
        img.Format = MagickFormat.Jpeg;
        img.Quality = 80;
        return img.ToByteArray();
    }

    [Fact]
    public async Task Check_changes_nothing_and_apply_replaces_in_place_and_leaves_no_temporary_file()
    {
        var (fake, api, _) = Signed();
        Book(fake);
        var shrinker = new GoogleDocShrinker(api) { BatchSize = 10 };
        var progress = new List<GoogleProgress>();
        using var plan = await shrinker.CheckAsync("docid", Preset.Defaults()[1], new ShrinkOptions(), _dir, new SyncProgress(progress));

        // The check only reads: nothing uploaded, nothing replaced.
        Assert.Empty(fake.Files);
        Assert.Empty(fake.Replaced);
        Assert.DoesNotContain(fake.Log, l => l.StartsWith("POST", StringComparison.Ordinal) && !l.Contains("oauth2"));
        Assert.Equal(3, progress.Last().Total);

        var by = plan.Jobs.ToDictionary(j => j.Picture.ObjectId);
        Assert.Equal(GooglePictureState.WillShrink, by["kix.big"].State);
        Assert.Equal(GooglePictureState.WillShrink, by["kix.crop"].State);
        Assert.Equal(GooglePictureState.Kept, by["kix.small"].State);
        Assert.Equal(GooglePictureState.Skipped, by["kix.fx"].State);
        Assert.Equal(2, plan.ToReplace);
        Assert.True(plan.BytesAfter < plan.BytesBefore / 3);

        // The copy of a cropped picture holds only the part the Doc shows, so the frame keeps its contents.
        var c = by["kix.crop"];
        double shown = 215.6 / 278.6, copy = (double)c.NewWidth / c.NewHeight;
        Assert.InRange(Math.Abs(copy - shown) / shown, 0, 0.003);
        Assert.Equal(1280, Math.Max(by["kix.big"].NewWidth, by["kix.big"].NewHeight));

        // Originals are saved, named in Doc order, in a folder named after the Doc (slash made safe).
        Assert.NotNull(plan.OriginalsFolder);
        Assert.Contains("Plantas_ libro_2026", plan.OriginalsFolder);
        Assert.Equal(3, Directory.GetFiles(plan.OriginalsFolder!).Length);

        await shrinker.ApplyAsync(plan);
        Assert.Equal(new[] { "kix.big", "kix.crop" }, fake.Replaced.OrderBy(x => x));
        Assert.Equal(GooglePictureState.Replaced, by["kix.big"].State);
        Assert.Equal(GooglePictureState.Replaced, by["kix.crop"].State);
        Assert.Empty(fake.Files);
        Assert.All(fake.Log.Where(l => l.Contains("upload")), l => Assert.StartsWith("POST " + GoogleApi.UploadBase, l));

        // The work folder goes away with the plan.
        plan.Dispose();
        Assert.False(Directory.Exists(plan.WorkFolder));
    }

    [Fact]
    public async Task In_copy_mode_the_original_is_only_read_and_the_copy_gets_the_smaller_pictures()
    {
        var (fake, api, _) = Signed();
        Book(fake);
        var shrinker = new GoogleDocShrinker(api);
        using var plan = await shrinker.CheckAsync("docid", Preset.Defaults()[1], new ShrinkOptions(), null);
        Assert.Equal("docid", plan.TargetDocId);

        await shrinker.CopyAsync(plan, GoogleText.CopyName("{0} (smaller)", "  " + plan.Doc.Title + " "));
        var copy = Assert.Single(fake.Copies);
        Assert.Equal("docid", copy.Source);
        Assert.Equal("Plantas: libro/2026 (smaller)", copy.Name);
        Assert.Equal(copy.Id, plan.CopyId);
        Assert.Equal(copy.Id, plan.TargetDocId);
        Assert.Equal(copy.Name, plan.CopyName);

        await shrinker.ApplyAsync(plan);
        Assert.Equal(new[] { "kix.big", "kix.crop" }, fake.Replaced.OrderBy(x => x));
        Assert.All(fake.ReplacedIn, d => Assert.Equal(copy.Id, d));
        Assert.DoesNotContain(fake.Log, l => l.Contains("/documents/docid:batchUpdate", StringComparison.Ordinal));
        Assert.Equal(2, plan.Count(GooglePictureState.Replaced));
        Assert.Empty(fake.Files);

        // A second copy is never made for the same plan.
        await shrinker.CopyAsync(plan, "again");
        Assert.Single(fake.Copies);
    }

    [Fact]
    public async Task A_copy_with_other_picture_IDs_is_matched_by_order_and_size()
    {
        var (fake, api, _) = Signed();
        Book(fake);
        fake.CopyChangesIds = true;
        var shrinker = new GoogleDocShrinker(api);
        using var plan = await shrinker.CheckAsync("docid", Preset.Defaults()[1], new ShrinkOptions(), null);
        await shrinker.CopyAsync(plan, "Copy");
        await shrinker.ApplyAsync(plan);
        Assert.Equal(new[] { "kixc.big", "kixc.crop" }, fake.Replaced.OrderBy(x => x));
        Assert.All(fake.ReplacedIn, d => Assert.Equal(plan.CopyId, d));
        Assert.Equal(2, plan.Count(GooglePictureState.Replaced));
    }

    [Fact]
    public async Task A_picture_missing_from_the_copy_is_left_alone()
    {
        var (fake, api, _) = Signed();
        Book(fake);
        fake.CopyChangesIds = true;
        var shrinker = new GoogleDocShrinker(api);
        using var plan = await shrinker.CheckAsync("docid", Preset.Defaults()[1], new ShrinkOptions(), null);
        // The Doc loses a picture between the check and the copy, so the copy has one picture fewer.
        ((JsonObject)fake.Doc["tabs"]![0]!["documentTab"]!["inlineObjects"]!).Remove("kix.small");
        await shrinker.CopyAsync(plan, "Copy");
        await shrinker.ApplyAsync(plan);
        Assert.Equal(0, plan.Count(GooglePictureState.Replaced));
        Assert.Equal(2, plan.Count(GooglePictureState.Changed));
        Assert.Empty(fake.Replaced);
        Assert.Empty(fake.Files);
    }

    [Theory]
    [InlineData("storageQuotaExceeded", GoogleError.DriveFull, "Gd.ErrDriveFull")]
    [InlineData("cannotCopyFile", GoogleError.CopyRefused, "Gd.ErrCopyRefused")]
    public async Task When_Google_can_not_make_the_copy_nothing_changes(string reason, GoogleError kind, string key)
    {
        var (fake, api, _) = Signed();
        Book(fake);
        fake.CopyFails = $$$"""{"error":{"code":403,"message":"No.","errors":[{"reason":"{{{reason}}}"}]}}""";
        var shrinker = new GoogleDocShrinker(api);
        using var plan = await shrinker.CheckAsync("docid", Preset.Defaults()[1], new ShrinkOptions(), null);
        var e = await Assert.ThrowsAsync<GoogleException>(() => shrinker.CopyAsync(plan, "Copy"));
        Assert.Equal(kind, e.Kind);
        Assert.Equal(key, GoogleText.ErrorKey(e.Kind));
        Assert.Null(plan.CopyId);
        Assert.Empty(fake.Replaced);
        Assert.Empty(fake.Files);
    }

    [Fact]
    public async Task A_picture_removed_after_the_check_is_reported_and_the_others_still_replaced()
    {
        var (fake, api, _) = Signed();
        Book(fake);
        var shrinker = new GoogleDocShrinker(api);
        using var plan = await shrinker.CheckAsync("docid", Preset.Defaults()[1], new ShrinkOptions(), null);
        Assert.Null(plan.OriginalsFolder);

        // Someone deletes a picture while NACHO waits for the user's OK.
        var tab = fake.Doc["tabs"]![0]!["documentTab"]!;
        ((JsonObject)tab["inlineObjects"]!).Remove("kix.big");

        await shrinker.ApplyAsync(plan);
        var by = plan.Jobs.ToDictionary(j => j.Picture.ObjectId);
        Assert.Equal(GooglePictureState.Changed, by["kix.big"].State);
        Assert.Equal(GooglePictureState.Replaced, by["kix.crop"].State);
        Assert.Equal(new[] { "kix.crop" }, fake.Replaced);
        Assert.Empty(fake.Files);
    }

    [Fact]
    public async Task Cancelling_stops_between_groups_and_cleans_up()
    {
        var (fake, api, _) = Signed();
        Book(fake);
        var shrinker = new GoogleDocShrinker(api) { BatchSize = 1 };
        using var plan = await shrinker.CheckAsync("docid", Preset.Defaults()[1], new ShrinkOptions(), null);
        using var cts = new CancellationTokenSource();
        await shrinker.ApplyAsync(plan, new SyncProgress(p => { if (p.Done == 1) cts.Cancel(); }), cts.Token);
        Assert.True(plan.Cancelled);
        Assert.Single(fake.Replaced);
        Assert.Equal(1, plan.Count(GooglePictureState.Replaced));
        Assert.Equal(1, plan.Count(GooglePictureState.WillShrink));
        Assert.Empty(fake.Files);
    }

    [Fact]
    public async Task A_full_disk_stops_the_check_with_one_clear_message()
    {
        var (fake, api, _) = Signed();
        Book(fake);
        var shrinker = new GoogleDocShrinker(api);
        var before = GoogleDocShrinker.FreeSpace;
        try
        {
            GoogleDocShrinker.FreeSpace = _ => 10L * 1024 * 1024;
            var e = await Assert.ThrowsAsync<GoogleException>(() => shrinker.CheckAsync("docid", Preset.Defaults()[1], new ShrinkOptions(), _dir));
            Assert.Equal(GoogleError.DiskFull, e.Kind);
            Assert.Equal("Gd.ErrDiskFull", GoogleText.ErrorKey(e.Kind));
        }
        finally
        {
            GoogleDocShrinker.FreeSpace = before;
        }
        Assert.Empty(fake.Files);
    }

    [Fact]
    public async Task Each_original_is_on_disk_once_and_the_work_folder_holds_only_the_copies()
    {
        var (fake, api, _) = Signed();
        Book(fake);
        var shrinker = new GoogleDocShrinker(api);
        using var plan = await shrinker.CheckAsync("docid", Preset.Defaults()[1], new ShrinkOptions(), _dir);
        var work = Directory.GetFiles(plan.WorkFolder, "*", SearchOption.AllDirectories);
        Assert.Equal(plan.ToReplace, work.Length);
        Assert.All(work, f => Assert.Contains(Path.DirectorySeparatorChar + "new" + Path.DirectorySeparatorChar, f));
        Assert.True(work.Sum(f => new FileInfo(f).Length) < plan.BytesBefore / 4);
    }

    [Fact]
    public async Task Leftover_temporary_files_from_an_earlier_run_are_removed_first()
    {
        var (fake, api, _) = Signed();
        Book(fake);
        fake.Files["old1"] = (new byte[] { 1 }, true, GoogleApi.TempPrefix + "abc.jpg");
        var shrinker = new GoogleDocShrinker(api);
        using var plan = await shrinker.CheckAsync("docid", Preset.Defaults()[1], new ShrinkOptions(), null);
        await shrinker.ApplyAsync(plan);
        Assert.Empty(fake.Files);
    }

    [Fact]
    public async Task Busy_answers_are_retried_and_an_expired_access_token_is_renewed()
    {
        var (fake, api, _) = Signed();
        Book(fake);
        fake.Fail429OnDocRead = 2;
        fake.Fail401Once = true;
        var info = await api.GetDocumentAsync("docid");
        Assert.Equal(4, info.Pictures.Count);
        Assert.Equal(2, fake.TokenForms.Count(f => f.Contains("refresh_token")));
        Assert.Contains(fake.Log, l => l.Contains("fields=") && l.Contains("includeTabsContent=true"));
    }

    [Fact]
    public async Task Errors_say_what_happened()
    {
        var (fake, api, store) = Signed();
        Book(fake);
        var e1 = await Assert.ThrowsAsync<GoogleException>(() => api.GetDocumentAsync("missing"));
        Assert.Equal(GoogleError.NotFound, e1.Kind);

        fake.ReadOnly = true;
        var e2 = await Assert.ThrowsAsync<GoogleException>(() => api.ReplaceAsync("docid", new[] { ("kix.big", "t.0", GoogleApi.TempUrl("x")) }));
        Assert.Equal(GoogleError.ReadOnly, e2.Kind);

        fake.Fail429OnDocRead = 99;
        api.MaxAttempts = 3;
        var e3 = await Assert.ThrowsAsync<GoogleException>(() => api.GetDocumentAsync("docid"));
        Assert.Equal(GoogleError.Quota, e3.Kind);

        // A sign-in Google no longer accepts is forgotten, so the app asks to sign in again.
        var (fake2, api2, store2) = Signed();
        fake2.RefreshRejected = true;
        var e4 = await Assert.ThrowsAsync<GoogleException>(() => api2.GetDocumentAsync("docid"));
        Assert.Equal(GoogleError.SignInNeeded, e4.Kind);
        Assert.Null(store2.Load());
        Assert.NotNull(store.Load());
    }

    [Fact]
    public async Task Pictures_are_only_fetched_from_Google()
    {
        var (_, api, _) = Signed();
        var e = await Assert.ThrowsAsync<GoogleException>(() => api.DownloadAsync("https://evil.example/x.png"));
        Assert.Contains("Unexpected", e.Message);
        await Assert.ThrowsAsync<GoogleException>(() => api.DownloadAsync("http://lh7-rt.googleusercontent.com/x"));
        await Assert.ThrowsAsync<GoogleException>(() => api.DownloadAsync("https://docs.google.com.evil.example/docs-images-rt/x"));
        await Assert.ThrowsAsync<GoogleException>(() => api.DownloadAsync("https://docs.google.com:8443/docs-images-rt/x"));
        Assert.True(GoogleApi.IsPictureAddress("https://docs.google.com/docs-images-rt/AD_4nX"));
        Assert.True(GoogleApi.IsPictureAddress("https://lh7-rt.googleusercontent.com/docsz/AD_4nX"));
    }

    [Fact]
    public async Task Sign_in_uses_the_browser_a_one_time_local_address_and_PKCE()
    {
        var fake = new SampleGoogle();
        var store = new MemoryTokenStore();
        var auth = new GoogleAuth(Client, store, fake);
        string? opened = null;
        auth.OpenBrowser = url =>
        {
            opened = url;
            var q = HttpUtility.ParseQueryString(new Uri(url).Query);
            string redirect = q["redirect_uri"]!;
            _ = Task.Run(async () =>
            {
                using var http = new HttpClient();
                string page = await http.GetStringAsync($"{redirect}?code=c-1&state={Uri.EscapeDataString(q["state"]!)}&scope=x");
                Assert.Contains("NACHO", page);
            });
            return true;
        };
        var picked = await auth.SignInAsync(TimeSpan.FromSeconds(20));
        Assert.Empty(picked);
        Assert.Equal("rt-1", store.Load());

        var query = HttpUtility.ParseQueryString(new Uri(opened!).Query);
        Assert.StartsWith(GoogleAuth.AuthEndpoint + "?", opened);
        Assert.StartsWith("http://127.0.0.1:", query["redirect_uri"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("https://www.googleapis.com/auth/drive.file", query["scope"]);
        Assert.Equal(GoogleAuth.Scopes, query["scope"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Null(query["trigger_onepick"]);

        // The code goes back with the verifier that matches the challenge in the browser address.
        var form = HttpUtility.ParseQueryString(fake.TokenForms.Single(f => f.Contains("authorization_code")));
        Assert.Equal(query["code_challenge"], GoogleAuth.Challenge(form["code_verifier"]!));
        Assert.Equal("c-1", form["code"]);
        Assert.Equal(query["redirect_uri"], form["redirect_uri"]);

        await auth.SignOutAsync();
        Assert.Null(store.Load());
        Assert.Contains(fake.Log, l => l.StartsWith("POST " + GoogleAuth.RevokeEndpoint, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_picker_returns_the_chosen_Doc_and_only_that_Doc_opens()
    {
        var fake = new SampleGoogle().WithBook();
        fake.Picked = new HashSet<string>();
        var session = new GoogleSession(new MemoryTokenStore(), fake)
        {
            Client = Client,
            Delay = (_, _) => Task.CompletedTask,
        };
        string? opened = null;
        session.OpenBrowser = url =>
        {
            opened = url;
            return SampleGoogle.AnswerSignIn(url);
        };
        const string docId = "1Go0DSEPjSepxQ5O5w-R7YEh55xUJCXDhGRB16mPWo9Y";

        // Signed in, but the Doc was never chosen: Google answers "not found", which means "let the user pick it".
        await session.SignInAsync();
        var e = await Assert.ThrowsAsync<GoogleException>(() => session.Api().GetDocumentAsync(docId));
        Assert.True(GoogleText.NeedsPick(e.Kind));

        // A pasted link narrows the picker to that one Doc.
        var (picked, email) = await session.PickAsync(docId);
        Assert.Equal(docId, picked);
        Assert.Equal("tester@example.com", email);
        var q = HttpUtility.ParseQueryString(new Uri(opened!).Query);
        Assert.Equal("true", q["trigger_onepick"]);
        Assert.Equal(GoogleAuth.DocMime, q["mimetypes"]);
        Assert.Equal(docId, q["file_ids"]);
        Assert.Equal("consent", q["prompt"]);
        Assert.Equal("https://www.googleapis.com/auth/drive.file", q["scope"]);
        Assert.Equal(6, (await session.Api().GetDocumentAsync(docId)).Pictures.Count);

        // Without a link the picker shows every Doc.
        var (other, _) = await session.PickAsync(null);
        Assert.Equal(SampleGoogle.PickerDefault, other);
        Assert.Null(HttpUtility.ParseQueryString(new Uri(opened!).Query)["file_ids"]);
        Assert.False(GoogleText.NeedsPick(GoogleError.Network));
    }

    [Fact]
    public async Task Closing_the_picker_without_a_Doc_is_a_cancel()
    {
        var fake = new SampleGoogle();
        var store = new MemoryTokenStore();
        var auth = new GoogleAuth(Client, store, fake)
        {
            OpenBrowser = url =>
            {
                var q = HttpUtility.ParseQueryString(new Uri(url).Query);
                _ = Task.Run(async () =>
                {
                    using var http = new HttpClient();
                    try { await http.GetStringAsync($"{q["redirect_uri"]}?code=c-1&state={Uri.EscapeDataString(q["state"]!)}"); } catch (HttpRequestException) { }
                });
                return true;
            },
        };
        var e = await Assert.ThrowsAsync<GoogleException>(() => auth.SignInAsync(TimeSpan.FromSeconds(20), pick: true));
        Assert.Equal(GoogleError.Cancelled, e.Kind);
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task A_sign_in_answer_for_another_request_is_refused()
    {
        var fake = new SampleGoogle();
        var store = new MemoryTokenStore();
        var auth = new GoogleAuth(Client, store, fake)
        {
            OpenBrowser = url =>
            {
                string redirect = HttpUtility.ParseQueryString(new Uri(url).Query)["redirect_uri"]!;
                _ = Task.Run(async () =>
                {
                    using var http = new HttpClient();
                    try { await http.GetStringAsync($"{redirect}?code=c-1&state=forged"); } catch (HttpRequestException) { }
                });
                return true;
            },
        };
        var e = await Assert.ThrowsAsync<GoogleException>(() => auth.SignInAsync(TimeSpan.FromSeconds(20)));
        Assert.Equal(GoogleError.Refused, e.Kind);
        Assert.Null(store.Load());
        Assert.Empty(fake.TokenForms);
    }

    [Fact]
    public void The_sign_in_file_on_Linux_is_private()
    {
        string path = Path.Combine(_dir, "google-signin");
        var s = new FileTokenStore(path);
        s.Save("rt-secret");
        Assert.Equal("rt-secret", s.Load());
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        s.Clear();
        Assert.Null(s.Load());
    }

    [Fact]
    public void Originals_folder_names_are_safe()
    {
        var when = new DateTime(2026, 10, 27, 16, 5, 0);
        string f = GoogleDocShrinker.OriginalsFolderFor("/root", "A: b/c? <d>.", when);
        Assert.Equal(Path.Combine("/root", "Google Docs originals", "A_ b_c_ _d_ 2026-10-27 1605"), f);
        Assert.EndsWith("Google Doc 2026-10-27 1605", GoogleDocShrinker.OriginalsFolderFor("/root", " ", when));
    }

    private sealed class SyncProgress : IProgress<GoogleProgress>
    {
        private readonly Action<GoogleProgress> _a;
        public SyncProgress(List<GoogleProgress> list) => _a = p => { lock (list) list.Add(p); };
        public SyncProgress(Action<GoogleProgress> a) => _a = a;
        public void Report(GoogleProgress value) => _a(value);
    }
}
