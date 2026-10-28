using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Nacho.Core.Google;

/// <summary>Finds the Doc in a Google Docs link.</summary>
public static partial class GoogleDocLink
{
    [GeneratedRegex(@"^https://docs\.google\.com/document/(?:u/\d+/)?d/([A-Za-z0-9_-]{20,100})(?:[/?#]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();

    /// <summary>The Doc's ID, or null when the text is not a Google Docs link.</summary>
    public static string? TryGetId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = text.Trim();
        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) t = "https://" + t[7..];
        if (t.StartsWith("docs.google.com/", StringComparison.OrdinalIgnoreCase)) t = "https://" + t;
        var m = LinkRegex().Match(t);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>The address NACHO opens to show the Doc: always built from the ID, never taken from outside.</summary>
    public static string EditUrl(string id) => $"https://docs.google.com/document/d/{id}/edit";
}

public enum GoogleSkip { None, NotPicture, Linked, Effects }

public sealed record GoogleCrop(double Left, double Right, double Top, double Bottom)
{
    public bool IsNone => Math.Abs(Left) < Tiny && Math.Abs(Right) < Tiny && Math.Abs(Top) < Tiny && Math.Abs(Bottom) < Tiny;
    /// <summary>Docs writes tiny rounding errors (like -3.5E-14) for an untouched side; those count as 0.</summary>
    private const double Tiny = 1e-6;

    public bool IsInside => Left > -Tiny && Right > -Tiny && Top > -Tiny && Bottom > -Tiny && Left + Right < 0.95 && Top + Bottom < 0.95;

    /// <summary>The same crop with rounding noise removed (never below 0).</summary>
    public GoogleCrop Clean() => new(Math.Max(0, Left), Math.Max(0, Right), Math.Max(0, Top), Math.Max(0, Bottom));
}

/// <summary>One picture in the Doc, as the Docs API describes it.</summary>
public sealed record GooglePicture(string ObjectId, string TabId, string TabTitle, bool Positioned, double WidthPt, double HeightPt,
    string? ContentUri, GoogleCrop Crop, GoogleSkip Skip);

public sealed record GoogleDocInfo(string Id, string Title, IReadOnlyList<GooglePicture> Pictures, int Tabs);

/// <summary>Reads the parts of a Docs API document NACHO needs: the tabs and the pictures in them.</summary>
public static class GoogleDocParser
{
    public static GoogleDocInfo Parse(string id, JsonNode doc)
    {
        var pics = new List<GooglePicture>();
        int tabs = 0;
        void Walk(JsonArray? list)
        {
            if (list is null) return;
            foreach (var t in list)
            {
                if (t is null) continue;
                tabs++;
                string tabId = (string?)t["tabProperties"]?["tabId"] ?? "";
                string title = (string?)t["tabProperties"]?["title"] ?? "";
                var dt = t["documentTab"];
                if (dt?["inlineObjects"] is JsonObject io)
                {
                    foreach (var kv in io) Add(kv.Key, tabId, title, false, kv.Value?["inlineObjectProperties"]?["embeddedObject"]);
                }
                if (dt?["positionedObjects"] is JsonObject po)
                {
                    foreach (var kv in po) Add(kv.Key, tabId, title, true, kv.Value?["positionedObjectProperties"]?["embeddedObject"]);
                }
                Walk(t["childTabs"] as JsonArray);
            }
        }

        void Add(string objectId, string tabId, string tabTitle, bool positioned, JsonNode? e)
        {
            double w = Pt(e?["size"]?["width"]), h = Pt(e?["size"]?["height"]);
            var ip = e?["imageProperties"];
            var cp = ip?["cropProperties"];
            var crop = new GoogleCrop(D(cp?["offsetLeft"]), D(cp?["offsetRight"]), D(cp?["offsetTop"]), D(cp?["offsetBottom"]));
            string? uri = (string?)ip?["contentUri"];
            GoogleSkip skip;
            if (ip is null || uri is null) skip = GoogleSkip.NotPicture;
            else if (e?["linkedContentReference"] is not null) skip = GoogleSkip.Linked;
            else if (Math.Abs(D(ip["brightness"])) > 1e-6 || Math.Abs(D(ip["contrast"])) > 1e-6 || Math.Abs(D(ip["transparency"])) > 1e-6
                     || Math.Abs(D(ip["angle"])) > 1e-6 || !crop.IsInside || D(cp?["angle"]) != 0)
                skip = GoogleSkip.Effects;
            else skip = GoogleSkip.None;
            pics.Add(new GooglePicture(objectId, tabId, tabTitle, positioned, w, h, uri, crop, skip));
        }

        if (doc["tabs"] is JsonArray tabsArray)
        {
            Walk(tabsArray);
        }
        else
        {
            // Without includeTabsContent the pictures sit at the top level.
            var wrapper = new JsonArray(new JsonObject
            {
                ["tabProperties"] = new JsonObject { ["tabId"] = "", ["title"] = "" },
                ["documentTab"] = new JsonObject
                {
                    ["inlineObjects"] = doc["inlineObjects"]?.DeepClone(),
                    ["positionedObjects"] = doc["positionedObjects"]?.DeepClone(),
                },
            });
            Walk(wrapper);
        }
        return new GoogleDocInfo(id, (string?)doc["title"] ?? "", pics, tabs);
    }

    private static double D(JsonNode? n)
    {
        if (n is null) return 0;
        try
        {
            return n.GetValue<double>();
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            return double.TryParse(n.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
        }
    }

    private static double Pt(JsonNode? d) => d is null ? 0 : D(d["magnitude"]) * ((string?)d["unit"] == "EMU" ? 1.0 / 12700 : 1);
}

/// <summary>The few Google Docs and Drive calls NACHO makes, with retries for the "slow down" answers.</summary>
/// <summary>A file in Drive as NACHO sees it.</summary>
public sealed record DriveFile(string Id, string Name, string MimeType, long Size, string? ParentId, string? Modified, bool CanEdit, string? Link)
{
    public bool IsWord => MimeType == GoogleAuth.WordMime;

    public static DriveFile From(JsonNode f) => new(
        (string?)f["id"] ?? throw new GoogleException(GoogleError.Server, "Drive did not return the file."),
        (string?)f["name"] ?? "",
        (string?)f["mimeType"] ?? "",
        long.TryParse((string?)f["size"], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long size) ? size : 0,
        (string?)(f["parents"] as JsonArray)?.FirstOrDefault(),
        (string?)f["modifiedTime"],
        (bool?)f["capabilities"]?["canEdit"] ?? false,
        (string?)f["webViewLink"]);

    public static string OpenUrl(string fileId) => "https://drive.google.com/file/d/" + Uri.EscapeDataString(fileId) + "/view";
}

public sealed class GoogleApi
{
    public const string DocsBase = "https://docs.googleapis.com/v1/documents/";
    public const string DriveBase = "https://www.googleapis.com/drive/v3/files";
    public const string UploadBase = "https://www.googleapis.com/upload/drive/v3/files";
    public const string TempPrefix = "nacho-temp-";

    /// <summary>Only the parts of the Doc NACHO needs (pictures and tabs, three levels deep), not the text.</summary>
    public const string DocFields = "title,tabs(tabProperties(tabId,title),documentTab(inlineObjects,positionedObjects),"
        + "childTabs(tabProperties(tabId,title),documentTab(inlineObjects,positionedObjects),"
        + "childTabs(tabProperties(tabId,title),documentTab(inlineObjects,positionedObjects),childTabs)))";

    private readonly GoogleAuth _auth;

    public GoogleApi(GoogleAuth auth) => _auth = auth;

    /// <summary>Waits between retries; tests replace it so they don't wait.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (t, ct) => Task.Delay(t, ct);

    public int MaxAttempts { get; set; } = 5;

    /// <summary>How many times Google was busy or the connection dropped and NACHO tried again (shown while working).</summary>
    public int Retries => _retries;
    private int _retries;

    public async Task<GoogleDocInfo> GetDocumentAsync(string docId, CancellationToken ct = default)
    {
        string url = DocsBase + Uri.EscapeDataString(docId) + "?includeTabsContent=true&fields=" + Uri.EscapeDataString(DocFields);
        var doc = await JsonAsync(HttpMethod.Get, url, null, ct, docRead: true).ConfigureAwait(false);
        return GoogleDocParser.Parse(docId, doc);
    }

    public async Task<string?> AccountEmailAsync(CancellationToken ct = default)
    {
        var about = await JsonAsync(HttpMethod.Get, "https://www.googleapis.com/drive/v3/about?fields=user(emailAddress)", null, ct).ConfigureAwait(false);
        return (string?)about["user"]?["emailAddress"];
    }

    /// <summary>Downloads a picture from the address the Docs API gave (valid for about 30 minutes).</summary>
    public async Task<byte[]> DownloadAsync(string contentUri, CancellationToken ct = default)
    {
        if (!IsPictureAddress(contentUri))
        {
            throw new GoogleException(GoogleError.Server, "Unexpected picture address.");
        }
        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, contentUri), ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw await Fail(resp, ct, false).ConfigureAwait(false);
        return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The Docs API gives https://docs.google.com/docs-images-rt/… (older Docs: lh*.googleusercontent.com).
    /// The access token is only ever sent to these Google addresses.</summary>
    public static bool IsPictureAddress(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.IsDefaultPort
        && (u.Host.Equals("docs.google.com", StringComparison.OrdinalIgnoreCase)
            || u.Host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>Uploads a shrunk picture to the user's Drive as a temporary file NACHO owns (drive.file).</summary>
    public async Task<string> UploadTempAsync(byte[] data, string mime, CancellationToken ct = default)
    {
        string name = TempPrefix + Guid.NewGuid().ToString("N") + (mime == "image/png" ? ".png" : mime == "image/gif" ? ".gif" : ".jpg");
        var meta = new JsonObject { ["name"] = name, ["description"] = "Temporary copy made by NACHO; it deletes this file a few seconds later." };
        using var resp = await SendAsync(() =>
        {
            var multipart = new MultipartContent("related");
            multipart.Add(new StringContent(meta.ToJsonString(), Encoding.UTF8, "application/json"));
            var bin = new ByteArrayContent(data);
            bin.Headers.ContentType = new MediaTypeHeaderValue(mime);
            multipart.Add(bin);
            return new HttpRequestMessage(HttpMethod.Post, UploadBase + "?uploadType=multipart&fields=id") { Content = multipart };
        }, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw await Fail(resp, ct, false).ConfigureAwait(false);
        var file = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return (string?)file?["id"] ?? throw new GoogleException(GoogleError.Server, "Drive did not return the file.");
    }

    /// <summary>Makes a copy of the Doc in the same Drive folder (Google keeps the folder when the user may write there).
    /// The copy belongs to the user and NACHO may open it, because NACHO made it (drive.file).</summary>
    public async Task<(string Id, string Name)> CopyDocAsync(string docId, string name, CancellationToken ct = default)
    {
        var file = await JsonAsync(HttpMethod.Post, $"{DriveBase}/{Uri.EscapeDataString(docId)}/copy?fields=id,name&supportsAllDrives=true",
            new JsonObject { ["name"] = name }, ct).ConfigureAwait(false);
        string id = (string?)file["id"] ?? throw new GoogleException(GoogleError.Server, "Drive did not return the copy.");
        return (id, (string?)file["name"] ?? name);
    }

    /// <summary>Lets Google Docs fetch the temporary file: "anyone with the link can view", for the few seconds it exists.</summary>
    public Task ShareAsync(string fileId, CancellationToken ct = default) =>
        JsonAsync(HttpMethod.Post, $"{DriveBase}/{Uri.EscapeDataString(fileId)}/permissions?fields=id",
            new JsonObject { ["role"] = "reader", ["type"] = "anyone" }, ct);

    public static string TempUrl(string fileId) => "https://drive.google.com/uc?export=download&id=" + Uri.EscapeDataString(fileId);

    /// <summary>Replaces pictures in the Doc in one request; each keeps its place and size.</summary>
    public Task ReplaceAsync(string docId, IEnumerable<(string ObjectId, string TabId, string Url)> pictures, CancellationToken ct = default)
    {
        var requests = new JsonArray();
        foreach (var (objectId, tabId, url) in pictures)
        {
            var r = new JsonObject { ["imageObjectId"] = objectId, ["uri"] = url, ["imageReplaceMethod"] = "CENTER_CROP" };
            if (!string.IsNullOrEmpty(tabId)) r["tabId"] = tabId;
            requests.Add(new JsonObject { ["replaceImage"] = r });
        }
        return JsonAsync(HttpMethod.Post, DocsBase + Uri.EscapeDataString(docId) + ":batchUpdate", new JsonObject { ["requests"] = requests }, ct, docWrite: true);
    }

    // ---------- Word files (.docx) in Drive ----------

    public const string FileFields = "id,name,mimeType,size,parents,modifiedTime,capabilities(canEdit),webViewLink";

    /// <summary>Name, size, folder, last change and whether the user may change the file (a file picked in the picker).</summary>
    public async Task<DriveFile> GetFileAsync(string fileId, CancellationToken ct = default) =>
        DriveFile.From(await JsonAsync(HttpMethod.Get, $"{DriveBase}/{Uri.EscapeDataString(fileId)}?fields={FileFields}&supportsAllDrives=true",
            null, ct, docRead: true).ConfigureAwait(false));

    /// <summary>Downloads a file's content to a local path.</summary>
    public async Task DownloadFileAsync(string fileId, string path, CancellationToken ct = default)
    {
        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get,
            $"{DriveBase}/{Uri.EscapeDataString(fileId)}?alt=media&supportsAllDrives=true"), ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw await Fail(resp, ct, true).ConfigureAwait(false);
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
    }

    /// <summary>Uploads a new file (in <paramref name="parentId"/>, or My Drive when null). NACHO made it, so drive.file lets NACHO open it.</summary>
    public async Task<DriveFile> UploadNewFileAsync(string name, string? parentId, string mime, string path, CancellationToken ct = default)
    {
        var meta = new JsonObject { ["name"] = name, ["mimeType"] = mime };
        if (!string.IsNullOrEmpty(parentId)) meta["parents"] = new JsonArray(parentId);
        string session = await StartUploadAsync(HttpMethod.Post, $"{UploadBase}?uploadType=resumable&supportsAllDrives=true&fields={FileFields}",
            meta, mime, new FileInfo(path).Length, ct).ConfigureAwait(false);
        return await FinishUploadAsync(session, mime, path, ct, docWrite: false).ConfigureAwait(false);
    }

    /// <summary>Uploads new content for an existing file: same ID, link and sharing; Drive keeps the old content as a
    /// previous version (Manage versions).</summary>
    public async Task<DriveFile> UpdateFileContentAsync(string fileId, string mime, string path, CancellationToken ct = default)
    {
        string session = await StartUploadAsync(HttpMethod.Patch,
            $"{UploadBase}/{Uri.EscapeDataString(fileId)}?uploadType=resumable&supportsAllDrives=true&fields={FileFields}",
            new JsonObject(), mime, new FileInfo(path).Length, ct, docWrite: true).ConfigureAwait(false);
        return await FinishUploadAsync(session, mime, path, ct, docWrite: true).ConfigureAwait(false);
    }

    /// <summary>Starts a resumable upload (safe for big files) and returns its session address.</summary>
    private async Task<string> StartUploadAsync(HttpMethod method, string url, JsonObject meta, string mime, long length, CancellationToken ct, bool docWrite = false)
    {
        using var resp = await SendAsync(() =>
        {
            var req = new HttpRequestMessage(method, url) { Content = new StringContent(meta.ToJsonString(), Encoding.UTF8, "application/json") };
            req.Headers.Add("X-Upload-Content-Type", mime);
            req.Headers.Add("X-Upload-Content-Length", length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return req;
        }, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw await Fail(resp, ct, false, docWrite).ConfigureAwait(false);
        var location = resp.Headers.Location;
        // The session address must be Google's upload host: the access token goes nowhere else.
        if (location is null || !location.IsAbsoluteUri || location.Scheme != Uri.UriSchemeHttps
            || !location.Host.Equals("www.googleapis.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new GoogleException(GoogleError.Server, "Drive did not start the upload.");
        }
        return location.ToString();
    }

    private async Task<DriveFile> FinishUploadAsync(string session, string mime, string path, CancellationToken ct, bool docWrite)
    {
        using var resp = await SendAsync(() =>
        {
            var content = new StreamContent(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            content.Headers.ContentType = new MediaTypeHeaderValue(mime);
            return new HttpRequestMessage(HttpMethod.Put, session) { Content = content };
        }, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw await Fail(resp, ct, false, docWrite).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return DriveFile.From(JsonNode.Parse(body) ?? new JsonObject());
    }

    public async Task DeleteAsync(string fileId, CancellationToken ct = default)
    {
        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, $"{DriveBase}/{Uri.EscapeDataString(fileId)}"), ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.NotFound) throw await Fail(resp, ct, false).ConfigureAwait(false);
    }

    /// <summary>Temporary files a cancelled or crashed run left behind (NACHO can only see files it created).</summary>
    public async Task<List<string>> ListTempFilesAsync(CancellationToken ct = default)
    {
        string q = $"name contains '{TempPrefix}' and trashed = false";
        var list = await JsonAsync(HttpMethod.Get, $"{DriveBase}?q={Uri.EscapeDataString(q)}&fields=files(id,name)&pageSize=100", null, ct).ConfigureAwait(false);
        return (list["files"] as JsonArray ?? new JsonArray())
            .Where(f => ((string?)f?["name"])?.StartsWith(TempPrefix, StringComparison.Ordinal) == true)
            .Select(f => (string)f!["id"]!).ToList();
    }

    // ---------- plumbing ----------

    private async Task<JsonNode> JsonAsync(HttpMethod method, string url, JsonNode? body, CancellationToken ct, bool docRead = false, bool docWrite = false)
    {
        using var resp = await SendAsync(() => new HttpRequestMessage(method, url)
        {
            Content = body is null ? null : new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        }, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw await Fail(resp, ct, docRead, docWrite).ConfigureAwait(false);
        string s = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(s) ? new JsonObject() : JsonNode.Parse(s) ?? new JsonObject();
    }

    /// <summary>Sends with the access token; renews it once on 401 and waits and retries on 429 and 5xx.</summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> make, CancellationToken ct)
    {
        bool refreshed = false, force = false;
        for (int attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var req = make();
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _auth.AccessTokenAsync(ct, forceRefresh: force).ConfigureAwait(false));
            force = false;
            HttpResponseMessage resp;
            try
            {
                resp = await _auth.Http.SendAsync(req, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                if (attempt >= MaxAttempts) throw new GoogleException(GoogleError.Network, ex.Message, 0, ex);
                Interlocked.Increment(ref _retries);
                await Delay(Backoff(attempt), ct).ConfigureAwait(false);
                continue;
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                if (attempt >= MaxAttempts) throw new GoogleException(GoogleError.Network, "Google did not answer in time.", 0, ex);
                Interlocked.Increment(ref _retries);
                await Delay(Backoff(attempt), ct).ConfigureAwait(false);
                continue;
            }

            int code = (int)resp.StatusCode;
            if (code == 401 && !refreshed)
            {
                resp.Dispose();
                refreshed = force = true;
                continue;
            }
            if ((code == 429 || code >= 500) && attempt < MaxAttempts)
            {
                var wait = resp.Headers.RetryAfter?.Delta ?? Backoff(attempt);
                Interlocked.Increment(ref _retries);
                resp.Dispose();
                await Delay(wait > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : wait, ct).ConfigureAwait(false);
                continue;
            }
            return resp;
        }
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(1000 * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 500));

    private static async Task<GoogleException> Fail(HttpResponseMessage resp, CancellationToken ct, bool docRead, bool docWrite = false)
    {
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        int code = (int)resp.StatusCode;
        var kind = code switch
        {
            401 => GoogleError.SignInNeeded,
            404 => GoogleError.NotFound,
            403 when body.Contains("storageQuotaExceeded", StringComparison.OrdinalIgnoreCase) => GoogleError.DriveFull,
            403 when body.Contains("cannotCopyFile", StringComparison.OrdinalIgnoreCase) => GoogleError.CopyRefused,
            403 when body.Contains("rateLimit", StringComparison.OrdinalIgnoreCase) || body.Contains("quota", StringComparison.OrdinalIgnoreCase) => GoogleError.Quota,
            403 when docWrite => GoogleError.ReadOnly,
            403 when docRead => GoogleError.NoPermission,
            403 => GoogleError.NoPermission,
            429 => GoogleError.Quota,
            >= 500 => GoogleError.Server,
            _ => GoogleError.Server,
        };
        return new GoogleException(kind, $"Google said {code}: {ErrorText(body)}", code);
    }

    /// <summary>Google's error message from a JSON error answer, short.</summary>
    public static string ErrorText(string body)
    {
        try
        {
            var n = JsonNode.Parse(body);
            string? m = (string?)n?["error"]?["message"] ?? (string?)n?["error_description"] ?? (n?["error"] is JsonValue v ? v.ToString() : null);
            if (!string.IsNullOrWhiteSpace(m)) return m.Length > 300 ? m[..300] + "…" : m;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
        }
        return body.Length > 200 ? body[..200] + "…" : body;
    }
}
