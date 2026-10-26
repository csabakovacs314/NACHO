using System.Diagnostics;
using System.Security.Cryptography;

namespace Nacho.Core;

/// <summary>
/// Makes PNG copies smaller without changing a pixel, by running oxipng (MIT licence, bundled with NACHO).
/// The apps carry oxipng inside them and unpack it once to a cache folder; without it PNGs are left as
/// Magick.NET wrote them. Any failure (missing tool, timeout, bad output) keeps the original bytes.
/// </summary>
public static class PngOptimizer
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>The oxipng program, or null to skip this step.</summary>
    public static string? ToolPath { get; set; }

    /// <summary>oxipng's effort level. 4 gave the best size for the time in the compression study (about 0.7 s a screenshot).</summary>
    public static int Level { get; set; } = 4;

    public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>A smaller PNG with the same pixels, or the same bytes when oxipng is missing, fails or gains nothing.</summary>
    public static byte[] Optimize(byte[] png)
    {
        string? tool = ToolPath;
        if (tool is null || !File.Exists(tool) || png.Length < PngSignature.Length) return png;

        string dir = Path.Combine(Path.GetTempPath(), "nacho-png-" + Guid.NewGuid().ToString("N"));
        string input = Path.Combine(dir, "in.png");
        string output = Path.Combine(dir, "out.png");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(input, png);
            var psi = new ProcessStartInfo(tool)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string arg in new[] { "-o", Level.ToString(System.Globalization.CultureInfo.InvariantCulture), "--strip", "safe", "--quiet", "--out", output, input })
            {
                psi.ArgumentList.Add(arg);
            }
            using var p = Process.Start(psi);
            if (p is null) return png;
            p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return png;
            }
            if (p.ExitCode != 0 || !File.Exists(output)) return png;
            byte[] result = File.ReadAllBytes(output);
            bool valid = result.Length > PngSignature.Length && result.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature);
            return valid && result.Length < png.Length ? result : png;
        }
        catch
        {
            return png;
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Unpacks a bundled tool into cacheDir/&lt;first 12 hex of its SHA-256&gt;/fileName, rewriting it when the file
    /// there is missing or different, and makes it executable on Linux. Returns its path, or null on failure.
    /// </summary>
    public static string? Extract(Stream source, string cacheDir, string fileName)
    {
        try
        {
            using var ms = new MemoryStream();
            source.CopyTo(ms);
            byte[] bytes = ms.ToArray();
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            string dir = Path.Combine(cacheDir, hash[..12]);
            string path = Path.Combine(dir, fileName);
            if (!File.Exists(path) || !SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(Convert.FromHexString(hash)))
            {
                Directory.CreateDirectory(dir);
                string temp = path + ".tmp-" + Environment.ProcessId;
                File.WriteAllBytes(temp, bytes);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                File.Move(temp, path, overwrite: true);
            }
            return path;
        }
        catch
        {
            return null;
        }
    }
}
