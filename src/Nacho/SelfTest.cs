using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nacho.Core;

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
        "LayerFillColorDefaultBrush", "AccentButtonStyle",
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

        Check(w.Items.Where(i => i.IsOk).All(i => i.Result!.ShapeErrorPercent <= 0.1), "every copy keeps the original shape");
        Check(Get(samples.Photo).Result!.ShapeExact, "phone photo shape exact: " + ResultItem.Dim(Get(samples.Photo).Result!.OutputWidth, Get(samples.Photo).Result!.OutputHeight));

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

        foreach (string lang in Strings.Languages)
        {
            Loc.Instance.Set(lang);
            var o = new OptionsWindow(settings) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0 };
            o.Show();
            await Settle();
            Shot(o, Path.Combine(outDir, $"options-{lang}.png"), dark: false);
            o.Close();
        }
        foreach (string lang in new[] { "es", "hu" })
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
        foreach (string shot in new[] { "main-en-light", "main-es-light", "main-hu-light", "main-es-dark", "main-en-narrow", "options-en", "options-es", "options-hu", "about-es", "about-hu" })
        {
            var f = new FileInfo(Path.Combine(outDir, shot + ".png"));
            Check(f.Exists && f.Length > 10_000, $"screenshot {shot}.png saved");
        }

        log.AppendLine();
        log.AppendLine(fails == 0 ? "ALL PASSED" : $"{fails} FAILED");
        await File.WriteAllTextAsync(Path.Combine(outDir, "selftest-report.txt"), log.ToString());
        return fails == 0 ? 0 : 1;
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
