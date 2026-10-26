using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nacho.Core;
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
        "N.Divider", "N.Layer", "N.Accent", "N.Success", "N.Critical",
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
        await w.AddFilesAsync(samples.All);
        await w.WaitIdleAsync();
        await Settle();

        var byName = w.Items.ToDictionary(i => Path.GetFileName(i.SourcePath));
        ResultItem Get(string path) => byName[Path.GetFileName(path)];
        Check(w.Items.Count == 9, "9 tiles in the list");
        Check(w.Items.All(i => !i.IsWorking), "every tile finished");
        Check(Get(samples.Photo).Result?.Kind == ResultKind.Shrunk && Get(samples.Photo).Result?.OutputFormat == "JPEG", "phone photo shrunk to JPEG");
        Check(Get(samples.Photo).Badges.Contains("Location removed"), "phone photo shows Location removed");
        Check(Get(samples.Screenshot).Result?.OutputFormat == "PNG", "screenshot stays PNG");
        Check(Get(samples.Logo).IsKept, "small logo kept as it was");
        Check(Get(samples.Alpha).Result?.OutputFormat == "PNG", "transparent image stays PNG");
        Check(Get(samples.Animated).Result?.OutputFormat == "GIF", "animated GIF stays GIF");
        Check(Get(samples.WebP).IsOk && Limits.IsDocsFormat(Get(samples.WebP).Result!.OutputFormat), "WebP converted");
        Check(Get(samples.Pdf).Result?.Error == ErrorKind.Pdf, "PDF gets the PDF message");
        Check(Get(samples.Broken).IsError, "broken file gets an error tile");
        Check(w.StatusMessage.Contains("shrunk", StringComparison.Ordinal), "status bar shows the summary: " + w.StatusMessage);
        Check(w.Items.Count(i => i.IsOk && File.Exists(i.Result!.OutputPath)) == 6, "6 new copies in Docs-Ready");
        Check(w.Items.Where(i => i.CanDeliver).All(i => i.Thumbnail is not null), "every copy has a thumbnail");

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
        Check(w.AddText.Text == "Añadir imágenes", "button text follows the language live: " + w.AddText.Text);
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

        foreach (string lang in Strings.Languages)
        {
            Loc.Instance.Set(lang);
            var o = new OptionsWindow(settings) { WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(0, 0) };
            o.Show();
            await Settle();
            Shot(o, Path.Combine(outDir, $"options-{lang}.png"));
            o.Close();
        }
        foreach (string lang in new[] { "es", "hu" })
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
        foreach (string shot in new[] { "main-en-light", "main-es-light", "main-hu-light", "main-es-dark", "main-en-narrow", "options-en", "options-es", "options-hu", "about-es", "about-hu" })
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
