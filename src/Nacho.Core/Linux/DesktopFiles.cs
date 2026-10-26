using System.Text;

namespace Nacho.Core.Linux;

/// <summary>Where NACHO's desktop files go, for one user. Tests point this at a temporary folder.</summary>
public sealed record DesktopTargets(string DataHome, string ConfigHome)
{
    public static DesktopTargets FromXdg(XdgPaths xdg) => new(xdg.DataHome, xdg.ConfigHome);

    public string AppEntry => Path.Combine(DataHome, "applications", "nacho.desktop");
    public string IconSvg => Path.Combine(DataHome, "icons", "hicolor", "scalable", "apps", "nacho.svg");
    public string IconPng => Path.Combine(DataHome, "icons", "hicolor", "256x256", "apps", "nacho.png");

    /// <summary>GNOME Files (Nautilus): Scripts submenu; the file name is the menu text.</summary>
    public string NautilusScripts => Path.Combine(DataHome, "nautilus", "scripts");

    /// <summary>MATE Caja: Scripts submenu, same idea as Nautilus.</summary>
    public string CajaScripts => Path.Combine(ConfigHome, "caja", "scripts");

    /// <summary>Cinnamon Nemo: an action, shown directly in the right-click menu.</summary>
    public string NemoAction => Path.Combine(DataHome, "nemo", "actions", "nacho.nemo_action");

    /// <summary>KDE Dolphin 6 (Plasma 6) service menu; it must be executable.</summary>
    public string DolphinMenu => Path.Combine(DataHome, "kio", "servicemenus", "nacho.desktop");

    /// <summary>KDE Dolphin 5 (Plasma 5) service menu.</summary>
    public string DolphinMenu5 => Path.Combine(DataHome, "kservices5", "ServiceMenus", "nacho.desktop");
}

/// <summary>
/// Writes and removes NACHO's app menu entry (with Open With) and the right-click entries for
/// Files, Caja, Nemo and Dolphin. Every file carries a marker, and only marked files are removed.
/// </summary>
public static class DesktopFiles
{
    public const string Marker = "Made by NACHO";

    public static readonly string[] MimeTypes =
    {
        "image/jpeg", "image/png", "image/gif", "image/heic", "image/heif", "image/webp", "image/avif", "image/tiff", "image/bmp",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };

    private const UnixFileMode Executable =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    // ---------- quoting ----------

    /// <summary>
    /// One argument for a desktop entry Exec key: quoted, with " ` $ \ escaped inside the quotes, then
    /// backslashes doubled because Exec is a string value, and % doubled because % starts a field code.
    /// </summary>
    public static string ExecArg(string path)
    {
        var inner = new StringBuilder();
        foreach (char c in path)
        {
            if (c is '"' or '`' or '$' or '\\') inner.Append('\\');
            inner.Append(c);
        }
        string quoted = "\"" + inner + "\"";
        return quoted.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal);
    }

    /// <summary>One argument for sh: single quotes, with ' written as '\''.</summary>
    public static string ShellArg(string s) => "'" + s.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    /// <summary>A desktop entry string value: no line breaks, and \ written as \\.</summary>
    private static string Value(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal);

    /// <summary>A safe file name for a script whose name is the menu text.</summary>
    public static string ScriptName(string menuText)
    {
        string name = new(menuText.Where(c => c != '/' && c != '\0' && !char.IsControl(c)).ToArray());
        name = name.Trim().TrimStart('.');
        return name.Length == 0 ? "NACHO" : name;
    }

    // ---------- file contents ----------

    /// <summary>The app menu entry. MimeType puts NACHO under Open With for images; it does not make NACHO the default.</summary>
    public static string AppEntryText(string exec)
    {
        var sb = new StringBuilder();
        sb.Append("[Desktop Entry]\n");
        sb.Append("# ").Append(Marker).Append(". Turn it off in NACHO > More options.\n");
        sb.Append("Type=Application\n");
        sb.Append("Version=1.5\n");
        sb.Append("Name=NACHO\n");
        foreach (string lang in Strings.Languages)
        {
            string key = lang == "en" ? "GenericName" : $"GenericName[{lang}]";
            sb.Append(key).Append('=').Append(Value(Strings.For(lang)["App.Tagline"])).Append('\n');
        }
        foreach (string lang in Strings.Languages)
        {
            string key = lang == "en" ? "Comment" : $"Comment[{lang}]";
            sb.Append(key).Append('=').Append(Value(Strings.For(lang)["App.Expansion"])).Append('\n');
        }
        sb.Append("Exec=").Append(ExecArg(exec)).Append(" %F\n");
        sb.Append("TryExec=").Append(Value(exec)).Append('\n');
        sb.Append("Icon=nacho\n");
        sb.Append("Terminal=false\n");
        sb.Append("Categories=Graphics;2DGraphics;RasterGraphics;\n");
        sb.Append("Keywords=image;photo;resize;shrink;compress;docs;\n");
        sb.Append("MimeType=").Append(string.Join(";", MimeTypes)).Append(";\n");
        sb.Append("StartupNotify=true\n");
        return sb.ToString();
    }

    /// <summary>A script for Files (Nautilus) and Caja: both pass the selected local files as arguments.</summary>
    public static string ScriptText(string exec) =>
        "#!/bin/sh\n" +
        "# " + Marker + ": sends the selected images to NACHO. Turn it off in NACHO > More options.\n" +
        "exec " + ShellArg(exec) + " \"$@\"\n";

    public static string NemoActionText(string exec, string menuText) =>
        "[Nemo Action]\n" +
        "# " + Marker + ". Turn it off in NACHO > More options.\n" +
        "Active=true\n" +
        "Name=" + Value(menuText).Replace("_", "__", StringComparison.Ordinal) + "\n" +
        "Comment=NACHO\n" +
        "Exec=" + ShellArg(exec) + " %F\n" +
        "Icon-Name=nacho\n" +
        "Selection=notnone\n" +
        "Mimetypes=" + string.Join(";", MimeTypes) + ";\n";

    public static string DolphinMenuText(string exec)
    {
        var sb = new StringBuilder();
        sb.Append("[Desktop Entry]\n");
        sb.Append("# ").Append(Marker).Append(". Turn it off in NACHO > More options.\n");
        sb.Append("Type=Service\n");
        sb.Append("X-KDE-ServiceTypes=KonqPopupMenu/Plugin\n");
        sb.Append("MimeType=").Append(string.Join(";", MimeTypes)).Append(";\n");
        sb.Append("Actions=nachoDocsReady;\n");
        sb.Append("X-KDE-Priority=TopLevel\n");
        sb.Append('\n');
        sb.Append("[Desktop Action nachoDocsReady]\n");
        foreach (string lang in Strings.Languages)
        {
            string key = lang == "en" ? "Name" : $"Name[{lang}]";
            sb.Append(key).Append('=').Append(Value(Strings.For(lang)["Shell.MenuText"])).Append('\n');
        }
        sb.Append("Icon=nacho\n");
        sb.Append("Exec=").Append(ExecArg(exec)).Append(" %F\n");
        return sb.ToString();
    }

    // ---------- state ----------

    public static bool IsAppMenuOn(DesktopTargets t) => IsOurs(t.AppEntry);

    public static bool IsContextMenuOn(DesktopTargets t) =>
        IsOurs(t.NemoAction) || IsOurs(t.DolphinMenu) || IsOurs(t.DolphinMenu5) ||
        OurScripts(t.NautilusScripts).Any() || OurScripts(t.CajaScripts).Any();

    /// <summary>The program a NACHO desktop file points at, read back from its Exec or exec line.</summary>
    public static string? AppEntryExec(DesktopTargets t)
    {
        if (!IsOurs(t.AppEntry)) return null;
        string? line = File.ReadLines(t.AppEntry).FirstOrDefault(l => l.StartsWith("TryExec=", StringComparison.Ordinal));
        return line?["TryExec=".Length..].Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    // ---------- applying ----------

    /// <summary>
    /// Makes the files match the two switches. Every write replaces what NACHO wrote before, so a new
    /// language or a moved program is picked up. Returns the files written.
    /// </summary>
    public static IReadOnlyList<string> Apply(DesktopTargets t, bool contextMenu, bool appMenu, string exec, string menuText,
        byte[]? iconSvg, byte[]? iconPng)
    {
        var written = new List<string>();
        RemoveContextMenu(t);
        if (contextMenu)
        {
            string name = ScriptName(menuText);
            foreach (string dir in new[] { t.NautilusScripts, t.CajaScripts })
            {
                string path = Path.Combine(dir, name);
                Write(path, ScriptText(exec), executable: true);
                written.Add(path);
            }
            Write(t.NemoAction, NemoActionText(exec, menuText), executable: false);
            written.Add(t.NemoAction);
            Write(t.DolphinMenu, DolphinMenuText(exec), executable: true);
            written.Add(t.DolphinMenu);
            Write(t.DolphinMenu5, DolphinMenuText(exec), executable: true);
            written.Add(t.DolphinMenu5);
        }

        if (appMenu)
        {
            Write(t.AppEntry, AppEntryText(exec), executable: false);
            written.Add(t.AppEntry);
        }
        else
        {
            DeleteIfOurs(t.AppEntry);
        }

        if (contextMenu || appMenu)
        {
            if (iconSvg is not null) { WriteBytes(t.IconSvg, iconSvg); written.Add(t.IconSvg); }
            if (iconPng is not null) { WriteBytes(t.IconPng, iconPng); written.Add(t.IconPng); }
        }
        else
        {
            // The icons carry no marker; they are only removed when nothing of NACHO's is left that uses them.
            TryDelete(t.IconSvg);
            TryDelete(t.IconPng);
        }
        return written;
    }

    public static void RemoveAll(DesktopTargets t) => Apply(t, false, false, "", "", null, null);

    private static void RemoveContextMenu(DesktopTargets t)
    {
        foreach (string script in OurScripts(t.NautilusScripts).Concat(OurScripts(t.CajaScripts)).ToList()) TryDelete(script);
        DeleteIfOurs(t.NemoAction);
        DeleteIfOurs(t.DolphinMenu);
        DeleteIfOurs(t.DolphinMenu5);
    }

    private static IEnumerable<string> OurScripts(string dir)
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (string f in Directory.EnumerateFiles(dir))
        {
            if (IsOurs(f)) yield return f;
        }
    }

    private static bool IsOurs(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024) return false;
            return File.ReadLines(path).Take(3).Any(l => l.Contains(Marker, StringComparison.Ordinal));
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void DeleteIfOurs(string path)
    {
        if (IsOurs(path)) TryDelete(path);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void Write(string path, string text, bool executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".nacho-tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, executable ? Executable : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
        File.Move(temp, path, overwrite: true);
    }

    private static void WriteBytes(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
