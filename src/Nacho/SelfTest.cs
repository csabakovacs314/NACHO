using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nacho.Core;
using Nacho.Core.Google;

namespace Nacho;

/// <summary>
/// NACHO.exe --selftest &lt;folder&gt;: builds sample images, runs them through the real window off-screen,
/// checks the results, and saves screenshots in English, Spanish, light, dark and narrow.
/// Uses its own settings file and output folder; never touches the registry or the real settings.
/// </summary>
public static class SelfTest
{
    private static readonly string[] ThemeKeys =
    {
        "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush", "CardBackgroundFillColorDefaultBrush",
        "CardStrokeColorDefaultBrush", "SystemFillColorSuccessBrush", "SystemFillColorCriticalBrush",
        "ControlStrongStrokeColorDefaultBrush", "ControlStrokeColorDefaultBrush", "SubtleFillColorSecondaryBrush",
        "AccentFillColorDefaultBrush", "AccentTextFillColorPrimaryBrush", "DividerStrokeColorDefaultBrush",
        "LayerFillColorDefaultBrush", "AccentButtonStyle", "SystemFillColorCautionBrush",
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

        Directory.CreateDirectory(outDir);
        AppSettings.PathOverride = Path.Combine(outDir, "settings.json");
        if (File.Exists(AppSettings.PathOverride)) File.Delete(AppSettings.PathOverride);
        var settings = AppSettings.Load();
        string? installerLanguage = AppSettings.InstallerLanguage();
        if (installerLanguage != null)
        {
            Check(settings.Language == installerLanguage, "first run uses the installer's language: " + settings.Language);
        }
        log.AppendLine($"INFO  exe: {Environment.ProcessPath}");
        log.AppendLine("INFO  built-in Google client: " + (GoogleClientInfo.BuiltIn is { } gci ? "present, id ends " + gci.ClientId[^30..] : "none"));
        log.AppendLine($"INFO  right-click menu on: {ShellIntegration.IsContextMenuOn()}, Send to on: {ShellIntegration.IsSendToOn()}");
        settings.Language = "en";
        settings.PageSize = "A4";
        settings.OutputFolder = Path.Combine(outDir, "Docs-Ready");

        foreach (string mode in new[] { "Light", "Dark" })
        {
            App.ApplyTheme(mode);
            foreach (string key in ThemeKeys) Check(Application.Current.TryFindResource(key) != null, $"{mode} theme has {key}");
        }
        App.ApplyTheme("Light");
        Loc.Instance.Set("en");

        var samples = await Task.Run(() => SampleImages.Generate(Path.Combine(outDir, "samples")));
        var w = new MainWindow(settings)
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = 0,
            Width = 1240,
            Height = 800,
        };
        w.Show();
        await Settle();

        // 0.11.0: one design everywhere; an empty list shows the start screen with both ways in.
        Check(w.HomeVisible, "start screen shows while the list is empty");
        Shot(w, Path.Combine(outDir, "main-en-home.png"), dark: false);
        App.ApplyTheme("Dark");
        await Settle();
        Shot(w, Path.Combine(outDir, "main-en-home-dark.png"), dark: true);
        App.ApplyTheme("Light");
        w.Width = 420;
        await Settle();
        Shot(w, Path.Combine(outDir, "main-en-home-narrow.png"), dark: false);
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
        Shot(w, Path.Combine(outDir, "main-en-light.png"), dark: false);

        Loc.Instance.Set("es");
        await Settle();
        Check(w.Title.Contains("Normaliza", StringComparison.Ordinal), "title switches to Spanish");
        Check(Get(samples.Photo).SizeText.Contains(" a ", StringComparison.Ordinal), "tile text switches to Spanish: " + Get(samples.Photo).SizeText);
        Check(w.StatusMessage.Contains("reducidas", StringComparison.Ordinal), "status line follows the language: " + w.StatusMessage);
        Check(w.CmpShapeB.Text == "3:4, exacta", "shape row follows the language: " + w.CmpShapeB.Text);
        Shot(w, Path.Combine(outDir, "main-es-light.png"), dark: false);

        Loc.Instance.Set("hu");
        await Settle();
        Check(w.Title.Contains("Normalizál", StringComparison.Ordinal), "title switches to Hungarian");
        Check(w.StatusMessage.Contains("kicsinyítve", StringComparison.Ordinal), "Hungarian status line: " + w.StatusMessage);
        Check(w.CmpShapeB.Text == "3:4, pontos", "Hungarian shape row: " + w.CmpShapeB.Text);
        Check(Get(samples.Photo).PxText.Contains("3:4 arány megtartva", StringComparison.Ordinal), "Hungarian tile line: " + Get(samples.Photo).PxText);
        Check(Get(samples.Photo).SizeText.Contains(",", StringComparison.Ordinal), "Hungarian decimal comma: " + Get(samples.Photo).SizeText);
        Shot(w, Path.Combine(outDir, "main-hu-light.png"), dark: false);
        Loc.Instance.Set("es");
        await Settle();

        w.Select(Get(samples.Screenshot));
        App.ApplyTheme("Dark");
        await Settle();
        Shot(w, Path.Combine(outDir, "main-es-dark.png"), dark: true);

        Loc.Instance.Set("en");
        App.ApplyTheme("Light");
        w.Width = 420;
        await Settle();
        Check(w.IsNarrow, "compact layout below 900 px");
        Shot(w, Path.Combine(outDir, "main-en-narrow.png"), dark: false);
        w.Width = 1240;
        await Settle();

        // 0.6.0: a whole Word document, and the before/after slider.
        string docx = SampleImages.WordDocument(Path.Combine(outDir, "samples", "Team plan.docx"));
        await w.AddFilesAsync(new[] { docx, banded });
        await w.WaitIdleAsync();
        await Settle();
        var docItem = w.Items.First(i => i.SourcePath == docx);
        var bandItem = w.Items.First(i => i.SourcePath == banded);
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
        Check(w.PreviewDoc.Visibility == Visibility.Visible && w.PreviewOk.Visibility != Visibility.Visible, "preview shows the document panel");
        Check(w.DocCounts.Text == "Pictures found: 4. Made smaller: 3, already fine: 1.", "document counts: " + w.DocCounts.Text);
        Check(w.PageGroup.Visibility != Visibility.Visible, "page size buttons hidden for a document");
        Shot(w, Path.Combine(outDir, "main-en-doc.png"), dark: false);

        w.Select(bandItem);
        await Settle();
        Check(w.CompareButton.Visibility == Visibility.Visible, "preview offers Compare with original");
        var cmp = w.OpenCompare(bandItem, activate: false);
        Check(cmp is not null, "compare window opens");
        if (cmp is not null)
        {
            cmp.Left = -20000;
            cmp.Top = 0;
            for (int i = 0; i < 40 && !cmp.IsReady; i++) await Task.Delay(100);
            await Settle();
            Check(cmp.IsReady && cmp.Pictures!.Width == bandItem.Result!.OutputWidth, "compare window shows the pictures");
            Check(cmp.PaletteOffered, "the 256-colour version is offered for the banded screenshot");
            Check(cmp.RightLabel.StartsWith("Copy · PNG · ", StringComparison.Ordinal), "right side is the copy: " + cmp.RightLabel);
            Check(Math.Abs(cmp.DividerPercent - 50) < 0.01, "divider starts in the middle");
            Shot(cmp, Path.Combine(outDir, "compare-en-copy.png"), dark: false);
            cmp.ShowPaletteSide(true);
            await Settle();
            Check(cmp.RightLabel == "256 colours · not used", "right side switches to the 256-colour version: " + cmp.RightLabel);
            Shot(cmp, Path.Combine(outDir, "compare-en-256.png"), dark: false);
            Loc.Instance.Set("hu");
            await Settle();
            Check(cmp.RightLabel == "256 szín · nem használt", "compare window follows the language: " + cmp.RightLabel);
            Shot(cmp, Path.Combine(outDir, "compare-hu-256.png"), dark: false);
            Loc.Instance.Set("en");
            cmp.Close();
        }
        var photoCmp = w.OpenCompare(Get(samples.Photo), activate: false);
        if (photoCmp is not null)
        {
            photoCmp.Left = -20000;
            photoCmp.Top = 0;
            for (int i = 0; i < 40 && !photoCmp.IsReady; i++) await Task.Delay(100);
            photoCmp.SetZoom(0);
            App.ApplyTheme("Dark");
            await Settle();
            Check(photoCmp.IsReady && photoCmp.Pictures!.OriginalResized && !photoCmp.PaletteOffered, "photo compare: original resized to the copy, no 256-colour option");
            Shot(photoCmp, Path.Combine(outDir, "compare-en-photo-dark.png"), dark: true);
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
        Check(w.PreviewDoc.Visibility == Visibility.Visible && w.PdfPart.Visibility == Visibility.Visible && w.DocxPart.Visibility != Visibility.Visible, "preview shows the PDF panel, not the Google Docs steps");
        Check(w.DocPagesB.Text == "3" && w.PreviewHeading.Text == "Whole PDF", "PDF panel: pages and heading");
        Shot(w, Path.Combine(outDir, "main-en-pdf.png"), dark: false);
        var pcmp = w.OpenCompare(pdfItem, activate: false);
        Check(pcmp is not null, "compare window opens for a PDF");
        if (pcmp is not null)
        {
            pcmp.Left = -20000;
            pcmp.Top = 0;
            for (int i = 0; i < 80 && !pcmp.IsReady; i++) await Task.Delay(100);
            await Settle();
            Check(pcmp.IsReady && pcmp.Pictures!.IsPdf && pcmp.PagesOffered && pcmp.PageLabel == "Page 1 of 3", "PDF compare shows page 1 of 3: " + pcmp.PageLabel);
            await pcmp.GoToPageAsync(2);
            await Settle();
            Check(pcmp.PageIndex == 2 && pcmp.PageLabel == "Page 3 of 3", "PDF compare goes to page 3: " + pcmp.PageLabel);
            Shot(pcmp, Path.Combine(outDir, "compare-en-pdf.png"), dark: false);
            pcmp.Close();
        }

        string versionNow = UpdateCheck.Current(typeof(App).Assembly).ToString(3);
        w.ShowUpdate(new UpdateInfo(new Version(9, 9, 0), "v9.9.0", UpdateCheck.ReleasesPage + "/tag/v9.9.0"));
        await Settle();
        Check(w.UpdateBarVisible && w.UpdateMessage == $"NACHO 9.9.0 is available. You have {versionNow}.", "update bar: " + w.UpdateMessage);
        Shot(w, Path.Combine(outDir, "main-en-update.png"), dark: false);
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
        var go = new OptionsWindow(settings) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0 };
        go.Show();
        await Settle();
        Check(go.GoogleStatusText == "Not signed in", "Google: Options shows Not signed in: " + go.GoogleStatusText);
        await go.SignInAsync();
        await Settle();
        Check(session.HasSignIn && settings.GoogleEmail == "tester@example.com", "Google: signed in through the browser step: " + settings.GoogleEmail);
        Check(go.GoogleStatusText == "Signed in as tester@example.com", "Google: Options shows the account: " + go.GoogleStatusText);
        go.ScrollToGoogle();
        await Settle();
        Shot(go, Path.Combine(outDir, "options-google-en.png"), dark: false);
        go.Close();

        string link = "https://docs.google.com/document/d/1Go0DSEPjSepxQ5O5w-R7YEh55xUJCXDhGRB16mPWo9Y/edit?usp=sharing";
        try
        {
            Clipboard.SetText(link);
            Check(MainWindow.ClipboardDocsLink() == link, "Google: a Docs link on the clipboard is recognised");
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            log.AppendLine("INFO  clipboard busy: " + ex.Message);
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
            Shot(gw, Path.Combine(outDir, "google-en-decide.png"), dark: false);
            Loc.Instance.Set("hu");
            await Settle();
            Shot(gw, Path.Combine(outDir, "google-hu-decide.png"), dark: false);
            Loc.Instance.Set("es");
            await Settle();
            Shot(gw, Path.Combine(outDir, "google-es-decide.png"), dark: false);
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
            Shot(gw, Path.Combine(outDir, "google-en-done.png"), dark: false);

            // Into this Doc: the user's other choice.
            await gw.UseLinkAsync(link, autoCheck: true);
            await Until(() => gw.Plan is not null && !gw.IsWorking, 120);
            gw.IntoCopy = false;
            await Settle();
            Check(gw.PrimaryLabel == Loc.F("Gd.ReplaceN", 4), "Google: in-place button: " + gw.PrimaryLabel);
            Shot(gw, Path.Combine(outDir, "google-en-decide-doc.png"), dark: false);
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
            Shot(gw, Path.Combine(outDir, "google-en-choose.png"), dark: false);
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
            Shot(gw, Path.Combine(outDir, "google-en-drivefull.png"), dark: false);
            fake.CopyFails = null;

            fake.ReadOnly = true;
            gw.IntoCopy = false;
            await gw.ReplaceAsync();
            await Settle();
            Check(gw.Error == Loc.T("Gd.ErrReadOnly") && gw.StageName == "Summary", $"Google: a view-only Doc gets a clear message and the choice again ({gw.StageName}): " + gw.Error);
            Check(fake.Files.Count == 0, "Google: no temporary file left after the refusal");
            Shot(gw, Path.Combine(outDir, "google-en-readonly.png"), dark: false);
            fake.ReadOnly = false;
        }
        gw.Close();

        foreach (string lang in Strings.Languages)
        {
            Loc.Instance.Set(lang);
            var o = new OptionsWindow(settings) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0 };
            o.Show();
            await Settle();
            Shot(o, Path.Combine(outDir, $"options-{lang}.png"), dark: false);
            o.Close();
        }
        foreach (string lang in new[] { "en", "es", "hu" })
        {
            Loc.Instance.Set(lang);
            var about = new AboutWindow(settings) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0 };
            about.Show();
            await Settle();
            Shot(about, Path.Combine(outDir, $"about-{lang}.png"), dark: false);
            about.Close();
        }
        Loc.Instance.Set("en");

        w.Close();
        foreach (string shot in new[] { "main-en-light", "main-es-light", "main-hu-light", "main-es-dark", "main-en-narrow", "main-en-doc", "main-en-pdf", "compare-en-pdf", "main-en-update", "compare-en-copy", "compare-en-256", "compare-hu-256", "compare-en-photo-dark", "options-en", "options-es", "options-hu", "about-en", "about-es", "about-hu", "main-en-home", "main-en-home-dark", "main-en-home-narrow", "options-google-en", "google-en-choose", "google-en-decide", "google-hu-decide", "google-es-decide", "google-en-decide-doc", "google-en-done", "google-en-drivefull", "google-en-readonly" })
        {
            var f = new FileInfo(Path.Combine(outDir, shot + ".png"));
            Check(f.Exists && f.Length > 10_000, $"screenshot {shot}.png saved");
        }

        log.AppendLine();
        log.AppendLine(fails == 0 ? "ALL PASSED" : $"{fails} FAILED");
        await File.WriteAllTextAsync(Path.Combine(outDir, "selftest-report.txt"), log.ToString());
        return fails == 0 ? 0 : 1;
    }

    private static async Task Until(Func<bool> done, int seconds)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < until) await Task.Delay(200);
        await Settle();
    }

    private static async Task Settle()
    {
        for (int i = 0; i < 3; i++) await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(250);
        for (int i = 0; i < 2; i++) await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static void Shot(Window w, string path, bool dark)
    {
        if (w.Content is not FrameworkElement root) return;
        Brush background = w.Background is SolidColorBrush { Color.A: 255 } solid
            ? solid
            : new SolidColorBrush(dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));
        if (root is Panel panel) panel.Background = background;
        root.UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
