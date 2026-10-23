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

    public string SizeText => IsOk
        ? Loc.F("Tile.SizeLine", Loc.Bytes(Result!.OriginalBytes), Loc.Bytes(Result.OutputBytes)) : "";

    public string SmallerText => IsOk ? Loc.F("Tile.Smaller", Result!.PercentSmaller) : "";

    public string PxText => IsOk
        ? Loc.F("Tile.PxLine", Dim(Result!.OriginalWidth, Result.OriginalHeight), Dim(Result.OutputWidth, Result.OutputHeight), Result.OutputFormat)
        : "";

    public string KeptBody => IsKept
        ? Loc.F("Tile.KeptBody", Loc.Bytes(Result!.OriginalBytes), Result.SourceFormat, Dim(Result.OriginalWidth, Result.OriginalHeight))
        : "";

    public string ErrorBody => IsError ? Loc.T("Err." + Result!.Error) : "";

    public IReadOnlyList<string> Badges => Result is null
        ? Array.Empty<string>()
        : Result.Badges.Select(b => b == Badge.FromFormat ? Loc.F("Badge.FromFormat", Result.SourceFormat) : Loc.T("Badge." + b)).ToList();

    public string DragName => Loc.F("Tile.DragName", DisplayName);

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
