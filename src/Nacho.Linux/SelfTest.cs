using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nacho.Core;
using Nacho.Core.Google;
using Nacho.Core.Linux;

namespace Nacho.Linux;

/// <summary>
/// nacho --selftest &lt;folder&gt;: builds sample images, runs them through the real window, checks the results,
/// the clipboard and the desktop files, and saves screenshots in three languages, light, dark and narrow.
/// Uses its own settings file, output folder and desktop folders; never touches the real ones.
/// </summary>
public static class SelfTest
{
    private static readonly string[] ThemeKeys =
    {
        "N.Text", "N.TextSecondary", "N.Window", "N.Card", "N.CardStroke", "N.Subtle", "N.StrongStroke",
        "N.Divider", "N.Layer", "N.Accent", "N.Success", "N.Critical", "N.Caution",
    };

    public static async Task<int> RunAsync(string outDir)
    {
        var log = new StringBuilder();
        int fails = 0;
        void Check(bool ok, string what)
        {
            log.AppendLine((ok ? "PASS  " : "FAIL  ") + what);
            if (!ok) fails++;
        }
        void Info(string what) => log.AppendLine("INFO  " + what);

        outDir = Path.GetFullPath(outDir);
        Directory.CreateDirectory(outDir);
        AppSettings.PathOverride = Path.Combine(outDir, "settings.json");
        if (File.Exists(AppSettings.PathOverride)) File.Delete(AppSettings.PathOverride);
        string desktopRoot = Path.Combine(outDir, "desktop");
        if (Directory.Exists(desktopRoot)) Directory.Delete(desktopRoot, recursive: true);
        Shell.TargetsOverride = new DesktopTargets(Path.Combine(desktopRoot, "data"), Path.Combine(desktopRoot, "config"));

        var settings = AppSettings.Load();
        Info($"exe: {Environment.ProcessPath}");
        Info($"menus point at: {Shell.ExecPath}");
        Info("built-in Google client: " + (GoogleClientInfo.BuiltIn is { } gci ? "present, id ends " + gci.ClientId[^30..] : "none"));
        Info($"display: {Environment.GetEnvironmentVariable("DISPLAY")}, session: {Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")}");
        settings.Language = "en";
        settings.PageSize = "A4";
        settings.OutputFolder = Path.Combine(outDir, "Docs-Ready");

        foreach (string mode in new[] { "Light", "Dark" })
        {
            App.ApplyTheme(mode);
            var variant = mode == "Dark" ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
            foreach (string key in ThemeKeys)
            {
                Check(Application.Current!.TryGetResource(key, variant, out var v) && v is not null, $"{mode} theme has {key}");
            }
        }
        App.ApplyTheme("Light");
        Loc.Instance.Set("en");

        var samples = await Task.Run(() => SampleImages.Generate(Path.Combine(outDir, "samples")));
        var w = new MainWindow(settings) { Width = 1240, Height = 800, WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(0, 0) };
        w.Show();
        await Settle();

        // 0.11.0: one design everywhere; an empty list shows the start screen with both ways in.
        Check(w.HomeVisible, "start screen shows while the list is empty");
        Shot(w, Path.Combine(outDir, "main-en-home.png"));
        App.ApplyTheme("Dark");
        await Settle();
        Shot(w, Path.Combine(outDir, "main-en-home-dark.png"));
        App.ApplyTheme("Light");
        w.Width = 420;
        await Settle();
        Shot(w, Path.Combine(outDir, "main-en-home-narrow.png"));
        w.Width = 1240;
        await Settle();

        await w.AddFilesAsync(samples.All);
        await w.WaitIdleAsync();
        await Settle();

        var byName = w.Items.ToDictionary(i => Path.GetFileName(i.SourcePath));
        ResultItem Get(string path) => byName[Path.GetFileName(path)];
        Check(w.Items.Count == 9, "9 tiles in the list");
        Check(!w.HomeVisible, "the results replace the start screen");
        Check(w.Items.All(i => !i.IsWorking), "every tile finished");
        Check(Get(samples.Photo).Result?.Kind == ResultKind.Shrunk && Get(samples.Photo).Result?.OutputFormat == "JPEG", "phone photo shrunk to JPEG");
        Check(Get(samples.Photo).Badges.Contains("Location removed"), "phone photo shows Location removed");
        Check(Get(samples.Screenshot).Result?.OutputFormat == "PNG", "screenshot stays PNG");
        Check(Get(samples.Logo).IsKept, "small logo kept as it was");
        Check(Get(samples.Alpha).Result?.OutputFormat == "PNG", "transparent image stays PNG");
        Check(Get(samples.Animated).Result?.OutputFormat == "GIF", "animated GIF stays GIF");
        Check(Get(samples.WebP).IsOk && Limits.IsDocsFormat(Get(samples.WebP).Result!.OutputFormat), "WebP converted");
        Check(Get(samples.Pdf).IsKept && Get(samples.Pdf).Result?.Document?.Format == "PDF", "text-only PDF read and kept as it was");
        Check(Get(samples.Broken).IsError, "broken file gets an error tile");
        Check(w.StatusMessage.Contains("shrunk", StringComparison.Ordinal), "status bar shows the summary: " + w.StatusMessage);
        Check(w.Items.Count(i => i.IsOk && File.Exists(i.Result!.OutputPath)) == 6, "6 new copies in Docs-Ready");
        Check(w.Items.Where(i => i.CanDeliver && !i.IsDocument).All(i => i.Thumbnail is not null), "every image copy has a thumbnail");

        Check(w.Items.Where(i => i.IsOk).All(i => i.Result!.ShapeErrorPercent <= 0.1), "every copy keeps the original shape");
        Check(Get(samples.Photo).Result!.ShapeExact, "phone photo shape exact: " + ResultItem.Dim(Get(samples.Photo).Result!.OutputWidth, Get(samples.Photo).Result!.OutputHeight));

        // 0.5.0: oxipng is unpacked and used, and a screenshot that would band keeps its full colours.
        string? oxipng = PngOptimizer.ToolPath;
        Check(oxipng is not null && File.Exists(oxipng), "oxipng unpacked: " + oxipng);
        string oxiVersion = "";
        if (oxipng is not null)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(oxipng, "--version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using var p = System.Diagnostics.Process.Start(psi)!;
                oxiVersion = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(10000);
            }
            catch (Exception ex)
            {
                oxiVersion = ex.Message;
            }
        }
        Check(oxiVersion == "oxipng 10.2.1", "oxipng runs: " + oxiVersion);
        var shotResult = Get(samples.Screenshot).Result!;
        Check(shotResult.PngOptimisedFrom is not null && shotResult.OutputBytes < shotResult.PngOptimisedFrom,
            $"oxipng shrank the screenshot copy: {shotResult.PngOptimisedFrom} to {shotResult.OutputBytes} bytes");
        string banded = SampleImages.BandedScreenshot(Path.Combine(outDir, "samples", "banded.png"));
        var br = await Task.Run(() => new ImageShrinker().Shrink(banded, settings.GetPreset(), settings.ToOptions()));
        Check(br.Badges.Contains(Badge.FullColour) && !br.Badges.Contains(Badge.Colours256) && br.PalettePsnr < ImageShrinker.MinPalettePsnr,
            $"banded screenshot stays in full colour ({br.PalettePsnr:0.0} dB for 256 colours)");
        Check(br.PngOptimisedFrom is not null, $"oxipng shrank the full-colour copy: {br.PngOptimisedFrom} to {br.OutputBytes} bytes");
        Check(Loc.T("Badge.FullColour") == "Full colour, no banding", "the new badge has its English text");

        w.Select(Get(samples.Photo));
        await Settle();
        Check(w.CmpShapeB.Text == "3:4, exact", "preview shows the shape: " + w.CmpShapeB.Text);
        Check(Get(samples.Photo).PxText.Contains("shape 3:4 kept", StringComparison.Ordinal), "tile shows the shape: " + Get(samples.Photo).PxText);
        Check(w.DropSub.Text?.Contains("Open With", StringComparison.Ordinal) == true, "Linux wording in the drop zone: " + w.DropSub.Text);
        Shot(w, Path.Combine(outDir, "main-en-light.png"));

        Loc.Instance.Set("es");
        await Settle();
        Check(w.Title?.Contains("Normaliza", StringComparison.Ordinal) == true, "title switches to Spanish");
        Check(w.AddText.Text == "Añadir archivos", "button text follows the language live: " + w.AddText.Text);
        Check(Get(samples.Photo).SizeText.Contains(" a ", StringComparison.Ordinal), "tile text switches to Spanish: " + Get(samples.Photo).SizeText);
        Check(w.StatusMessage.Contains("reducidas", StringComparison.Ordinal), "status line follows the language: " + w.StatusMessage);
        Check(w.CmpShapeB.Text == "3:4, exacta", "shape row follows the language: " + w.CmpShapeB.Text);
        Check(w.DropSub.Text?.Contains("Abrir con", StringComparison.Ordinal) == true, "Spanish Linux wording: " + w.DropSub.Text);
        Shot(w, Path.Combine(outDir, "main-es-light.png"));

        Loc.Instance.Set("hu");
        await Settle();
        Check(w.Title?.Contains("Normalizál", StringComparison.Ordinal) == true, "title switches to Hungarian");
        Check(w.StatusMessage.Contains("kicsinyítve", StringComparison.Ordinal), "Hungarian status line: " + w.StatusMessage);
        Check(w.CmpShapeB.Text == "3:4, pontos", "Hungarian shape row: " + w.CmpShapeB.Text);
        Check(Get(samples.Photo).PxText.Contains("3:4 arány megtartva", StringComparison.Ordinal), "Hungarian tile line: " + Get(samples.Photo).PxText);
        Check(Get(samples.Photo).SizeText.Contains(',', StringComparison.Ordinal), "Hungarian decimal comma: " + Get(samples.Photo).SizeText);
        Shot(w, Path.Combine(outDir, "main-hu-light.png"));
        Loc.Instance.Set("es");
        await Settle();

        w.Select(Get(samples.Screenshot));
        App.ApplyTheme("Dark");
        await Settle();
        Shot(w, Path.Combine(outDir, "main-es-dark.png"));

        Loc.Instance.Set("en");
        App.ApplyTheme("Light");
        w.Width = 420;
        await Settle();
        Check(w.IsNarrow, "compact layout below 900 px");
        Shot(w, Path.Combine(outDir, "main-en-narrow.png"));
        w.Width = 1240;
        await Settle();
        Check(!w.IsNarrow, "wide layout again at 1240 px");

        await ClipboardChecks(w, Get(samples.Photo), Check, Info);
        await DesktopChecks(w, settings, Check);

        // 0.6.0: a whole Word document, and the before/after slider.
        string docx = SampleImages.WordDocument(Path.Combine(outDir, "samples", "Team plan.docx"));
        string bandedShot = SampleImages.BandedScreenshot(Path.Combine(outDir, "samples", "banded-compare.png"));
        await w.AddFilesAsync(new[] { docx, bandedShot }, wordWindow: false);
        await w.WaitIdleAsync();
        await Settle();
        var docItem = w.Items.First(i => i.SourcePath == docx);
        var bandItem = w.Items.First(i => i.SourcePath == bandedShot);
        Check(docItem.IsOk && docItem.IsDocument && docItem.Result!.OutputFormat == "DOCX", "Word document shrunk to a new .docx: " + docItem.Result?.OutputPath);
        Check(docItem.Result!.OutputBytes < docItem.Result.OriginalBytes / 4, $"document much smaller: {docItem.Result.OriginalBytes} to {docItem.Result.OutputBytes} bytes");
        Check(docItem.PxText == "3 of 4 pictures made smaller · DOCX · drawings left as they are: 1", "document tile line: " + docItem.PxText);
        Check(docItem.DragHint == "Drag into Drive" && !docItem.CanCopyPicture && !docItem.CanCompare, "document tile offers Drive, not picture actions");
        Check(docItem.Badges.Contains("Location removed"), "document tile shows Location removed");
        using (var z = System.IO.Compression.ZipFile.OpenRead(docItem.Result.OutputPath!))
        {
            Check(z.GetEntry("word/media/image1.jpg") is not null && z.GetEntry("word/media/image1.png") is null, "PNG photo inside the document became a JPEG");
        }
        w.Select(docItem);
        await Settle();
        Check(w.PreviewDoc.IsVisible && !w.PreviewOk.IsVisible, "preview shows the document panel");
        Check(w.DocCounts.Text == "Pictures found: 4. Made smaller: 3, already fine: 1.", "document counts: " + w.DocCounts.Text);
        Check(!w.PageGroup.IsVisible, "page size buttons hidden for a document");
        Shot(w, Path.Combine(outDir, "main-en-doc.png"));

        w.Select(bandItem);
        await Settle();
        Check(w.CompareButton.IsVisible, "preview offers Compare with original");
        var cmp = w.OpenCompare(bandItem, activate: false);
        Check(cmp is not null, "compare window opens");
        if (cmp is not null)
        {
            for (int i = 0; i < 50 && !cmp.IsReady; i++) await Task.Delay(100);
            await Settle();
            Check(cmp.IsReady && cmp.Pictures!.Width == bandItem.Result!.OutputWidth, "compare window shows the pictures");
            Check(cmp.PaletteOffered, "the 256-colour version is offered for the banded screenshot");
            Check(cmp.RightLabel.StartsWith("Copy · PNG · ", StringComparison.Ordinal), "right side is the copy: " + cmp.RightLabel);
            Check(Math.Abs(cmp.DividerPercent - 50) < 0.01, "divider starts in the middle");
            Shot(cmp, Path.Combine(outDir, "compare-en-copy.png"));
            cmp.ShowPaletteSide(true);
            await Settle();
            Check(cmp.RightLabel == "256 colours · not used", "right side switches to the 256-colour version: " + cmp.RightLabel);
            Shot(cmp, Path.Combine(outDir, "compare-en-256.png"));
            Loc.Instance.Set("hu");
            await Settle();
            Check(cmp.RightLabel == "256 szín · nem használt", "compare window follows the language: " + cmp.RightLabel);
            Shot(cmp, Path.Combine(outDir, "compare-hu-256.png"));
            Loc.Instance.Set("en");
            cmp.Close();
        }
        var photoCmp = w.OpenCompare(Get(samples.Photo), activate: false);
        if (photoCmp is not null)
        {
            for (int i = 0; i < 50 && !photoCmp.IsReady; i++) await Task.Delay(100);
            photoCmp.SetZoom(0);
            App.ApplyTheme("Dark");
            await Settle();
            Check(photoCmp.IsReady && photoCmp.Pictures!.OriginalResized && !photoCmp.PaletteOffered, "photo compare: original resized to the copy, no 256-colour option");
            Shot(photoCmp, Path.Combine(outDir, "compare-en-photo-dark.png"));
            App.ApplyTheme("Light");
            photoCmp.Close();
        }
        Check(w.OpenCompare(Get(samples.Logo)) is null && w.OpenCompare(docItem) is null, "nothing to compare for a kept image or a document");
        await Settle();

        // 0.7.0: smaller PDFs, PDF pages in the compare window, and the update bar.
        string pdfReport = SampleImages.PdfReport(Path.Combine(outDir, "samples", "Quarterly report.pdf"));
        await w.AddFilesAsync(new[] { pdfReport });
        await w.WaitIdleAsync();
        await Settle();
        var pdfItem = w.Items.First(i => i.SourcePath == pdfReport);
        Check(pdfItem.IsOk && pdfItem.IsPdf && pdfItem.Result!.OutputFormat == "PDF", "PDF shrunk to a smaller PDF: " + pdfItem.Result?.OutputPath);
        Check(pdfItem.Result!.OutputBytes < pdfItem.Result.OriginalBytes * 6 / 10, $"PDF much smaller: {pdfItem.Result.OriginalBytes} to {pdfItem.Result.OutputBytes} bytes");
        Check(pdfItem.PxText == "4 of 9 pictures made smaller · 3 pages · PDF · pictures left as they are: 3", "PDF tile line: " + pdfItem.PxText);
        Check(pdfItem.DragHint == "Drag the file" && pdfItem.CanCompare && !pdfItem.CanCopyPicture, "PDF tile offers the file and Compare");
        Check(Nacho.Core.Pdf.PdfPages.Count(pdfItem.Result.OutputPath!) == 3, "the smaller PDF opens with 3 pages");
        w.Select(pdfItem);
        await Settle();
        Check(w.PreviewDoc.IsVisible && w.PdfPart.IsVisible && !w.DocxPart.IsVisible, "preview shows the PDF panel, not the Google Docs steps");
        Check(w.DocPagesB.Text == "3" && w.PreviewHeading.Text == "Whole PDF", "PDF panel: pages and heading");
        Shot(w, Path.Combine(outDir, "main-en-pdf.png"));
        var pcmp = w.OpenCompare(pdfItem, activate: false);
        Check(pcmp is not null, "compare window opens for a PDF");
        if (pcmp is not null)
        {
            for (int i = 0; i < 80 && !pcmp.IsReady; i++) await Task.Delay(100);
            await Settle();
            Check(pcmp.IsReady && pcmp.Pictures!.IsPdf && pcmp.PagesOffered && pcmp.PageLabel == "Page 1 of 3", "PDF compare shows page 1 of 3: " + pcmp.PageLabel);
            await pcmp.GoToPageAsync(2);
            await Settle();
            Check(pcmp.PageIndex == 2 && pcmp.PageLabel == "Page 3 of 3", "PDF compare goes to page 3: " + pcmp.PageLabel);
            Shot(pcmp, Path.Combine(outDir, "compare-en-pdf.png"));
            pcmp.Close();
        }

        string versionNow = UpdateCheck.Current(typeof(App).Assembly).ToString(3);
        w.ShowUpdate(new UpdateInfo(new Version(9, 9, 0), "v9.9.0", UpdateCheck.ReleasesPage + "/tag/v9.9.0"));
        await Settle();
        Check(w.UpdateBarVisible && w.UpdateMessage == $"NACHO 9.9.0 is available. You have {versionNow}.", "update bar: " + w.UpdateMessage);
        Shot(w, Path.Combine(outDir, "main-en-update.png"));
        Loc.Instance.Set("hu");
        await Settle();
        Check(w.UpdateMessage == $"Elérhető a NACHO 9.9.0. A te verziód: {versionNow}.", "update bar follows the language: " + w.UpdateMessage);
        Loc.Instance.Set("en");
        await Settle();

        // 0.8.0 to 0.10.0: a Google Doc, into a new copy or in place, against a pretend Google (nothing goes over the network).
        var fake = new SampleGoogle().WithBook();
        fake.Picked = new HashSet<string>(); // drive.file: only the Docs picked in Google's picker open
        var session = new GoogleSession(new MemoryTokenStore(), fake)
        {
            Client = new GoogleClientInfo("123-selftest.apps.googleusercontent.com", "GOCSPX-selftest"),
            OpenBrowser = SampleGoogle.AnswerSignIn,
            Delay = (_, _) => Task.CompletedTask,
        };
        GoogleUi.Session = session;
        GoogleUi.SelfTestClient = true;
        Check(!session.HasSignIn, "Google: starts signed out");
        var go = new OptionsWindow(settings) { WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(0, 0) };
        go.Show();
        await Settle();
        Check(go.GoogleStatusText == "Not signed in", "Google: Options shows Not signed in: " + go.GoogleStatusText);
        await go.SignInAsync();
        await Settle();
        Check(session.HasSignIn && settings.GoogleEmail == "tester@example.com", "Google: signed in through the browser step: " + settings.GoogleEmail);
        Check(go.GoogleStatusText == "Signed in as tester@example.com", "Google: Options shows the account: " + go.GoogleStatusText);
        go.ScrollToGoogle();
        await Settle();
        Shot(go, Path.Combine(outDir, "options-google-en.png"));
        go.Close();

        string link = "https://docs.google.com/document/d/1Go0DSEPjSepxQ5O5w-R7YEh55xUJCXDhGRB16mPWo9Y/edit?usp=sharing";
        try
        {
            await w.Clipboard!.SetTextAsync(link);
            await Settle();
            Check(await w.ClipboardDocsLinkAsync() == link, "Google: a Docs link on the clipboard is recognised");
        }
        catch (Exception ex)
        {
            Info("clipboard text not available here: " + ex.Message);
        }
        // The Google Doc button and Ctrl+G start at Choose even with a Docs link on the clipboard (0.10.0 bug fix).
        var g0 = w.OpenGoogleDoc(null, activate: false);
        await Settle();
        Check(g0.StageName == "Link" && !g0.IsWorking && g0.PrimaryLabel == Loc.T("Gd.Choose") && fake.Picked.Count == 0,
            $"Google: the button opens at Choose and checks nothing ({g0.StageName}, {g0.PrimaryLabel})");
        g0.Close();
        await Settle();

        var gw = w.OpenGoogleDoc(link, activate: false);
        await Until(() => gw.Plan is not null && !gw.IsWorking, 120);
        var gplan = gw.Plan;
        string docId = "1Go0DSEPjSepxQ5O5w-R7YEh55xUJCXDhGRB16mPWo9Y";
        Check(gplan is not null, $"Google: the link opened the window and the check ran by itself (error: {gw.Error ?? "none"}, busy: {gw.Busy})");
        Check(fake.Picked.Contains(docId), "Google: a Doc not given to NACHO yet opened the picker for that Doc, then the check ran");
        if (gplan is not null)
        {
            Check(gw.StageName == "Summary" && gw.IntoCopy, "Google: after the check NACHO waits at Decide, with a new copy chosen: " + gw.StageName);
            Check(gplan.Jobs.Count == 6 && gplan.ToReplace == 4, $"Google: 6 pictures found, 4 to shrink ({gplan.Jobs.Count}, {gplan.ToReplace})");
            Check(gplan.Count(GooglePictureState.Kept) == 1 && gplan.Count(GooglePictureState.Skipped) == 1, "Google: small picture kept, drawing left alone");
            Check(fake.Files.Count == 0 && fake.Replaced.Count == 0 && fake.Copies.Count == 0, "Google: the check changed nothing in the Doc or Drive");
            Check(gplan.OriginalsFolder is { } of && Directory.GetFiles(of).Length == 5, "Google: 5 originals saved in " + gplan.OriginalsFolder);
            Check(gplan.BytesAfter < gplan.BytesBefore / 4, $"Google: pictures {gplan.BytesBefore} to {gplan.BytesAfter} bytes");
            Check(gw.PrimaryLabel == Loc.T("Gd.MakeCopy"), "Google: the main button says " + gw.PrimaryLabel);
            await Settle();
            Shot(gw, Path.Combine(outDir, "google-en-decide.png"));
            Loc.Instance.Set("hu");
            await Settle();
            Shot(gw, Path.Combine(outDir, "google-hu-decide.png"));
            Loc.Instance.Set("es");
            await Settle();
            Shot(gw, Path.Combine(outDir, "google-es-decide.png"));
            Loc.Instance.Set("en");
            await Settle();

            // Into a new copy (the default): the original is only read.
            await gw.ReplaceAsync();
            await Settle();
            var made = fake.Copies.FirstOrDefault();
            Check(fake.Copies.Count == 1 && made.Source == docId && made.Name == "Plantas no nativas (sample) (smaller)",
                $"Google: one copy made, next to the original ({fake.Copies.Count}, {made.Name})");
            Check(fake.Replaced.Count == 4 && fake.ReplacedIn.All(d => d == made.Id) && fake.Files.Count == 0,
                $"Google: 4 pictures replaced in the copy, none in the original, no temporary file left ({fake.Replaced.Count}, {string.Join(",", fake.ReplacedIn.Distinct())})");
            Check(gw.Done == Loc.T("Gd.DoneCopyTitle") && gw.DoneDetail.StartsWith("4 of 4 pictures replaced", StringComparison.Ordinal), "Google: done message: " + gw.Done + " / " + gw.DoneDetail);
            Check(w.StatusMessage.Contains("4 pictures replaced", StringComparison.Ordinal), "Google: main window status: " + w.StatusMessage);
            Check(settings.GoogleRecent.Count == 1 && settings.GoogleRecent[0].Copy && settings.GoogleRecent[0].DocId == made.Id, "Google: the copy is listed under Recently shrunk");
            Shot(gw, Path.Combine(outDir, "google-en-done.png"));

            // Into this Doc: the user's other choice.
            await gw.UseLinkAsync(link, autoCheck: true);
            await Until(() => gw.Plan is not null && !gw.IsWorking, 120);
            gw.IntoCopy = false;
            await Settle();
            Check(gw.PrimaryLabel == Loc.F("Gd.ReplaceN", 4), "Google: in-place button: " + gw.PrimaryLabel);
            Shot(gw, Path.Combine(outDir, "google-en-decide-doc.png"));
            await gw.ReplaceAsync();
            await Settle();
            Check(fake.Replaced.Count == 8 && fake.ReplacedIn.Skip(4).All(d => d == docId) && fake.Copies.Count == 1,
                "Google: in place, 4 more pictures replaced in the Doc itself and no copy made");
            Check(gw.Done == Loc.T("Gd.DoneDocTitle"), "Google: in-place done message: " + gw.Done);

            await gw.UseLinkAsync("https://example.com/not-a-doc", autoCheck: false);
            await gw.CheckAsync();
            await Settle();
            Check(gw.LinkErrorShown && gw.Plan is null, "Google: a wrong link is explained, nothing checked");

            // No link at all: the main button opens Google's picker and checks the Doc picked there.
            await gw.UseLinkAsync("", autoCheck: false);
            await Settle();
            Check(gw.PrimaryLabel == Loc.T("Gd.Choose"), "Google: with no link the main button is " + gw.PrimaryLabel);
            Check(!gw.LinkErrorShown, "Google: emptying the link box clears the wrong-link message");
            Check(gw.RecentCount == 2, "Google: Recently shrunk lists both runs: " + gw.RecentCount);
            Check(gw.SafeLine.Contains(Loc.T("Gd.SafeTitle"), StringComparison.Ordinal), "Google: the read-only line is shown before anything changes");
            Shot(gw, Path.Combine(outDir, "google-en-choose.png"));
            await gw.CheckAsync();
            await Until(() => !gw.IsWorking, 120);
            Check(gw.DocId == SampleGoogle.PickerDefault && gw.Plan is not null && fake.Picked.Contains(SampleGoogle.PickerDefault),
                $"Google: the picker's Doc was checked ({gw.DocId}, error: {gw.Error ?? "none"})");
            Check(fake.Replaced.Count == 8, "Google: picking and checking changed nothing");

            // A full Drive: the copy can't be made, nothing changes, the user is told and can choose again.
            fake.CopyFails = """{"error":{"code":403,"message":"The user's Drive storage quota has been exceeded.","errors":[{"reason":"storageQuotaExceeded"}]}}""";
            gw.IntoCopy = true;
            await gw.ReplaceAsync();
            await Settle();
            Check(gw.Error == Loc.T("Gd.ErrDriveFull") && gw.StageName == "Summary" && fake.Copies.Count == 1 && fake.Replaced.Count == 8,
                $"Google: a full Drive is explained and nothing changes ({gw.StageName}, {gw.Error})");
            Shot(gw, Path.Combine(outDir, "google-en-drivefull.png"));
            fake.CopyFails = null;

            fake.ReadOnly = true;
            gw.IntoCopy = false;
            await gw.ReplaceAsync();
            await Settle();
            Check(gw.Error == Loc.T("Gd.ErrReadOnly") && gw.StageName == "Summary", $"Google: a view-only Doc gets a clear message and the choice again ({gw.StageName}): " + gw.Error);
            Check(fake.Files.Count == 0, "Google: no temporary file left after the refusal");
            Shot(gw, Path.Combine(outDir, "google-en-readonly.png"));
            fake.ReadOnly = false;
        }
        gw.Close();
        await Settle();

        // 0.12.0: a Word file on this PC (a new copy; then the file itself and Restore original) and in Google Drive
        // (a new copy; then a new version of the file), against the same pretend Google.
        string wordDir = Path.Combine(outDir, "word");
        Directory.CreateDirectory(wordDir);
        string handbook = SampleImages.WordDocument(Path.Combine(wordDir, "Team handbook.docx"));
        byte[] handbookBytes = File.ReadAllBytes(handbook);
        settings.WordRecent.Clear();
        settings.WordIntoCopy = true;
        await w.AddFilesAsync(new[] { handbook });
        var ww = w.OpenWordDoc(null, activate: false);
        await Until(() => ww.StageName == "Summary" || ww.Error is not null, 90);
        Check(ww.StageName == "Summary" && ww.Check is { CanShrink: true }, $"Word: a dropped .docx opens the Word window and is checked ({ww.StageName}, error: {ww.Error ?? "none"})");
        Check(!w.Items.Any(i => i.SourcePath == handbook), "Word: the .docx did not go into the pictures list");
        Check(File.ReadAllBytes(handbook).AsSpan().SequenceEqual(handbookBytes), "Word: the check changed nothing in the file");
        Check(ww.IntoCopy && ww.PrimaryLabel == Loc.T("Gd.MakeCopy"), "Word: a new copy is chosen; the button says " + ww.PrimaryLabel);
        Check(ww.WillLine == Loc.F("Wd.WillCopyPc", "Team handbook.docx"), "Word: the banner says only the new file is written: " + ww.WillLine);
        Shot(ww, Path.Combine(outDir, "word-en-decide.png"));
        await ww.ApplyAsync();
        await Settle();
        string wordCopy = Path.Combine(wordDir, "Team handbook (smaller).docx");
        Check(ww.StageName == "Done" && File.Exists(wordCopy) && new FileInfo(wordCopy).Length < handbookBytes.Length,
            $"Word: the smaller copy is next to the original ({ww.StageName}, error: {ww.Error ?? "none"})");
        Check(File.ReadAllBytes(handbook).AsSpan().SequenceEqual(handbookBytes), "Word: the original is untouched after the copy");
        Check(ww.Done == Loc.T("Wd.DoneCopyTitle") && settings.WordRecent.Count == 1 && settings.WordRecent[0].Copy, "Word: done, and listed as a copy: " + ww.Done);
        Check(w.StatusMessage.Contains("pictures replaced", StringComparison.Ordinal), "Word: main window status: " + w.StatusMessage);

        await ww.OpenLocalAsync(handbook);
        await Until(() => ww.StageName == "Summary" || ww.Error is not null, 90);
        ww.IntoCopy = false;
        int wordPics = ww.Check?.Report.Shrunk ?? 0;
        Check(ww.PrimaryLabel == (wordPics == 1 ? Loc.T("Wd.Replace1") : Loc.F("Wd.ReplaceN", wordPics)), "Word: in-place button: " + ww.PrimaryLabel);
        Shot(ww, Path.Combine(outDir, "word-en-decide-file.png"));
        await ww.ApplyAsync();
        await Settle();
        string backupDir = Path.Combine(settings.OutputFolder, "Word originals");
        var backups = Directory.Exists(backupDir) ? Directory.GetFiles(backupDir, "Team handbook *.docx") : Array.Empty<string>();
        Check(ww.StageName == "Done" && new FileInfo(handbook).Length < handbookBytes.Length, $"Word: the file itself is smaller ({ww.StageName}, error: {ww.Error ?? "none"})");
        Check(backups.Length == 1 && File.ReadAllBytes(backups[0]).AsSpan().SequenceEqual(handbookBytes), "Word: the original was saved first in Docs-Ready > Word originals");
        Check(ww.Done == Loc.F("Wd.DoneTitle", "Team handbook.docx"), "Word: in-place done message: " + ww.Done);
        Shot(ww, Path.Combine(outDir, "word-en-done.png"));
        ww.RestoreOriginal();
        await Settle();
        Check(ww.WasRestored && File.ReadAllBytes(handbook).AsSpan().SequenceEqual(handbookBytes), "Word: Restore original put the old file back");
        Check(settings.WordRecent.Count == 1, "Word: the restored file is no longer listed as changed");

        fake.WithWordFile(handbookBytes);
        await ww.ChooseInDriveAsync();
        await Until(() => !ww.IsWorking, 90);
        Check(ww.StageName == "Summary" && ww.Check?.Where == Nacho.Core.Word.WordWhere.Drive, $"Word: a file picked in Drive is downloaded and checked ({ww.StageName}, error: {ww.Error ?? "none"})");
        ww.IntoCopy = true;
        Shot(ww, Path.Combine(outDir, "word-en-decide-drive.png"));
        await ww.ApplyAsync();
        await Settle();
        Check(ww.StageName == "Done" && fake.DriveFiles.Count == 2 && fake.DriveFiles.Values.Any(f => f.Name == "Team handbook (smaller).docx")
            && fake.DriveFiles[SampleGoogle.PickerWordDefault].Version == 1, $"Word: the Drive copy is next to the original, which is untouched ({ww.StageName}, error: {ww.Error ?? "none"})");
        await ww.ChooseInDriveAsync();
        await Until(() => !ww.IsWorking, 90);
        ww.IntoCopy = false;
        Check(ww.PrimaryLabel == Loc.T("Wd.Upload"), "Word: Drive in-place button: " + ww.PrimaryLabel);
        await ww.ApplyAsync();
        await Settle();
        Check(ww.StageName == "Done" && fake.DriveFiles[SampleGoogle.PickerWordDefault].Version == 2 && fake.DriveFiles.Count == 2,
            $"Word: the Drive file got a new version and no other file ({ww.StageName}, error: {ww.Error ?? "none"})");
        Check(settings.WordRecent.Count == 3, "Word: Recently shrunk lists three runs: " + settings.WordRecent.Count);
        ww.Close();
        await Settle();
        var ww2 = w.OpenWordDoc(null, activate: false);
        await Settle();
        Check(ww2.StageName == "Choose" && ww2.RecentCount == 3 && ww2.SafeLine.Contains(Loc.T("Wd.SafeTitle"), StringComparison.Ordinal),
            $"Word: the button opens at Choose, read only, with the recent files ({ww2.StageName}, {ww2.RecentCount}, {ww2.SafeLine})");
        Shot(ww2, Path.Combine(outDir, "word-en-choose.png"));
        ww2.Close();

        foreach (string lang in Strings.Languages)
        {
            Loc.Instance.Set(lang);
            var o = new OptionsWindow(settings) { WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(0, 0) };
            o.Show();
            await Settle();
            Shot(o, Path.Combine(outDir, $"options-{lang}.png"));
            o.Close();
        }
        foreach (string lang in new[] { "en", "es", "hu" })
        {
            Loc.Instance.Set(lang);
            var about = new AboutWindow(settings) { WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(0, 0) };
            about.Show();
            await Settle();
            Shot(about, Path.Combine(outDir, $"about-{lang}.png"));
            about.Close();
        }
        Loc.Instance.Set("en");

        w.Close();
        await Settle();
        foreach (string shot in new[] { "main-en-light", "main-es-light", "main-hu-light", "main-es-dark", "main-en-narrow", "main-en-doc", "main-en-pdf", "compare-en-pdf", "main-en-update", "compare-en-copy", "compare-en-256", "compare-hu-256", "compare-en-photo-dark", "options-en", "options-es", "options-hu", "about-en", "about-es", "about-hu", "main-en-home", "main-en-home-dark", "main-en-home-narrow", "options-google-en", "google-en-choose", "google-en-decide", "google-hu-decide", "google-es-decide", "google-en-decide-doc", "google-en-done", "google-en-drivefull", "google-en-readonly", "word-en-choose", "word-en-decide", "word-en-decide-file", "word-en-decide-drive", "word-en-done" })
        {
            var f = new FileInfo(Path.Combine(outDir, shot + ".png"));
            Check(f.Exists && f.Length > 10_000, $"screenshot {shot}.png saved");
        }

        log.AppendLine();
        log.AppendLine(fails == 0 ? "ALL PASSED" : $"{fails} FAILED");
        await File.WriteAllTextAsync(Path.Combine(outDir, "selftest-report.txt"), log.ToString());
        Console.Write(log.ToString());
        return fails == 0 ? 0 : 1;
    }

    /// <summary>Copy file must offer the standard file list and the GNOME one; Copy path plain text; Paste must take a picture.</summary>
    private static async Task ClipboardChecks(MainWindow w, ResultItem photo, Action<bool, string> check, Action<string> info)
    {
        var clipboard = w.Clipboard;
        if (clipboard is null)
        {
            check(false, "the window has a clipboard");
            return;
        }
        string path = photo.DeliverPath!;
        string uri = new Uri(path).AbsoluteUri;

        w.CopyFile(photo);
        await Settle();
        await Task.Delay(300);
        var formats = await clipboard.GetDataFormatsAsync();
        info("clipboard formats after Copy file (in process): " + string.Join(", ", formats.Select(f => f.Identifier)));

        if (Shell.Run("which", "xclip") == 0)
        {
            // Asked from another process, the way a file manager or browser asks.
            string targets = await CaptureAsync("xclip", "-selection", "clipboard", "-t", "TARGETS", "-o");
            info("clipboard targets seen by another program: " + targets.Replace('\n', ' ').Trim());
            check(targets.Contains("text/uri-list", StringComparison.Ordinal), "Copy file offers text/uri-list to other programs");
            check(targets.Contains("x-special/gnome-copied-files", StringComparison.Ordinal), "Copy file offers the GNOME file list to other programs");
            string uris = await CaptureAsync("xclip", "-selection", "clipboard", "-t", "text/uri-list", "-o");
            check(uris.Contains(uri, StringComparison.Ordinal), "the file list holds the copy: " + uris.Trim());
            string gnome = await CaptureAsync("xclip", "-selection", "clipboard", "-t", "x-special/gnome-copied-files", "-o");
            check(gnome.StartsWith("copy\n" + uri, StringComparison.Ordinal), "the GNOME list reads copy + the file: " + gnome.Replace('\n', '|'));
        }
        else
        {
            info("xclip not found: clipboard checked in process only");
            var files = await clipboard.TryGetFilesAsync();
            check(files?.Any(f => f.TryGetLocalPath() == path) == true, "Copy file puts the copy on the clipboard");
        }

        w.Select(photo);
        string? text = null;
        await clipboard.SetTextAsync(path);
        text = await clipboard.TryGetTextAsync();
        check(text == path, "Copy path text round trip");

        // Paste: a picture on the clipboard becomes a new tile.
        int before = w.Items.Count;
        // Not disposed: the clipboard keeps using it until something else is copied.
        var bmp = Images.Load(path, 0);
        if (bmp is not null) await clipboard.SetBitmapAsync(bmp);
        await Settle();
        var pasteMethod = typeof(MainWindow).GetMethod("Paste", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        pasteMethod?.Invoke(w, null);
        for (int i = 0; i < 40 && w.Items.Count == before; i++) await Task.Delay(100);
        await w.WaitIdleAsync();
        await Settle();
        check(w.Items.Count == before + 1 && w.Items[0].DisplayName.StartsWith("Clipboard ", StringComparison.Ordinal),
            "Paste turns a copied picture into a tile: " + (w.Items.Count > 0 ? w.Items[0].DisplayName : "none"));
        if (w.Items.Count == before + 1) w.Items.RemoveAt(0);
    }

    /// <summary>The app menu entry and the right-click entries, written to the test folder and removed again.</summary>
    private static async Task DesktopChecks(MainWindow w, AppSettings settings, Action<bool, string> check)
    {
        var t = Shell.Targets;
        settings.ContextMenu = true;
        settings.AppMenu = true;
        Shell.Apply(true, true, Loc.T("Shell.MenuText"));
        check(Shell.IsContextMenuOn() && Shell.IsAppMenuOn(), "menus on after Apply");
        check(File.Exists(Path.Combine(t.NautilusScripts, "Make Docs-ready")), "Files (Nautilus) script named in English");
        check(File.Exists(t.NemoAction) && File.Exists(t.DolphinMenu) && File.Exists(Path.Combine(t.CajaScripts, "Make Docs-ready")), "Nemo, Dolphin and Caja entries written");
        check(File.Exists(t.IconSvg) && File.Exists(t.IconPng), "icons written");
        check(DesktopFiles.AppEntryExec(t) == Shell.ExecPath, "app menu points at this program: " + DesktopFiles.AppEntryExec(t));
        check(File.GetUnixFileMode(Path.Combine(t.NautilusScripts, "Make Docs-ready")).HasFlag(UnixFileMode.UserExecute), "script is executable");

        if (Shell.Run("which", "desktop-file-validate") == 0)
        {
            string result = await CaptureAsync("desktop-file-validate", t.AppEntry);
            check(result.Trim().Length == 0 || !result.Contains("error", StringComparison.OrdinalIgnoreCase), "desktop-file-validate accepts the app entry: " + result.Trim());
        }

        // Switching the language in the window renames the script, like Windows updates its menu text.
        w.LangHu.IsChecked = true;
        await Settle();
        check(File.Exists(Path.Combine(t.NautilusScripts, "Docs-kész másolat készítése")) && !File.Exists(Path.Combine(t.NautilusScripts, "Make Docs-ready")),
            "language switch renames the Files script to Hungarian");
        w.LangEn.IsChecked = true;
        await Settle();

        DesktopFiles.RemoveAll(t);
        settings.ContextMenu = false;
        settings.AppMenu = false;
        bool anyLeft = Directory.Exists(t.DataHome) && Directory.EnumerateFiles(t.DataHome, "*", SearchOption.AllDirectories).Any()
            || Directory.Exists(t.ConfigHome) && Directory.EnumerateFiles(t.ConfigHome, "*", SearchOption.AllDirectories).Any();
        check(!anyLeft, "turning both off leaves no file behind");
    }

    /// <summary>Runs a tool off the UI thread, so the window can answer it (the clipboard owner must keep responding).</summary>
    private static Task<string> CaptureAsync(string file, params string[] args) => Task.Run(() => Capture(file, args));

    private static string Capture(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(5000)) return "";
            return output.Result;
        }
        catch
        {
            return "";
        }
    }

    private static async Task Until(Func<bool> done, int seconds)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < until) await Task.Delay(200);
        await Settle();
    }

    private static async Task Settle()
    {
        for (int i = 0; i < 3; i++) await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(300);
        for (int i = 0; i < 2; i++) await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static void Shot(Window w, string path)
    {
        var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(w.Bounds.Width)), Math.Max(1, (int)Math.Ceiling(w.Bounds.Height)));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(w);
        bitmap.Save(path, new PngBitmapEncoderOptions());
    }
}
