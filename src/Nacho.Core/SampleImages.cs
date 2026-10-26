using ImageMagick;
using ImageMagick.Drawing;

namespace Nacho.Core;

/// <summary>Builds the sample images the unit tests and the app self-test feed through NACHO.</summary>
public static partial class SampleImages
{
    public sealed record Set(
        string Photo, string Screenshot, string Logo, string Alpha, string Animated,
        string WebP, string SmallGpsPhoto, string Pdf, string Broken)
    {
        public IEnumerable<string> All => new[] { Photo, Screenshot, Logo, Alpha, Animated, WebP, SmallGpsPhoto, Pdf, Broken };
    }

    public static Set Generate(string dir)
    {
        Directory.CreateDirectory(dir);
        string P(string name) => Path.Combine(dir, name);

        // A phone photo: stored 3000 x 2250 with EXIF orientation 6, so it shows as 2250 x 3000. Camera details and GPS.
        string photo = P("IMG_4821.jpg");
        using (var img = new MagickImage("plasma:", new MagickReadSettings { Width = 3000, Height = 2250 }))
        {
            img.AddNoise(NoiseType.Gaussian, 0.3);
            img.SetProfile(CameraExif(orientation: 6, gps: true));
            img.Orientation = Orientation.RightTop;
            img.Quality = 95;
            img.Write(photo, MagickFormat.Jpeg);
        }

        // A screenshot: flat UI, lines of "text", one small picture inside.
        string shot = P("Screenshot 2026-10-22 143010.png");
        using (var img = new MagickImage(MagickColors.White, 2880, 1800))
        {
            var d = new Drawables()
                .FillColor(new MagickColor("#E6E6E6")).Rectangle(0, 0, 2880, 96)
                .FillColor(new MagickColor("#005FB8")).Rectangle(120, 1500, 520, 1600)
                .FillColor(new MagickColor("#2B2B2B"));
            var rnd = new Random(7);
            for (int line = 0; line < 40; line++)
            {
                double y = 180 + line * 30, x = 120;
                while (x < 1600)
                {
                    double w = 8 + rnd.Next(30);
                    d.Rectangle(x, y, x + w, y + 14);
                    x += w + 6 + (rnd.Next(5) == 0 ? 14 : 0);
                }
            }
            d.Draw(img);
            using var pic = new MagickImage("plasma:", new MagickReadSettings { Width = 600, Height = 400 });
            img.Composite(pic, 1800, 300, CompositeOperator.Over);
            img.Write(shot, MagickFormat.Png);
        }

        // A small logo that is already Docs-ready.
        string logo = P("logo-final.png");
        using (var img = new MagickImage(MagickColors.White, 512, 512))
        {
            new Drawables()
                .FillColor(new MagickColor("#E8A33D")).StrokeColor(new MagickColor("#8A5000")).StrokeWidth(12)
                .Polygon(new PointD(256, 40), new PointD(480, 440), new PointD(32, 440))
                .Draw(img);
            img.Write(logo, MagickFormat.Png);
        }

        // A large image with real transparency.
        string alpha = P("cut-out.png");
        using (var img = new MagickImage("plasma:", new MagickReadSettings { Width = 1600, Height = 1600 }))
        using (var mask = new MagickImage(MagickColors.Transparent, 1600, 1600))
        {
            new Drawables().FillColor(MagickColors.White).Circle(800, 800, 800, 100).Draw(mask);
            img.Alpha(AlphaAction.Set);
            img.Composite(mask, CompositeOperator.DstIn);
            img.Write(alpha, MagickFormat.Png);
        }

        // A large animated GIF.
        string anim = P("spinner.gif");
        using (var coll = new MagickImageCollection())
        {
            for (int i = 0; i < 3; i++)
            {
                var frame = new MagickImage("plasma:", new MagickReadSettings { Width = 1800, Height = 1800 });
                frame.AnimationDelay = 50;
                coll.Add(frame);
            }
            coll.Quantize(new QuantizeSettings { Colors = 128 });
            coll.Write(anim, MagickFormat.Gif);
        }

        // A WebP that Docs would refuse.
        string webp = P("banner.webp");
        using (var img = new MagickImage("plasma:", new MagickReadSettings { Width = 2000, Height = 1500 }))
        {
            img.Quality = 90;
            img.Write(webp, MagickFormat.WebP);
        }

        // A small photo with GPS: must lose the GPS but must not be upscaled.
        string small = P("small-gps.jpg");
        using (var img = new MagickImage("plasma:", new MagickReadSettings { Width = 800, Height = 600 }))
        {
            img.SetProfile(CameraExif(orientation: 1, gps: true));
            img.Quality = 90;
            img.Write(small, MagickFormat.Jpeg);
        }

        // A text-only PDF: nothing to shrink, so NACHO keeps it (0.7.0 shrinks pictures inside PDFs).
        string pdf = PdfTextOnly(P("scan-03.pdf"));

        string broken = P("broken.jpg");
        var junk = new byte[4096];
        new Random(11).NextBytes(junk);
        junk[0] = 0x00;
        File.WriteAllBytes(broken, junk);

        return new Set(photo, shot, logo, alpha, anim, webp, small, pdf, broken);
    }

    private static ExifProfile CameraExif(ushort orientation, bool gps)
    {
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, "NachoCam");
        exif.SetValue(ExifTag.Model, "Test One");
        exif.SetValue(ExifTag.Orientation, orientation);
        if (gps)
        {
            exif.SetValue(ExifTag.GPSLatitudeRef, "N");
            exif.SetValue(ExifTag.GPSLatitude, new[] { new Rational(51, 1), new Rational(53, 1), new Rational(0, 1) });
            exif.SetValue(ExifTag.GPSLongitudeRef, "W");
            exif.SetValue(ExifTag.GPSLongitude, new[] { new Rational(8, 1), new Rational(28, 1), new Rational(0, 1) });
        }
        return exif;
    }
    /// <summary>
    /// A screenshot with a photo-like panel: under 8000 colours, so NACHO treats it as a graphic, but its
    /// 256-colour version bands (about 41 dB). The quality guard must keep it in full colour.
    /// </summary>
    public static string BandedScreenshot(string path)
    {
        const int w = 1000, h = 640, levels = 64;
        var px = new byte[w * h * 3];
        static byte Q(double c) => (byte)Math.Clamp(Math.Round(Math.Round(c / 255 * (levels - 1)) * 255.0 / (levels - 1)), 0, 255);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 3;
                byte r = 245, g = 245, b = 245;
                if (x >= 40 && x < 600 && y >= 60 && y < 600)
                {
                    double u = (x - 40) / 560.0, v = (y - 60) / 540.0;
                    r = Q(40 + 180 * u + 25 * Math.Sin(v * 9 + u * 3));
                    g = Q(90 + 120 * v + 20 * Math.Sin(u * 7));
                    b = Q(160 - 90 * u * v + 20 * Math.Cos((u + v) * 5));
                }
                else if (x >= 640 && x < 960 && (y / 24) % 2 == 0 && y % 24 < 12 && (x / 7) % 9 != 0)
                {
                    r = g = b = 40;
                }
                px[i] = r;
                px[i + 1] = g;
                px[i + 2] = b;
            }
        }
        using var img = new MagickImage(px, new MagickReadSettings { Width = w, Height = h, Format = MagickFormat.Rgb, Depth = 8 });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Stored without compression, like a raw screen capture, so NACHO makes a copy instead of keeping it.
        img.Quality = 0;
        img.Write(path, MagickFormat.Png24);
        return path;
    }
}
