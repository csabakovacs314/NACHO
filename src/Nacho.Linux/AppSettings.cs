using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nacho.Core;
using Nacho.Core.Linux;

namespace Nacho.Linux;

/// <summary>Settings in ~/.config/nacho/settings.json. Same fields as on Windows, plus the app menu switch.</summary>
public sealed class AppSettings
{
    // Kept the same as on Windows, so a file name works on both systems and in Google Drive.
    private const string BadNameChars = "\\/:*?\"<>|";

    public string Language { get; set; } = "";
    public string CurrentPreset { get; set; } = "balanced";
    public List<Preset> Presets { get; set; } = Preset.Defaults();
    public FormatMode Format { get; set; } = FormatMode.Auto;
    public bool RemoveLocation { get; set; } = true;
    public bool RemoveCamera { get; set; } = true;
    public string OutputFolder { get; set; } = "";
    public string Suffix { get; set; } = "-docs";
    public bool ContextMenu { get; set; }
    public bool AppMenu { get; set; }
    public bool CheckUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }
    public string PageSize { get; set; } = "";
    public string SavingsMonth { get; set; } = "";
    public long SavingsBytes { get; set; }

    /// <summary>Google Docs in place: keep copies of the original pictures in Docs-Ready.</summary>
    public bool GoogleKeepOriginals { get; set; } = true;

    /// <summary>The user's own Google client file, or "" for the client built into NACHO.</summary>
    public string GoogleClientFile { get; set; } = "";

    /// <summary>Who is signed in, for display only (the sign-in itself is kept in a private file).</summary>
    public string GoogleEmail { get; set; } = "";

    /// <summary>Where the smaller pictures go: a new copy of the Doc (true, the safe default) or the Doc itself.</summary>
    public bool GoogleIntoCopy { get; set; } = true;

    /// <summary>The last Docs NACHO shrank, newest first, for the "Recently shrunk" list.</summary>
    public List<Nacho.Core.Google.GoogleRecent> GoogleRecent { get; set; } = new();

    /// <summary>The self-test points this at its own folder so it never touches the real settings.</summary>
    public static string? PathOverride { get; set; }

    public static string FilePath => PathOverride ?? XdgPaths.Current.SettingsFile;

    public static string DefaultFolder => Path.Combine(XdgPaths.Current.Pictures, "Docs-Ready");

    public static bool HasBadNameChars(string s) => s.IndexOfAny(BadNameChars.ToCharArray()) >= 0 || s.Any(char.IsControl);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        AppSettings s;
        try
        {
            s = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            s = new AppSettings();
        }
        s.Normalise();
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch
        {
            // Settings are a convenience; a failed save must never stop the work.
        }
    }

    public void Normalise()
    {
        // Only well-formed Doc IDs come back from the file: the list opens them in the browser.
        GoogleRecent = (GoogleRecent ?? new())
            .Where(r => r is not null && !string.IsNullOrEmpty(r.DocId)
                && Nacho.Core.Google.GoogleDocLink.TryGetId(Nacho.Core.Google.GoogleDocLink.EditUrl(r.DocId)) == r.DocId)
            .Take(Nacho.Core.Google.GoogleRecent.Keep).ToList();
        if (!Strings.Languages.Contains(Language)) Language = XdgPaths.Current.UiLanguage;
        var ids = new[] { "small", "balanced", "sharp" };
        if (Presets is null || Presets.Count != 3 || !ids.All(id => Presets.Any(p => p.Id == id)))
        {
            Presets = Preset.Defaults();
        }
        Presets = ids.Select(id => Presets.First(p => p.Id == id)).ToList();
        foreach (var p in Presets)
        {
            p.LongEdge = Math.Clamp(p.LongEdge, 320, 8000);
            p.BudgetKB = Math.Clamp(p.BudgetKB, 30, 20000);
        }
        if (!ids.Contains(CurrentPreset)) CurrentPreset = "balanced";
        if (string.IsNullOrWhiteSpace(OutputFolder)) OutputFolder = DefaultFolder;
        if (string.IsNullOrWhiteSpace(Suffix) || HasBadNameChars(Suffix)) Suffix = "-docs";
        if (PageSize is not ("A4" or "Letter")) PageSize = UsesLetter() ? "Letter" : "A4";
    }

    /// <summary>Letter in the US and Canada (from LC_PAPER or LANG), A4 everywhere else.</summary>
    private static bool UsesLetter()
    {
        string locale = Environment.GetEnvironmentVariable("LC_PAPER") ?? Environment.GetEnvironmentVariable("LANG") ?? "";
        string region = locale.Split('.', '@')[0].Split('_').ElementAtOrDefault(1) ?? "";
        return region is "US" or "CA";
    }

    public Preset GetPreset() => Presets.First(p => p.Id == CurrentPreset);

    public ShrinkOptions ToOptions() => new()
    {
        Format = Format,
        RemoveLocation = RemoveLocation,
        RemoveCamera = RemoveCamera,
        OutputFolder = OutputFolder,
        Suffix = Suffix,
    };

    private static string ThisMonth => DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public void AddSavings(long bytes)
    {
        if (SavingsMonth != ThisMonth)
        {
            SavingsMonth = ThisMonth;
            SavingsBytes = 0;
        }
        SavingsBytes = Math.Max(0, SavingsBytes + bytes);
    }

    public long MonthSavings() => SavingsMonth == ThisMonth ? SavingsBytes : 0;
}
