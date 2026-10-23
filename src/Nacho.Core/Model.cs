namespace Nacho.Core;

public enum FormatMode { Auto, Jpeg, Png }

public enum ResultKind { Shrunk, Kept, Error }

public enum ErrorKind { None, NotFound, Pdf, NotAnImage, Damaged }

public enum Badge { FromFormat, LocationRemoved, CameraRemoved, RotatedUpright, TextKeptSharp, Colours256, OverBudget, Animated, Transparency }

public sealed class Preset
{
    public string Id { get; set; } = "balanced";
    public int LongEdge { get; set; } = 1280;
    public int BudgetKB { get; set; } = 300;

    public Preset() { }

    public Preset(string id, int longEdge, int budgetKB)
    {
        Id = id;
        LongEdge = longEdge;
        BudgetKB = budgetKB;
    }

    public Preset Clone() => new(Id, LongEdge, BudgetKB);

    public long BudgetBytes => (long)BudgetKB * 1024;

    public static List<Preset> Defaults() => new()
    {
        new Preset("small", 800, 150),
        new Preset("balanced", 1280, 300),
        new Preset("sharp", 1920, 600),
    };
}

public sealed class ShrinkOptions
{
    public FormatMode Format { get; set; } = FormatMode.Auto;
    public bool RemoveLocation { get; set; } = true;
    public bool RemoveCamera { get; set; } = true;
    public string OutputFolder { get; set; } = "";
    public string Suffix { get; set; } = "-docs";
}

public sealed class ShrinkResult
{
    public string SourcePath { get; init; } = "";
    public ResultKind Kind { get; set; }
    public ErrorKind Error { get; set; }
    public string? OutputPath { get; set; }
    public long OriginalBytes { get; set; }
    public long OutputBytes { get; set; }
    public int OriginalWidth { get; set; }
    public int OriginalHeight { get; set; }
    public int OutputWidth { get; set; }
    public int OutputHeight { get; set; }
    public string SourceFormat { get; set; } = "";
    public string OutputFormat { get; set; } = "";
    public bool OriginalHadLocation { get; set; }
    public bool OutputHasLocation { get; set; }
    public List<Badge> Badges { get; } = new();
    public string? ErrorDetail { get; set; }

    /// <summary>True when the copy has exactly the original's width-to-height ratio.</summary>
    public bool ShapeExact => OutputWidth > 0 && Shape.Same(OriginalWidth, OriginalHeight, OutputWidth, OutputHeight);

    /// <summary>How far the copy's shape is from the original's, in percent (0 when exact).</summary>
    public double ShapeErrorPercent => 100 * Shape.Error(OriginalWidth, OriginalHeight, OutputWidth, OutputHeight);

    /// <summary>The file to hand over: the new copy, or the original when it was kept.</summary>
    public string? DeliverPath => Kind switch
    {
        ResultKind.Shrunk => OutputPath,
        ResultKind.Kept => SourcePath,
        _ => null,
    };

    public int PercentSmaller => OriginalBytes <= 0 || Kind != ResultKind.Shrunk
        ? 0
        : (int)Math.Round(100.0 * (OriginalBytes - OutputBytes) / OriginalBytes);
}
