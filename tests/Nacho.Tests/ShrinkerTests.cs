using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ImageMagick;
using Nacho.Core;
using Xunit;

namespace Nacho.Tests;

public sealed class SamplesFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "nacho-tests-" + Guid.NewGuid().ToString("N")[..8]);
    public SampleImages.Set Samples { get; }

    public SamplesFixture() => Samples = SampleImages.Generate(Path.Combine(Root, "in"));

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch { /* temp folder, best effort */ }
    }
}

[Collection("Engine")]
public sealed class ShrinkerTests : IClassFixture<SamplesFixture>
{
    private static readonly Preset Small = new("small", 800, 150);
    private static readonly Preset Balanced = new("balanced", 1280, 300);
    private readonly SamplesFixture _f;
    private readonly ImageShrinker _s = new();

    public ShrinkerTests(SamplesFixture f) => _f = f;

    private ShrinkOptions Opt(string name, Action<ShrinkOptions>? tweak = null)
    {
        var o = new ShrinkOptions { OutputFolder = Path.Combine(_f.Root, "out-" + name) };
        tweak?.Invoke(o);
        return o;
    }

    [Fact]
    public void Photo_is_turned_upright_resized_stripped_and_within_budget()
    {
        var r = _s.Shrink(_f.Samples.Photo, Balanced, Opt("photo"));

        Assert.Equal(ResultKind.Shrunk, r.Kind);
        Assert.Equal("JPEG", r.OutputFormat);
        Assert.Equal(2250, r.OriginalWidth);
        Assert.Equal(3000, r.OriginalHeight);
        Assert.True(r.OutputHeight <= 1280 && r.OutputHeight > r.OutputWidth, $"{r.OutputWidth} x {r.OutputHeight}");
        Assert.InRange((double)r.OutputWidth / r.OutputHeight, 0.74, 0.76);
        Assert.True(r.OutputBytes <= Balanced.BudgetBytes, $"{r.OutputBytes} bytes");
        Assert.Contains(Badge.RotatedUpright, r.Badges);
        Assert.Contains(Badge.LocationRemoved, r.Badges);
        Assert.Contains(Badge.CameraRemoved, r.Badges);
        Assert.False(r.OutputHasLocation);

        using var o = new MagickImage(r.OutputPath!);
        Assert.Equal(r.OutputWidth, (int)o.Width);
        Assert.True(o.Orientation is Orientation.Undefined or Orientation.TopLeft);
        var exif = o.GetExifProfile();
        Assert.False(ImageShrinker.HasGps(exif));
        Assert.Null(exif?.GetValue(ExifTag.Make));
    }

    [Fact]
    public void Small_preset_fits_its_budget()
    {
        var r = _s.Shrink(_f.Samples.Photo, Small, Opt("small"));
        Assert.Equal(ResultKind.Shrunk, r.Kind);
        Assert.True(r.OutputBytes <= Small.BudgetBytes, $"{r.OutputBytes} bytes");
        Assert.True(Math.Max(r.OutputWidth, r.OutputHeight) <= 800);
    }

    [Fact]
    public void Keeping_camera_details_still_removes_location()
    {
        var r = _s.Shrink(_f.Samples.Photo, Balanced, Opt("keepcam", o => o.RemoveCamera = false));
        Assert.Equal(ResultKind.Shrunk, r.Kind);
        using var o = new MagickImage(r.OutputPath!);
        var exif = o.GetExifProfile();
        Assert.NotNull(exif);
        Assert.Equal("NachoCam", exif!.GetValue(ExifTag.Make)?.Value);
        Assert.False(ImageShrinker.HasGps(exif));
        Assert.Contains(Badge.LocationRemoved, r.Badges);
        Assert.DoesNotContain(Badge.CameraRemoved, r.Badges);
    }

    [Fact]
    public void Screenshot_stays_png_with_sharp_text()
    {
        var r = _s.Shrink(_f.Samples.Screenshot, Balanced, Opt("shot"));
        Assert.Equal(ResultKind.Shrunk, r.Kind);
        Assert.Equal("PNG", r.OutputFormat);
        Assert.Equal(1280, r.OutputWidth);
        Assert.Equal(800, r.OutputHeight);
        Assert.Contains(Badge.TextKeptSharp, r.Badges);
        Assert.True(r.OutputBytes < r.OriginalBytes);
    }

    [Fact]
    public void Docs_ready_logo_is_kept_as_it_is()
    {
        var r = _s.Shrink(_f.Samples.Logo, Balanced, Opt("logo"));
        Assert.Equal(ResultKind.Kept, r.Kind);
        Assert.Null(r.OutputPath);
        Assert.Equal(_f.Samples.Logo, r.DeliverPath);
        Assert.False(Directory.Exists(Path.Combine(_f.Root, "out-logo")) && Directory.EnumerateFiles(Path.Combine(_f.Root, "out-logo")).Any());
    }

    [Fact]
    public void Transparency_is_kept()
    {
        var r = _s.Shrink(_f.Samples.Alpha, Balanced, Opt("alpha"));
        Assert.Equal(ResultKind.Shrunk, r.Kind);
        Assert.Equal("PNG", r.OutputFormat);
        Assert.Contains(Badge.Transparency, r.Badges);
        using var o = new MagickImage(r.OutputPath!);
        Assert.True(o.HasAlpha && !o.IsOpaque);
        Assert.Equal(1280u, o.Width);
    }

    [Fact]
    public void Animated_gif_stays_animated()
    {
        var r = _s.Shrink(_f.Samples.Animated, Balanced, Opt("anim"));
        Assert.Equal(ResultKind.Shrunk, r.Kind);
        Assert.Equal("GIF", r.OutputFormat);
        Assert.Contains(Badge.Animated, r.Badges);
        using var frames = new MagickImageCollection(r.OutputPath!);
        Assert.Equal(3, frames.Count);
        Assert.Equal(1280, r.OutputWidth);
    }

    [Fact]
    public void WebP_is_converted_to_a_format_Docs_accepts()
    {
        var r = _s.Shrink(_f.Samples.WebP, Balanced, Opt("webp"));
        Assert.Equal(ResultKind.Shrunk, r.Kind);
        Assert.Equal("WEBP", r.SourceFormat);
        Assert.True(Limits.IsDocsFormat(r.OutputFormat));
        Assert.Contains(Badge.FromFormat, r.Badges);
    }

    [Fact]
    public void Small_photo_with_gps_loses_gps_but_is_not_upscaled()
    {
        var r = _s.Shrink(_f.Samples.SmallGpsPhoto, Balanced, Opt("smallgps"));
        Assert.Equal(ResultKind.Shrunk, r.Kind);
        Assert.Equal(800, r.OutputWidth);
        Assert.Equal(600, r.OutputHeight);
        using var o = new MagickImage(r.OutputPath!);
        Assert.False(ImageShrinker.HasGps(o.GetExifProfile()));
    }

    [Fact]
    public void Text_only_pdf_is_read_and_kept()
    {
        var r = _s.Shrink(_f.Samples.Pdf, Balanced, Opt("pdf"));
        Assert.Equal(ResultKind.Kept, r.Kind);
        Assert.Equal("PDF", r.Document?.Format);
    }

    [Fact]
    public void Broken_file_is_reported_not_crashed()
    {
        var r = _s.Shrink(_f.Samples.Broken, Balanced, Opt("broken"));
        Assert.Equal(ResultKind.Error, r.Kind);
        Assert.True(r.Error is ErrorKind.Damaged or ErrorKind.NotAnImage, r.Error.ToString());
    }

    [Fact]
    public void Missing_file_is_reported()
    {
        var r = _s.Shrink(Path.Combine(_f.Root, "nope.jpg"), Balanced, Opt("missing"));
        Assert.Equal(ErrorKind.NotFound, r.Error);
    }

    [Fact]
    public void Forced_formats_are_respected()
    {
        var png = _s.Shrink(_f.Samples.SmallGpsPhoto, Balanced, Opt("forcepng", o => o.Format = FormatMode.Png));
        Assert.Equal("PNG", png.OutputFormat);
        var jpg = _s.Shrink(_f.Samples.Screenshot, Balanced, Opt("forcejpg", o => o.Format = FormatMode.Jpeg));
        Assert.Equal("JPEG", jpg.OutputFormat);
    }

    [Fact]
    public void A_second_run_never_overwrites_the_first_copy()
    {
        var o = Opt("twice");
        var a = _s.Shrink(_f.Samples.Screenshot, Balanced, o);
        var b = _s.Shrink(_f.Samples.Screenshot, Balanced, o);
        Assert.NotEqual(a.OutputPath, b.OutputPath);
        Assert.True(File.Exists(a.OutputPath) && File.Exists(b.OutputPath));
        Assert.EndsWith("-docs (2).png", b.OutputPath);
    }

    [Fact]
    public void Originals_are_never_changed()
    {
        var before = _f.Samples.All.ToDictionary(p => p, Hash);
        foreach (var p in _f.Samples.All) _s.Shrink(p, Small, Opt("hash"));
        foreach (var p in _f.Samples.All) Assert.Equal(before[p], Hash(p));
    }

    [Theory]
    [InlineData(4032, 3024, 1280, 1280, 960)]
    [InlineData(3024, 4032, 1280, 960, 1280)]
    [InlineData(600, 400, 1280, 600, 400)]
    [InlineData(2880, 1800, 1280, 1280, 800)]
    public void FitWithin_keeps_aspect_and_never_upscales(int w, int h, int edge, int ew, int eh)
    {
        Assert.Equal((ew, eh), Limits.FitWithin(w, h, edge));
    }

    [Fact]
    public void FitWithin_respects_25_megapixels()
    {
        var (w, h) = Limits.FitWithin(10000, 5000, 8000);
        Assert.True((long)w * h <= Limits.MaxPixels);
        Assert.True(w <= 8000);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}

public sealed class StringsTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)[^}]*\}");

    [Fact]
    public void English_and_Spanish_have_the_same_keys()
    {
        Assert.Empty(Strings.En.Keys.Except(Strings.Es.Keys));
        Assert.Empty(Strings.Es.Keys.Except(Strings.En.Keys));
    }

    [Fact]
    public void Placeholders_match_in_both_languages()
    {
        foreach (var (key, en) in Strings.En)
        {
            var a = Placeholder.Matches(en).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
            var b = Placeholder.Matches(Strings.Es[key]).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
            Assert.True(a.SequenceEqual(b), key);
        }
    }

    [Fact]
    public void No_long_dashes_anywhere()
    {
        foreach (var s in Strings.En.Values.Concat(Strings.Es.Values))
        {
            Assert.DoesNotContain('—', s);
            Assert.DoesNotContain('–', s);
        }
    }

    [Fact]
    public void Byte_sizes_use_the_language_decimal_mark()
    {
        Assert.Equal("4.8 MB", Strings.Bytes(5_033_165, Strings.CultureFor("en")));
        Assert.Equal("4,8 MB", Strings.Bytes(5_033_165, Strings.CultureFor("es")));
        Assert.Equal("236 KB", Strings.Bytes(241_664, Strings.CultureFor("en")));
    }
}
