using Nacho.Core;
using Xunit;

namespace Nacho.Tests;

/// <summary>The copy must keep the original's shape: it goes back into the Doc in the old image's frame.</summary>
public sealed class ShapeTests : IClassFixture<SamplesFixture>
{
    private static readonly int[] Edges = { 800, 1280, 1920, 1088, 924, 8000 };
    private readonly SamplesFixture _f;
    private readonly ImageShrinker _s = new();

    public ShapeTests(SamplesFixture f) => _f = f;

    [Theory]
    [InlineData(4032, 3024)]
    [InlineData(3024, 4032)]
    [InlineData(4000, 2667)]
    [InlineData(2879, 1799)]
    [InlineData(1920, 1081)]
    [InlineData(3000, 101)]
    [InlineData(101, 3000)]
    [InlineData(5000, 10)]
    [InlineData(10000, 7)]
    [InlineData(7000, 5000)]
    [InlineData(12000, 9000)]
    [InlineData(6000, 4000)]
    [InlineData(1001, 999)]
    [InlineData(2560, 1440)]
    [InlineData(4608, 3456)]
    [InlineData(3264, 2448)]
    public void Every_size_keeps_the_original_shape(int w, int h)
    {
        foreach (int edge in Edges)
        {
            var (a, b) = Limits.FitWithin(w, h, edge);
            string at = $"{w} x {h} at {edge} -> {a} x {b}";
            Assert.True(a <= w && b <= h, "never upscaled: " + at);
            Assert.True((long)a * b <= Limits.MaxPixels, "under 25 MP: " + at);
            Assert.True(Math.Max(a, b) <= edge || (a == w && b == h), "within the edge or left as it was: " + at);
            Assert.True(Shape.Error(w, h, a, b) <= 0.01, "shape within 1%: " + at);
        }
    }

    [Theory]
    [InlineData(4032, 3024)]
    [InlineData(4000, 2667)]
    [InlineData(2879, 1799)]
    [InlineData(1920, 1081)]
    [InlineData(3000, 101)]
    [InlineData(2560, 1440)]
    public void Ordinary_sizes_are_within_a_tenth_of_a_percent(int w, int h)
    {
        foreach (int edge in Edges)
        {
            var (a, b) = Limits.FitWithin(w, h, edge);
            Assert.True(Shape.Error(w, h, a, b) <= 0.001, $"{w} x {h} at {edge} -> {a} x {b}");
        }
    }

    [Theory]
    [InlineData(4032, 3024, 1280, 1280, 960)]
    [InlineData(3024, 4032, 1280, 960, 1280)]
    [InlineData(6000, 4000, 1280, 1278, 852)]
    [InlineData(1920, 1080, 1280, 1280, 720)]
    [InlineData(2880, 1800, 1280, 1280, 800)]
    [InlineData(2250, 3000, 1088, 816, 1088)]
    [InlineData(12000, 9000, 8000, 5772, 4329)]
    public void Common_shapes_come_out_exact(int w, int h, int edge, int ew, int eh)
    {
        var (a, b) = Limits.FitWithin(w, h, edge);
        Assert.Equal((ew, eh), (a, b));
        Assert.True(Shape.Same(w, h, a, b));
    }

    [Fact]
    public void Tight_budget_shrinks_further_but_keeps_the_exact_shape()
    {
        var tiny = new Preset("tiny", 1280, 30);
        var r = _s.Shrink(_f.Samples.Photo, tiny, new ShrinkOptions { OutputFolder = Path.Combine(_f.Root, "out-tiny") });
        Assert.Equal(ResultKind.Shrunk, r.Kind);
        Assert.True(r.OutputHeight < 1280, $"the extra shrinking step ran: {r.OutputWidth} x {r.OutputHeight}");
        Assert.True(r.ShapeExact, $"{r.OriginalWidth} x {r.OriginalHeight} -> {r.OutputWidth} x {r.OutputHeight}");
    }

    [Fact]
    public void Every_sample_copy_keeps_its_shape()
    {
        var opt = new ShrinkOptions { OutputFolder = Path.Combine(_f.Root, "out-shape") };
        foreach (var path in _f.Samples.All)
        {
            var r = _s.Shrink(path, new Preset("balanced", 1280, 300), opt);
            if (r.Kind != ResultKind.Shrunk) continue;
            Assert.True(r.ShapeErrorPercent <= 0.1, $"{Path.GetFileName(path)}: {r.ShapeErrorPercent:0.###}%");
            using var o = new ImageMagick.MagickImage(r.OutputPath!);
            Assert.Equal(r.OutputWidth, (int)o.Width);
            Assert.Equal(r.OutputHeight, (int)o.Height);
        }
    }

    [Theory]
    [InlineData(4032, 3024, "en", "4:3")]
    [InlineData(3024, 4032, "en", "3:4")]
    [InlineData(1920, 1080, "en", "16:9")]
    [InlineData(6000, 4000, "en", "3:2")]
    [InlineData(1000, 1000, "en", "1:1")]
    [InlineData(2000, 1100, "en", "1.82:1")]
    [InlineData(2000, 1100, "es", "1,82:1")]
    [InlineData(1100, 2000, "en", "1:1.82")]
    public void Shapes_have_readable_names(int w, int h, string lang, string expected)
    {
        Assert.Equal(expected, Shape.Label(w, h, Strings.CultureFor(lang)));
    }
}
