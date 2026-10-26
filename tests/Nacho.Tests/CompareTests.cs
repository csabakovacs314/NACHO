using System.Security.Cryptography;
using ImageMagick;
using Nacho.Core;
using Xunit;

namespace Nacho.Tests;

/// <summary>The pictures behind the before/after slider.</summary>
[Collection("Engine")]
public sealed class CompareTests : IClassFixture<SamplesFixture>
{
    private static readonly Preset Balanced = new("balanced", 1280, 300);
    private readonly SamplesFixture _f;
    private readonly ImageShrinker _s = new();

    public CompareTests(SamplesFixture f) => _f = f;

    private ShrinkOptions Opt() => new() { OutputFolder = Path.Combine(_f.Root, "cmp-" + Guid.NewGuid().ToString("N")[..6]) };

    [Fact]
    public void Original_is_upright_and_resized_to_the_copy()
    {
        string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_f.Samples.Photo)));
        var r = _s.Shrink(_f.Samples.Photo, Balanced, Opt());
        var c = ComparePictures.Load(r);

        Assert.Equal(r.OutputWidth, c.Width);
        Assert.Equal(r.OutputHeight, c.Height);
        Assert.Equal(2250, c.OriginalWidth);
        Assert.Equal(3000, c.OriginalHeight);
        Assert.True(c.OriginalResized);
        Assert.Equal(c.Width * c.Height * 4, c.Original.Length);
        Assert.Equal(c.Width * c.Height * 4, c.Copy.Length);
        Assert.Null(c.Palette);
        Assert.Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_f.Samples.Photo))));
    }

    [Fact]
    public void Both_sides_show_the_same_picture()
    {
        var r = _s.Shrink(_f.Samples.Photo, Balanced, Opt());
        var c = ComparePictures.Load(r);
        // A JPEG at quality 60 to 82 stays close to the original: the mean difference per channel is small.
        double diff = 0;
        for (int i = 0; i < c.Copy.Length; i += 4)
        {
            diff += Math.Abs(c.Copy[i] - c.Original[i]) + Math.Abs(c.Copy[i + 1] - c.Original[i + 1]) + Math.Abs(c.Copy[i + 2] - c.Original[i + 2]);
        }
        diff /= c.Copy.Length / 4 * 3;
        Assert.InRange(diff, 0.01, 12);
        Assert.All(Enumerable.Range(0, 50).Select(i => c.Copy[i * 4 + 3]), a => Assert.Equal(255, a));
    }

    [Fact]
    public void Banded_screenshot_offers_the_256_colour_version_it_turned_down()
    {
        string src = SampleImages.BandedScreenshot(Path.Combine(_f.Root, "cmp-banded-" + Guid.NewGuid().ToString("N")[..6] + ".png"));
        var r = _s.Shrink(src, Balanced, Opt());
        var c = ComparePictures.Load(r);

        Assert.NotNull(c.Palette);
        Assert.Equal(c.Copy.Length, c.Palette!.Length);
        Assert.False(c.OriginalResized);
        Assert.NotNull(c.PalettePsnr);
        Assert.Equal(r.PalettePsnr!.Value, c.PalettePsnr!.Value, 1);
        Assert.True(c.PalettePsnr < ImageShrinker.MinPalettePsnr);
        // Full colour copy: the same pixels as the original.
        Assert.Equal(c.Original, c.Copy);
        Assert.NotEqual(c.Copy, c.Palette);
    }

    [Fact]
    public void Transparency_comes_through()
    {
        var r = _s.Shrink(_f.Samples.Alpha, Balanced, Opt());
        var c = ComparePictures.Load(r);
        Assert.Equal(0, c.Copy[3]);
        Assert.Equal(0, c.Original[3]);
    }

    [Fact]
    public void Only_shrunk_pictures_can_be_compared()
    {
        Assert.False(ComparePictures.CanCompare(_s.Shrink(_f.Samples.Logo, Balanced, Opt())));
        Assert.False(ComparePictures.CanCompare(_s.Shrink(_f.Samples.Pdf, Balanced, Opt())));
        Assert.False(ComparePictures.CanCompare(null));
        Assert.True(ComparePictures.CanCompare(_s.Shrink(_f.Samples.Screenshot, Balanced, Opt())));
    }
}
