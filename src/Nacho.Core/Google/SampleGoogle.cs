using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ImageMagick;

namespace Nacho.Core.Google;

/// <summary>A pretend Google (sign-in server, Docs API, Drive API, picture addresses) for the tests and the self-test.
/// Nothing here talks to the network.</summary>
public sealed class SampleGoogle : HttpMessageHandler
{
    public const string ContentHost = "https://docs.google.com/docs-images-rt/";
    public readonly Dictionary<string, byte[]> Pictures = new();
    public JsonObject Doc = new();
    public readonly Dictionary<string, (byte[] Data, bool Shared, string Name)> Files = new();
    public readonly List<string> Replaced = new();
    /// <summary>The Doc each replacement went into, in the same order as <see cref="Replaced"/>.</summary>
    public readonly List<string> ReplacedIn = new();
    /// <summary>Copies NACHO made: (new id, name, the Doc copied).</summary>
    public readonly List<(string Id, string Name, string Source)> Copies = new();
    /// <summary>Gives the pictures of a copy other IDs (Google keeps them; this tests the fallback).</summary>
    public bool CopyChangesIds;
    /// <summary>A 403 answer body for the copy request, for example a full Drive.</summary>
    public string? CopyFails;
    public readonly List<string> Log = new();
    public readonly List<string> TokenForms = new();
    public int Fail429OnDocRead;
    public bool Fail401Once;
    public bool RefreshRejected;
    public bool ReadOnly;
    public string AccessToken = "at-1";

    /// <summary>Like drive.file: NACHO may only open the Docs chosen in the picker. Null lets every Doc through.</summary>
    public HashSet<string>? Picked;

    /// <summary>What the pretend picker returns when NACHO does not name a Doc.</summary>
    public const string PickerDefault = "1SampleDocPickedInTheGooglePicker0000";
    private int _next;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        string url = req.RequestUri!.ToString();
        string body = req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct);
        lock (Log) Log.Add(req.Method + " " + url);

        if (url == GoogleAuth.TokenEndpoint)
        {
            lock (TokenForms) TokenForms.Add(body);
            // The pretend picker puts the chosen Doc into the code ("pick:<id>"), as Google ties the grant to the code.
            var codeMatch = System.Text.RegularExpressions.Regex.Match(body, "code=pick%3A([A-Za-z0-9_-]+)");
            if (codeMatch.Success && Picked is not null) lock (Picked) Picked.Add(codeMatch.Groups[1].Value);
            if (RefreshRejected && body.Contains("grant_type=refresh_token")) return Json(400, """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""");
            string refresh = body.Contains("authorization_code") ? ",\"refresh_token\":\"rt-1\"" : "";
            return Json(200, $$"""{"access_token":"{{AccessToken}}","expires_in":3599,"scope":"https://www.googleapis.com/auth/drive.file","token_type":"Bearer"{{refresh}}}""");
        }
        if (url.StartsWith(GoogleAuth.RevokeEndpoint, StringComparison.Ordinal)) return Json(200, "{}");

        // Every API call must carry the access token.
        if (req.Headers.Authorization?.Parameter is not { } token) return Json(401, """{"error":{"code":401,"message":"no token"}}""");
        if (Fail401Once)
        {
            Fail401Once = false;
            return Json(401, """{"error":{"code":401,"message":"expired"}}""");
        }
        if (token != AccessToken) return Json(401, """{"error":{"code":401,"message":"bad token"}}""");

        if (url.StartsWith(ContentHost, StringComparison.Ordinal))
        {
            string id = url[ContentHost.Length..];
            return Pictures.TryGetValue(id, out var d) ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(d) } : Json(404, "{}");
        }
        if (url.StartsWith(GoogleApi.DocsBase, StringComparison.Ordinal) && req.Method == HttpMethod.Get)
        {
            if (Fail429OnDocRead > 0)
            {
                Fail429OnDocRead--;
                return Json(429, """{"error":{"code":429,"message":"Quota exceeded","status":"RESOURCE_EXHAUSTED"}}""");
            }
            string docId = url[GoogleApi.DocsBase.Length..].Split('?')[0];
            bool allowed = Picked is null || Picked.Contains(docId);
            return url.Contains("/missing") || !allowed ? Json(404, """{"error":{"code":404,"message":"Requested entity was not found."}}""") : Json(200, DocFor(docId).ToJsonString());
        }
        if (url.StartsWith(GoogleApi.DocsBase, StringComparison.Ordinal) && url.EndsWith(":batchUpdate", StringComparison.Ordinal))
        {
            if (ReadOnly) return Json(403, """{"error":{"code":403,"message":"The caller does not have permission"}}""");
            var reqs = (JsonNode.Parse(body)!["requests"] as JsonArray)!;
            string target = url[GoogleApi.DocsBase.Length..^":batchUpdate".Length];
            var objects = AllObjects(DocFor(target));
            foreach (var r in reqs)
            {
                var ri = r!["replaceImage"]!;
                string oid = (string)ri["imageObjectId"]!;
                string uri = (string)ri["uri"]!;
                string fileId = uri.Contains("id=", StringComparison.Ordinal) ? Uri.UnescapeDataString(uri[(uri.IndexOf("id=", StringComparison.Ordinal) + 3)..]) : "";
                if (!objects.Contains(oid)) return Json(400, new JsonObject { ["error"] = new JsonObject { ["code"] = 400, ["message"] = $"Invalid requests[0].replaceImage: The object ({oid}) could not be found." } }.ToJsonString());
                lock (Files)
                {
                    if (!Files.TryGetValue(fileId, out var f) || !f.Shared) return Json(400, """{"error":{"code":400,"message":"Invalid requests[0].replaceImage: There was a problem retrieving the image."}}""");
                }
                if ((string?)ri["imageReplaceMethod"] != "CENTER_CROP") return Json(400, "{}");
            }
            foreach (var r in reqs)
            {
                lock (Replaced)
                {
                    Replaced.Add((string)r!["replaceImage"]!["imageObjectId"]!);
                    ReplacedIn.Add(target);
                }
            }
            return Json(200, """{"replies":[]}""");
        }
        if (url.StartsWith(GoogleApi.UploadBase, StringComparison.Ordinal))
        {
            string id = "file" + Interlocked.Increment(ref _next);
            string name = System.Text.RegularExpressions.Regex.Match(body, "\"name\":\"([^\"]+)\"").Groups[1].Value;
            lock (Files) Files[id] = (Encoding.Latin1.GetBytes(body), false, name);
            return Json(200, $$"""{"id":"{{id}}"}""");
        }
        if (req.Method == HttpMethod.Post && url.StartsWith(GoogleApi.DriveBase + "/", StringComparison.Ordinal) && url.Contains("/copy?"))
        {
            if (CopyFails is not null) return Json(403, CopyFails);
            string source = url[(GoogleApi.DriveBase.Length + 1)..].Split('/')[0];
            if (Picked is not null && !Picked.Contains(source)) return Json(404, """{"error":{"code":404,"message":"File not found."}}""");
            string id = "copy" + Interlocked.Increment(ref _next);
            string name = (string?)JsonNode.Parse(body)?["name"] ?? "";
            lock (Copies) Copies.Add((id, name, source));
            if (Picked is not null) lock (Picked) Picked.Add(id); // NACHO made it, so drive.file lets NACHO open it
            return Json(200, new JsonObject { ["id"] = id, ["name"] = name }.ToJsonString());
        }
        if (url.StartsWith(GoogleApi.DriveBase + "/", StringComparison.Ordinal) && url.Contains("/permissions"))
        {
            string id = url[(GoogleApi.DriveBase.Length + 1)..].Split('/')[0];
            var p = JsonNode.Parse(body)!;
            if ((string?)p["type"] != "anyone" || (string?)p["role"] != "reader") return Json(400, "{}");
            lock (Files)
            {
                if (!Files.TryGetValue(id, out var f)) return Json(404, "{}");
                Files[id] = (f.Data, true, f.Name);
            }
            return Json(200, """{"id":"anyoneWithLink"}""");
        }
        if (req.Method == HttpMethod.Delete && url.StartsWith(GoogleApi.DriveBase + "/", StringComparison.Ordinal))
        {
            string id = url[(GoogleApi.DriveBase.Length + 1)..];
            lock (Files) return Files.Remove(id) ? new HttpResponseMessage(HttpStatusCode.NoContent) : Json(404, "{}");
        }
        if (req.Method == HttpMethod.Get && url.StartsWith(GoogleApi.DriveBase + "?", StringComparison.Ordinal))
        {
            var arr = new JsonArray();
            lock (Files) foreach (var kv in Files) arr.Add(new JsonObject { ["id"] = kv.Key, ["name"] = kv.Value.Name });
            return Json(200, new JsonObject { ["files"] = arr }.ToJsonString());
        }
        if (url.StartsWith("https://www.googleapis.com/drive/v3/about", StringComparison.Ordinal)) return Json(200, """{"user":{"emailAddress":"tester@example.com"}}""");
        return Json(404, """{"error":{"code":404,"message":"unknown address"}}""");
    }

    /// <summary>The Doc as Google would return it for this ID: the same content for a copy (other IDs only when asked).</summary>
    public JsonObject DocFor(string docId)
    {
        bool isCopy;
        lock (Copies) isCopy = Copies.Any(c => c.Id == docId);
        if (!isCopy || !CopyChangesIds) return Doc;
        return (JsonObject)JsonNode.Parse(Doc.ToJsonString().Replace("\"kix.", "\"kixc.", StringComparison.Ordinal))!;
    }

    public HashSet<string> AllObjects() => AllObjects(Doc);

    public static HashSet<string> AllObjects(JsonObject doc)
    {
        var set = new HashSet<string>();
        void Walk(JsonArray? tabs)
        {
            foreach (var t in tabs ?? new JsonArray())
            {
                foreach (var key in new[] { "inlineObjects", "positionedObjects" })
                    if (t?["documentTab"]?[key] is JsonObject o) foreach (var kv in o) set.Add(kv.Key);
                Walk(t?["childTabs"] as JsonArray);
            }
        }
        Walk(doc["tabs"] as JsonArray);
        return set;
    }

    private static HttpResponseMessage Json(int code, string body) =>
        new((HttpStatusCode)code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // ---------- building a Doc ----------

    public static JsonObject Picture(string id, double wPt, double hPt, JsonObject? extra = null, bool picture = true)
    {
        var ip = new JsonObject { ["contentUri"] = ContentHost + id, ["cropProperties"] = new JsonObject() };
        if (extra is not null) foreach (var kv in extra) ip[kv.Key] = kv.Value?.DeepClone();
        var e = new JsonObject
        {
            ["size"] = new JsonObject
            {
                ["width"] = new JsonObject { ["magnitude"] = wPt, ["unit"] = "PT" },
                ["height"] = new JsonObject { ["magnitude"] = hPt, ["unit"] = "PT" },
            },
        };
        if (picture) e["imageProperties"] = ip;
        else e["embeddedDrawingProperties"] = new JsonObject();
        return e;
    }

    public static JsonObject Tab(string id, string title, JsonObject inline, JsonObject positioned, JsonArray? children = null)
    {
        var io = new JsonObject();
        foreach (var kv in inline) io[kv.Key] = new JsonObject { ["objectId"] = kv.Key, ["inlineObjectProperties"] = new JsonObject { ["embeddedObject"] = kv.Value!.DeepClone() } };
        var po = new JsonObject();
        foreach (var kv in positioned) po[kv.Key] = new JsonObject { ["objectId"] = kv.Key, ["positionedObjectProperties"] = new JsonObject { ["embeddedObject"] = kv.Value!.DeepClone() } };
        var t = new JsonObject
        {
            ["tabProperties"] = new JsonObject { ["tabId"] = id, ["title"] = title },
            ["documentTab"] = new JsonObject { ["inlineObjects"] = io, ["positionedObjects"] = po },
        };
        if (children is not null) t["childTabs"] = children;
        return t;
    }

    public static byte[] Png(int w, int h)
    {
        using var img = new MagickImage("plasma:", new MagickReadSettings { Width = (uint)w, Height = (uint)h });
        img.Format = MagickFormat.Png;
        return img.ToByteArray();
    }

    // ---------- for the self-test ----------

    /// <summary>Plays the browser: answers the sign-in page at once, as if the user allowed both permissions.</summary>
    public static bool AnswerSignIn(string url)
    {
        var q = Query(url);
        if (!q.TryGetValue("redirect_uri", out string? redirect) || !q.TryGetValue("state", out string? state)) return false;
        string extra = "";
        string code = "c-1";
        if (q.TryGetValue("trigger_onepick", out string? pick) && pick == "true")
        {
            string id = q.TryGetValue("file_ids", out string? ids) && ids.Length > 0 ? ids.Split(',')[0] : PickerDefault;
            extra = "&picked_file_ids=" + Uri.EscapeDataString(id);
            code = "pick:" + id;
        }
        _ = Task.Run(async () =>
        {
            using var http = new HttpClient();
            try
            {
                await http.GetStringAsync($"{redirect}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}{extra}&scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fdrive.file").ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
            }
        });
        return true;
    }

    public static Dictionary<string, string> Query(string url)
    {
        var d = new Dictionary<string, string>();
        int i = url.IndexOf('?');
        if (i < 0) return d;
        foreach (string part in url[(i + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int e = part.IndexOf('=');
            if (e > 0) d[Uri.UnescapeDataString(part[..e])] = Uri.UnescapeDataString(part[(e + 1)..].Replace('+', ' '));
        }
        return d;
    }

    /// <summary>A small book like the real test Doc: photos in the text, wrapped pictures with a side crop,
    /// one picture that is already small and one drawing.</summary>
    public SampleGoogle WithBook(string title = "Plantas no nativas (sample)")
    {
        Pictures["p1"] = Png(1300, 2048);
        Pictures["p2"] = Png(1300, 2048);
        Pictures["w1"] = Png(1692, 2048);
        Pictures["w2"] = Png(1692, 2048);
        using (var small = new MagickImage("plasma:", new MagickReadSettings { Width = 400, Height = 300 }))
        {
            small.Format = MagickFormat.Jpeg;
            small.Quality = 80;
            Pictures["s1"] = small.ToByteArray();
        }
        var crop = new JsonObject { ["cropProperties"] = new JsonObject { ["offsetLeft"] = 0.031, ["offsetRight"] = 0.030 } };
        Doc = new JsonObject
        {
            ["title"] = title,
            ["tabs"] = new JsonArray(Tab("t.0", "Main",
                new JsonObject
                {
                    ["kix.p1"] = Picture("p1", 154.2, 243.0),
                    ["kix.p2"] = Picture("p2", 158.0, 249.0),
                    ["kix.s1"] = Picture("s1", 200, 150),
                    ["kix.d1"] = Picture("d1", 100, 100, picture: false),
                },
                new JsonObject
                {
                    ["kix.w1"] = Picture("w1", 215.6, 278.6, crop),
                    ["kix.w2"] = Picture("w2", 237.1, 287.6),
                })),
        };
        return this;
    }
}
