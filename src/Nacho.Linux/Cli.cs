using Nacho.Core;
using Nacho.Core.Linux;

namespace Nacho.Linux;

/// <summary>Command-line jobs for install.sh and uninstall.sh. They run without opening a window.</summary>
public static class Cli
{
    public const string Help =
        "NACHO: Docs-ready copies of images for Google Docs.\n\n" +
        "  nacho [image ...]            open the window, optionally with images\n" +
        "  nacho --setup-desktop [--lang en|es|hu] [--no-context-menu] [--no-app-menu]\n" +
        "                               add NACHO to the app menu, Open With and the file manager menu\n" +
        "  nacho --remove-desktop       remove all of that again\n" +
        "  nacho --selftest <folder>    check NACHO and save screenshots (needs a display)\n" +
        "  nacho --version";

    public static int SetupDesktop(string[] args)
    {
        bool firstRun = !File.Exists(AppSettings.FilePath);
        var settings = AppSettings.Load();
        int langAt = Array.IndexOf(args, "--lang");
        if (langAt >= 0 && langAt + 1 < args.Length && firstRun && Strings.Languages.Contains(args[langAt + 1]))
        {
            settings.Language = args[langAt + 1];
        }
        settings.ContextMenu = !args.Contains("--no-context-menu");
        settings.AppMenu = !args.Contains("--no-app-menu");
        try
        {
            var written = Shell.Apply(settings.ContextMenu, settings.AppMenu, Strings.ForPlatform(settings.Language, linux: true)["Shell.MenuText"]);
            settings.Save();
            Console.WriteLine($"NACHO {App.Version}: desktop integration for {Shell.ExecPath}");
            foreach (string f in written) Console.WriteLine("  wrote " + f);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Could not set up the desktop integration: " + ex.Message);
            return 1;
        }
    }

    public static int RemoveDesktop()
    {
        try
        {
            DesktopFiles.RemoveAll(Shell.Targets);
            Shell.Run("update-desktop-database", Path.GetDirectoryName(Shell.Targets.AppEntry)!);
            if (File.Exists(AppSettings.FilePath))
            {
                var settings = AppSettings.Load();
                settings.ContextMenu = false;
                settings.AppMenu = false;
                settings.Save();
            }
            Console.WriteLine("NACHO: removed the app menu entry, Open With and the file manager menus.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Could not remove the desktop integration: " + ex.Message);
            return 1;
        }
    }
}
