using System.Diagnostics;
using Nacho.Core.Linux;

namespace Nacho.Linux;

/// <summary>NACHO's place in the desktop: menus, the file manager, opening files.</summary>
public static class Shell
{
    /// <summary>The self-test points this at its own folder so it never touches the real desktop.</summary>
    public static DesktopTargets? TargetsOverride { get; set; }

    public static DesktopTargets Targets => TargetsOverride ?? DesktopTargets.FromXdg(XdgPaths.Current);

    /// <summary>What the menus start: the AppImage file when running from one, else this program.</summary>
    public static string ExecPath
    {
        get
        {
            string? appImage = Environment.GetEnvironmentVariable("APPIMAGE");
            if (!string.IsNullOrEmpty(appImage) && File.Exists(appImage)) return appImage;
            return Environment.ProcessPath ?? "nacho";
        }
    }

    public static bool IsContextMenuOn() => DesktopFiles.IsContextMenuOn(Targets);

    public static bool IsAppMenuOn() => DesktopFiles.IsAppMenuOn(Targets);

    public static IReadOnlyList<string> Apply(bool contextMenu, bool appMenu, string menuText)
    {
        var t = Targets;
        var written = DesktopFiles.Apply(t, contextMenu, appMenu, ExecPath, menuText, App.AssetBytes("nacho.svg"), App.AssetBytes("nacho.png"));
        if (TargetsOverride is null)
        {
            // Lets menus pick up the change at once; harmless when the tool is missing.
            Run("update-desktop-database", Path.GetDirectoryName(t.AppEntry)!);
        }
        return written;
    }

    /// <summary>Selects the file in the file manager (Files, Dolphin, Nemo, Caja), else opens its folder.</summary>
    public static bool ShowInFolder(string path)
    {
        string uri = new Uri(path).AbsoluteUri;
        int code = Run("dbus-send", "--session", "--print-reply", "--dest=org.freedesktop.FileManager1", "--type=method_call",
            "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1.ShowItems", "array:string:" + uri, "string:");
        return code == 0 || Open(Path.GetDirectoryName(path)!);
    }

    public static bool Open(string path) => Start("xdg-open", path);

    /// <summary>Runs a tool and waits up to 5 seconds. Returns its exit code, or -1 when it could not run.</summary>
    public static int Run(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return -1;
            p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEndAsync();
            return p.WaitForExit(5000) ? p.ExitCode : -1;
        }
        catch
        {
            return -1;
        }
    }

    private static bool Start(string file, string arg)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false };
            psi.ArgumentList.Add(arg);
            using var p = Process.Start(psi);
            return p is not null;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return false;
        }
    }
}
