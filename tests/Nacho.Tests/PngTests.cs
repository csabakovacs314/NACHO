using ImageMagick;
using Nacho.Core;
using Xunit;

namespace Nacho.Tests;

/// <summary>
/// The 256-colour quality guard and the bundled oxipng step. Same collection as ShrinkerTests, because
/// PngOptimizer.ToolPath is shared by the whole engine and must not change under a running shrink.
/// </summary>
[Collection("Engine")]
public sealed class PngTests : IClassFixture<SamplesFixture>, IDisposable
{
    private static readonly Preset Balanced = new("balanced", 1280, 300);
    private readonly SamplesFixture _f;
    private readonly ImageShrinker _s = new();
    private readonly string _work;

    public PngTests(SamplesFixture f)
    {
        _f = f;
        _work = Path.Combine(f.Root, "png-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(_work);
    }

    public void Dispose() => PngOptimizer.ToolPath = null;

    private ShrinkOptions Opt(string name) => new() { OutputFolder = Path.Combine(_work, name) };

    /// <summary>The oxipng build for this system from tools/oxipng, unpacked the way the apps do it.</summary>
    private string Oxipng()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Directory.Build.props"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        string source = OperatingSystem.IsWindows()
            ? Path.Combine(dir!, "tools", "oxipng", "win-x64", "oxipng.exe")
            : Path.Combine(dir!, "tools", "oxipng", "linux-x64", "oxipng");
        using var s = File.OpenRead(source);
        string? path = PngOptimizer.Extract(s, Path.Combine(_work, "tools"), Path.GetFileName(source));
        Assert.NotNull(path);
        return path!;
    }

    [Fact]
    public void Banded_screenshot_stays_in_full_colour()
    {
        string src = SampleImages.BandedScreenshot(Path.Combine(_work, "banded.png"));
        var r = _s.Shrink(src, Balanced, Opt("banded"));

        Assert.Equal("PNG", r.OutputFormat);
        Assert.NotNull(r.PalettePsnr);
        Assert.True(r.PalettePsnr < ImageShrinker.MinPalettePsnr, $"256-colour version scored {r.PalettePsnr:0.0} dB");
        Assert.Contains(Badge.FullColour, r.Badges);
        Assert.DoesNotContain(Badge.Colours256, r.Badges);

        // Full colour means every pixel as it was (the picture is under 1280 px, so it is not resized).
        using var a = new MagickImage(src);
        using var b = new MagickImage(r.OutputPath!);
        Assert.Equal(0, a.Compare(b, ErrorMetric.MeanSquared), 9);
    }

    [Fact]
    public void Plain_screenshot_keeps_256_colours_when_they_look_the_same()
    {
        var r = _s.Shrink(_f.Samples.Screenshot, Balanced, Opt("plain"));
        Assert.Equal("PNG", r.OutputFormat);
        if (r.PalettePsnr is { } psnr)
        {
            Assert.Equal(psnr >= ImageShrinker.MinPalettePsnr, r.Badges.Contains(Badge.Colours256));
            Assert.Equal(psnr < ImageShrinker.MinPalettePsnr, r.Badges.Contains(Badge.FullColour));
        }
        Assert.False(r.Badges.Contains(Badge.Colours256) && r.Badges.Contains(Badge.FullColour));
    }

    [Fact]
    public void Psnr_is_100_for_the_same_picture_and_lower_for_a_changed_one()
    {
        using var a = new MagickImage(SampleImages.BandedScreenshot(Path.Combine(_work, "p.png")));
        using var b = (MagickImage)a.Clone();
        Assert.Equal(100, ImageShrinker.Psnr(a, b));
        b.Quantize(new QuantizeSettings { Colors = 16, DitherMethod = DitherMethod.No });
        Assert.InRange(ImageShrinker.Psnr(a, b), 15, 40);
    }

    [Fact]
    public void Without_oxipng_png_bytes_are_unchanged()
    {
        PngOptimizer.ToolPath = null;
        byte[] png = File.ReadAllBytes(_f.Samples.Screenshot);
        Assert.Same(png, PngOptimizer.Optimize(png));
        PngOptimizer.ToolPath = Path.Combine(_work, "no-such-tool");
        Assert.Same(png, PngOptimizer.Optimize(png));
    }

    [Fact]
    public void A_failing_tool_keeps_the_original()
    {
        PngOptimizer.ToolPath = Oxipng();
        byte[] notPng = "this is not a PNG file at all"u8.ToArray();
        Assert.Same(notPng, PngOptimizer.Optimize(notPng));
    }

    [Fact]
    public void Oxipng_makes_pngs_smaller_with_identical_pixels()
    {
        PngOptimizer.ToolPath = Oxipng();
        string src = SampleImages.BandedScreenshot(Path.Combine(_work, "o.png"));
        using var img = new MagickImage(src);
        img.Format = MagickFormat.Png;
        img.Quality = 95;
        byte[] before = img.ToByteArray();

        byte[] after = PngOptimizer.Optimize(before);

        Assert.True(after.Length < before.Length, $"{before.Length} -> {after.Length}");
        using var a = new MagickImage(before);
        using var b = new MagickImage(after);
        Assert.Equal(0, a.Compare(b, ErrorMetric.MeanSquared), 9);
    }

    [Fact]
    public void Shrink_records_the_oxipng_saving()
    {
        PngOptimizer.ToolPath = Oxipng();
        var r = _s.Shrink(_f.Samples.Screenshot, Balanced, Opt("oxi"));
        Assert.Equal("PNG", r.OutputFormat);
        Assert.NotNull(r.PngOptimisedFrom);
        Assert.True(r.OutputBytes < r.PngOptimisedFrom, $"{r.PngOptimisedFrom} -> {r.OutputBytes}");
        Assert.Equal(r.OutputBytes, new FileInfo(r.OutputPath!).Length);
    }

    [Fact]
    public void Extract_writes_once_and_repairs_a_changed_copy()
    {
        byte[] tool = "#!/bin/sh\necho hi\n"u8.ToArray();
        string cache = Path.Combine(_work, "cache");
        string? first = PngOptimizer.Extract(new MemoryStream(tool), cache, "tool");
        Assert.NotNull(first);
        Assert.Equal(tool, File.ReadAllBytes(first!));
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(first!).HasFlag(UnixFileMode.UserExecute));
        }

        File.WriteAllText(first!, "tampered");
        string? second = PngOptimizer.Extract(new MemoryStream(tool), cache, "tool");
        Assert.Equal(first, second);
        Assert.Equal(tool, File.ReadAllBytes(second!));
    }
}
