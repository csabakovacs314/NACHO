namespace Nacho.Core.Linux;

/// <summary>
/// The freedesktop.org (XDG) folders NACHO uses on Linux. Everything is per user: nothing needs root.
/// The environment is passed in, so tests can point it at a temporary home.
/// </summary>
public sealed class XdgPaths
{
    private readonly Func<string, string?> _env;

    public XdgPaths(Func<string, string?> env) => _env = env;

    public static XdgPaths Current { get; } = new(Environment.GetEnvironmentVariable);

    public string Home => NonEmpty(_env("HOME")) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>$XDG_CONFIG_HOME, or ~/.config. Settings live in its nacho folder.</summary>
    public string ConfigHome => Absolute(_env("XDG_CONFIG_HOME")) ?? Path.Combine(Home, ".config");

    /// <summary>$XDG_DATA_HOME, or ~/.local/share. App menu entries, icons and file manager menus live here.</summary>
    public string DataHome => Absolute(_env("XDG_DATA_HOME")) ?? Path.Combine(Home, ".local", "share");

    /// <summary>$XDG_CACHE_HOME, or ~/.cache. Bundled tools are unpacked into its nacho folder.</summary>
    public string CacheHome => Absolute(_env("XDG_CACHE_HOME")) ?? Path.Combine(Home, ".cache");

    /// <summary>$XDG_RUNTIME_DIR, or the temp folder. The single-instance socket lives here.</summary>
    public string RuntimeDir => Absolute(_env("XDG_RUNTIME_DIR")) ?? Path.GetTempPath();

    public string SettingsFile => Path.Combine(ConfigHome, "nacho", "settings.json");

    public string LogFile => Path.Combine(ConfigHome, "nacho", "nacho.log");

    /// <summary>The Pictures folder from user-dirs.dirs (it is translated on many desktops), else ~/Pictures.</summary>
    public string Pictures
    {
        get
        {
            string file = Path.Combine(ConfigHome, "user-dirs.dirs");
            try
            {
                if (File.Exists(file))
                {
                    foreach (string raw in File.ReadLines(file))
                    {
                        string line = raw.Trim();
                        if (!line.StartsWith("XDG_PICTURES_DIR=", StringComparison.Ordinal)) continue;
                        string value = line["XDG_PICTURES_DIR=".Length..].Trim().Trim('"');
                        if (value.StartsWith("$HOME", StringComparison.Ordinal)) value = Home + value[5..];
                        else if (!value.StartsWith('/')) continue;
                        // A Pictures folder set to the home folder itself means "not set".
                        if (value.TrimEnd('/') == Home.TrimEnd('/')) break;
                        return value;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return Path.Combine(Home, "Pictures");
        }
    }

    /// <summary>The UI language from LANGUAGE, LC_ALL, LC_MESSAGES or LANG, as en, es or hu (anything else is English).</summary>
    public string UiLanguage
    {
        get
        {
            foreach (string name in new[] { "LANGUAGE", "LC_ALL", "LC_MESSAGES", "LANG" })
            {
                string? value = NonEmpty(_env(name));
                if (value is null) continue;
                // LANGUAGE can be a list ("hu:en"); the first entry wins.
                string first = value.Split(':')[0];
                string code = first.Split('_', '.', '@')[0].ToLowerInvariant();
                if (code is "c" or "posix" or "") continue;
                return Strings.Normalise(code);
            }
            return "en";
        }
    }

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    // The XDG spec says relative paths in these variables are invalid and must be ignored.
    private static string? Absolute(string? s) => NonEmpty(s) is { } v && v.StartsWith('/') ? v : null;
}
