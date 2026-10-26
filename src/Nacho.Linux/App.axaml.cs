using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Nacho.Core.Linux;

namespace Nacho.Linux;

public partial class App : Application
{
    public static string[] Args { get; set; } = Array.Empty<string>();

    public static string Version
    {
        get
        {
            var v = typeof(App).Assembly.GetName().Version ?? new Version(0, 4, 0);
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        UnpackTools();
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Log(e.Exception);
            e.Handled = true;
        };

        if (Args.Length > 0 && Args[0] == "--selftest")
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnFrameworkInitializationCompleted();
            int code;
            try
            {
                code = await SelfTest.RunAsync(Args.Length > 1 ? Args[1] : Path.Combine(Path.GetTempPath(), "nacho-selftest"));
            }
            catch (Exception ex)
            {
                Log(ex);
                Console.Error.WriteLine(ex);
                code = 3;
            }
            desktop.Shutdown(code);
            return;
        }

        var settings = AppSettings.Load();
        // The install script and the uninstall script change these too, so the files are the truth.
        settings.ContextMenu = Shell.IsContextMenuOn();
        settings.AppMenu = Shell.IsAppMenuOn();
        Loc.Instance.Set(settings.Language);

        var window = new MainWindow(settings);
        desktop.MainWindow = window;
        base.OnFrameworkInitializationCompleted();

        var files = Args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (files.Length > 0) window.ReceiveExternal(files);
        SingleInstance.StartServer(paths => Dispatcher.UIThread.Post(() => window.ReceiveExternal(paths)));
        window.Opened += async (_, _) =>
        {
            try
            {
                await window.CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        };
    }

    /// <summary>Follows the desktop's light or dark setting, or forces one (the self-test does).</summary>
    public static void ApplyTheme(string mode)
    {
        if (Current is null) return;
        Current.RequestedThemeVariant = mode switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    public static Stream? Asset(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

    public static byte[]? AssetBytes(string name)
    {
        using var s = Asset(name);
        if (s is null) return null;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public static WindowIcon? Icon()
    {
        try
        {
            using var s = Asset("nacho.png");
            return s is null ? null : new WindowIcon(s);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Unpacks the bundled oxipng to ~/.cache/nacho/tools (once per version) for smaller PNGs.</summary>
    public static void UnpackTools()
    {
        try
        {
            using var s = Asset("oxipng");
            if (s is null) return;
            string cache = Path.Combine(XdgPaths.Current.CacheHome, "nacho", "tools");
            Nacho.Core.PngOptimizer.ToolPath = Nacho.Core.PngOptimizer.Extract(s, cache, "oxipng");
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    public static void Log(Exception ex)
    {
        try
        {
            string file = XdgPaths.Current.LogFile;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file, $"{DateTime.Now:s} {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
