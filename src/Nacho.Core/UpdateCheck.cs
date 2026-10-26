using System.Net.Http.Headers;
using System.Text.Json;

namespace Nacho.Core;

/// <summary>A newer NACHO release on GitHub.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string PageUrl);

/// <summary>
/// Asks GitHub's public API for the latest NACHO release. No account, no data about the user: GitHub sees an ordinary
/// web request with the user agent "NACHO/version". NACHO never downloads or installs anything itself; it only
/// shows the release page link. Drafts and pre-releases are ignored (GitHub's "latest" never returns them).
/// </summary>
public static class UpdateCheck
{
    public const string LatestReleaseApi = "https://api.github.com/repos/csabakovacs314/NACHO/releases/latest";
    public const string ReleasesPage = "https://github.com/csabakovacs314/NACHO/releases";

    /// <summary>How often the automatic check runs.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(20);

    public enum Outcome { UpToDate, Newer, Failed }

    public sealed record Result(Outcome Outcome, UpdateInfo? Update = null, string? Error = null);

    /// <summary>The version this NACHO reports (major.minor.build from the assembly).</summary>
    public static Version Current(System.Reflection.Assembly assembly)
    {
        var v = assembly.GetName().Version ?? new Version(0, 0, 0);
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    public static bool Due(DateTime? lastCheckUtc, DateTime nowUtc) =>
        lastCheckUtc is null || nowUtc - lastCheckUtc.Value >= Interval || lastCheckUtc.Value > nowUtc.AddHours(1);

    /// <summary>"v0.8.0" or "0.8.0" as a version; null for anything else (for example "v0.8.0-beta").</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string t = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(t, out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    /// <summary>Reads GitHub's release JSON; null when it is not a usable, published release.</summary>
    public static UpdateInfo? ParseRelease(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
            if (root.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) return null;
            string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            var version = ParseTag(tag);
            if (version is null) return null;
            string url = root.TryGetProperty("html_url", out var u) && u.GetString() is { } s && IsReleasePage(s) ? s : ReleasesPage;
            return new UpdateInfo(version, tag!, url);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Only NACHO's own release pages are opened, whatever the answer says.</summary>
    public static bool IsReleasePage(string url) =>
        url.StartsWith(ReleasesPage + "/", StringComparison.Ordinal) && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;

    public static async Task<Result> CheckAsync(Version current, HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        try
        {
            using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            http.Timeout = TimeSpan.FromSeconds(15);
            using var req = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("NACHO", current.ToString(3)));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return new Result(Outcome.Failed, Error: "HTTP " + (int)resp.StatusCode);
            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var info = ParseRelease(json);
            if (info is null) return new Result(Outcome.Failed, Error: "unexpected answer");
            return info.Version > current ? new Result(Outcome.Newer, info) : new Result(Outcome.UpToDate, info);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return new Result(Outcome.Failed, Error: ex.Message);
        }
    }
}
