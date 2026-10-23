using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nacho.Core;

namespace Nacho;

public sealed class AppSettings
{
    public string Language { get; set; } = "";
    public string CurrentPreset { get; set; } = "balanced";
    public List<Preset> Presets { get; set; } = Preset.Defaults();
    public FormatMode Format { get; set; } = FormatMode.Auto;
    public bool RemoveLocation { get; set; } = true;
    public bool RemoveCamera { get; set; } = true;
    public string OutputFolder { get; set; } = "";
    public string Suffix { get; set; } = "-docs";
    public bool ContextMenu { get; set; }
    public bool SendTo { get; set; }
    public string PageSize { get; set; } = "";
    public string SavingsMonth { get; set; } = "";
    public long SavingsBytes { get; set; }

    /// <summary>The self-test points this at its own folder so it never touches the real settings.</summary>
    public static string? PathOverride { get; set; }

    public static string FilePath => PathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NACHO", "settings.json");

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Docs-Ready");

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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // Settings are a convenience; a failed save must never stop the work.
        }
    }

    public void Normalise()
    {
        if (Language is not ("en" or "es"))
        {
            Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";
        }
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
        if (string.IsNullOrWhiteSpace(Suffix) || Suffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) Suffix = "-docs";
        if (PageSize is not ("A4" or "Letter")) PageSize = RegionInfo.CurrentRegion.IsMetric ? "A4" : "Letter";
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
