using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nacho.Core;

namespace Nacho;

/// <summary>One tile in the results list. Every text it shows is rebuilt from Loc, so a language switch is live.</summary>
public sealed class ResultItem : INotifyPropertyChanged
{
    public ResultItem(string sourcePath, string displayName)
    {
        SourcePath = sourcePath;
        DisplayName = displayName;
    }

    public string SourcePath { get; }
    public string DisplayName { get; }
    public ShrinkResult? Result { get; private set; }
    public ImageSource? Thumbnail { get; private set; }
    public ImageSource? PreviewImage { get; private set; }

    public bool IsWorking => Result is null;
    public bool IsOk => Result?.Kind == ResultKind.Shrunk;
    public bool IsKept => Result?.Kind == ResultKind.Kept;
    public bool IsError => Result?.Kind == ResultKind.Error;
    public bool CanDeliver => IsOk || IsKept;
    public string? DeliverPath => Result?.DeliverPath;

    /// <summary>A Word document (.docx): every picture inside was shrunk, so it goes to Drive, not into a Doc.</summary>
    public bool IsDocument => Result?.IsDocument == true;

    /// <summary>A PDF: its pictures were shrunk into a smaller PDF.</summary>
    public bool IsPdf => Result?.Document?.Format == "PDF";

    public bool ShowDocumentIcon => IsDocument;
    public bool CanCopyPicture => CanDeliver && !IsDocument;
    public bool CanCompare => ComparePictures.CanCompare(Result);
    public string DragHint => Loc.T(IsPdf ? "Tile.PdfDragHint" : IsDocument ? "Tile.DocDragHint" : "Tile.DragHint");

    public string SizeText => IsOk
        ? Loc.F("Tile.SizeLine", Loc.Bytes(Result!.OriginalBytes), Loc.Bytes(Result.OutputBytes)) : "";

    public string SmallerText => IsOk ? Loc.F("Tile.Smaller", Result!.PercentSmaller) : "";

    public string PxText => IsOk && IsDocument ? DocumentLine(Result!.Document!) : IsOk
        ? Loc.F("Tile.PxLine", Dim(Result!.OriginalWidth, Result.OriginalHeight), Dim(Result.OutputWidth, Result.OutputHeight),
            ShapeLabel(Result.OriginalWidth, Result.OriginalHeight), Result.OutputFormat)
        : "";

    public string KeptBody => IsKept && IsDocument
        ? KeptDocumentBody(Result!.Document!)
        : IsKept
        ? Loc.F("Tile.KeptBody", Loc.Bytes(Result!.OriginalBytes), Result.SourceFormat, Dim(Result.OriginalWidth, Result.OriginalHeight))
        : "";

    public string ErrorBody => IsError ? Loc.T("Err." + Result!.Error) : "";

    public IReadOnlyList<string> Badges => Result is null
        ? Array.Empty<string>()
        : Result.Badges.Select(b => b == Badge.FromFormat ? Loc.F("Badge.FromFormat", Result.SourceFormat) : Loc.T("Badge." + b)).ToList();

    public string DragName => Loc.F(IsPdf ? "Tile.PdfDragName" : IsDocument ? "Tile.DocDragName" : "Tile.DragName", DisplayName);

    public static string DocumentLine(DocumentReport d) => d.Format == "PDF"
        ? Loc.F("Tile.PdfLine", d.Shrunk, d.Pictures + d.Skipped, d.Pages) + (d.Skipped > 0 ? " · " + Loc.F("Tile.PdfSkipped", d.Skipped) : "")
        : Loc.F("Tile.DocLine", d.Shrunk, d.Pictures) + (d.Skipped > 0 ? " · " + Loc.F("Tile.DocSkipped", d.Skipped) : "");

    public static string KeptDocumentBody(DocumentReport d)
    {
        if (d.Format != "PDF") return d.Pictures == 0 ? Loc.T("Tile.DocNoPictures") : Loc.F("Tile.DocKeptBody", d.Pictures);
        if (d.CheckFailed) return Loc.T("Tile.PdfCheckFailed");
        if (d.Pictures == 0) return Loc.T(d.Skipped > 0 ? "Tile.PdfOnlySkipped" : "Tile.DocNoPictures");
        return Loc.F("Tile.PdfKeptBody", d.Dpi);
    }

    public void SetResult(ShrinkResult result, ImageSource? thumbnail, ImageSource? preview)
    {
        Result = result;
        Thumbnail = thumbnail;
        PreviewImage = preview;
        Refresh();
    }

    public void Reset()
    {
        Result = null;
        Thumbnail = null;
        PreviewImage = null;
        Refresh();
    }

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    public override string ToString() => DisplayName;

    public event PropertyChangedEventHandler? PropertyChanged;

    public static string Dim(int w, int h) => $"{w} × {h}";

    public static string ShapeLabel(int w, int h) => Shape.Label(w, h, Loc.Instance.Culture);

    /// <summary>"3:4, exact", or "16:9, within 0.04%" when whole pixels can't hit the ratio exactly.</summary>
    public static string ShapeDescription(ShrinkResult r)
    {
        string label = ShapeLabel(r.OriginalWidth, r.OriginalHeight);
        if (r.ShapeExact) return Loc.F("Shape.Exact", label);
        double pct = Math.Max(0.01, Math.Round(r.ShapeErrorPercent, 2));
        return Loc.F("Shape.Within", label, pct.ToString("0.##", Loc.Instance.Culture));
    }
}

public static class Images
{
    /// <summary>Loads a picture without keeping the file open, so copies can be deleted or replaced.</summary>
    public static ImageSource? Load(string path, int decodeWidth)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
            bi.StreamSource = fs;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }
}
