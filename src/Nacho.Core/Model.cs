namespace Nacho.Core;

public enum FormatMode { Auto, Jpeg, Png }

public enum ResultKind { Shrunk, Kept, Error }

public enum ErrorKind { None, NotFound, NotAnImage, Damaged, DocLocked, PdfLocked, PdfSigned, PdfEngine }

public enum Badge { FromFormat, LocationRemoved, CameraRemoved, RotatedUpright, TextKeptSharp, Colours256, OverBudget, Animated, Transparency, FullColour }

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

    /// <summary>The sharpness pictures inside a PDF are kept at: Small 100, Balanced 150, Sharp 220 dots per inch.</summary>
    public int PdfDpi => Id switch { "small" => 100, "sharp" => 220, _ => 150 };

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

    /// <summary>How close the 256-colour version came to the full-colour picture, in dB, when it was tried.</summary>
    public double? PalettePsnr { get; set; }

    /// <summary>The PNG's size before oxipng, when oxipng made it smaller.</summary>
    public long? PngOptimisedFrom { get; set; }

    /// <summary>Set for a Word document (.docx): what happened to the pictures inside it.</summary>
    public DocumentReport? Document { get; set; }

    public bool IsDocument => Document is not null;

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

/// <summary>What NACHO did to the pictures inside one .docx or PDF.</summary>
public sealed class DocumentReport
{
    /// <summary>"DOCX" or "PDF".</summary>
    public string Format { get; set; } = "DOCX";

    /// <summary>Pages in a PDF (0 for a .docx).</summary>
    public int Pages { get; set; }

    /// <summary>PDF only: a smaller version was made but did not pass the page-by-page check, so the original was kept.</summary>
    public bool CheckFailed { get; set; }

    /// <summary>PDF only: the target sharpness in dots per inch.</summary>
    public int Dpi { get; set; }

    /// <summary>Pictures NACHO could read (photos, screenshots, GIFs).</summary>
    public int Pictures { get; set; }

    public int Shrunk { get; set; }

    /// <summary>Pictures that were already Docs-ready and stay byte for byte as they were.</summary>
    public int Kept { get; set; }

    /// <summary>Drawings (EMF, WMF, SVG), pictures NACHO can't read, and in a PDF pictures with transparency, print
    /// colours (CMYK), black-and-white scans or inside drawing groups; they stay as they were.</summary>
    public int Skipped { get; set; }

    /// <summary>Bytes of the readable pictures before and after.</summary>
    public long PicturesBefore { get; set; }

    public long PicturesAfter { get; set; }

    /// <summary>Pictures whose location (GPS) data was removed.</summary>
    public int LocationRemoved { get; set; }
}
