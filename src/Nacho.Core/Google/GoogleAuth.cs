using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Nacho.Core.Google;

/// <summary>The Google sign-in client NACHO identifies itself with (a "Desktop app" OAuth client).</summary>
public sealed record GoogleClientInfo(string ClientId, string ClientSecret)
{
    /// <summary>Name of the client file the release build embeds; it is never part of the source.</summary>
    public const string ResourceName = "Nacho.GoogleClient.json";

    /// <summary>Reads Google's client file ({"installed": {"client_id", "client_secret"}}); null when it is not one.</summary>
    public static GoogleClientInfo? Parse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            var c = root?["installed"];
            string? id = (string?)c?["client_id"], secret = (string?)c?["client_secret"];
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret)) return null;
            if (!id.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal)) return null;
            return new GoogleClientInfo(id, secret);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static GoogleClientInfo? FromFile(string path)
    {
        try
        {
            var f = new FileInfo(path);
            return f.Exists && f.Length < 64 * 1024 ? Parse(File.ReadAllText(path)) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static readonly Lazy<GoogleClientInfo?> BuiltInClient = new(() =>
    {
        using var s = typeof(GoogleClientInfo).Assembly.GetManifestResourceStream(ResourceName);
        if (s is null) return null;
        using var r = new StreamReader(s);
        return Parse(r.ReadToEnd());
    });

    /// <summary>The client built into this NACHO, or null in builds made without one.</summary>
    public static GoogleClientInfo? BuiltIn => BuiltInClient.Value;

    /// <summary>The user's own client file when one is set and valid, otherwise the built-in client.</summary>
    public static GoogleClientInfo? Choose(string? ownFile) =>
        (string.IsNullOrWhiteSpace(ownFile) ? null : FromFile(ownFile)) ?? BuiltIn;
}

/// <summary>Where the sign-in (the refresh token) is kept. The apps encrypt it; tests keep it in memory.</summary>
public interface ITokenStore
{
    string? Load();
    void Save(string refreshToken);
    void Clear();
}

public sealed class MemoryTokenStore : ITokenStore
{
    private string? _token;
    public string? Load() => _token;
    public void Save(string refreshToken) => _token = refreshToken;
    public void Clear() => _token = null;
}

/// <summary>Linux: the sign-in in a file only the user can read (mode 600).</summary>
public sealed class FileTokenStore : ITokenStore
{
    private readonly string _path;

    public FileTokenStore(string path) => _path = path;

    public string? Load()
    {
        try
        {
            return File.Exists(_path) ? File.ReadAllText(_path).Trim() is { Length: > 0 } t ? t : null : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string refreshToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        string tmp = _path + ".new";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var fs = new FileStream(tmp, options))
        using (var w = new StreamWriter(fs))
        {
            w.Write(refreshToken);
        }
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, _path, overwrite: true);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch (IOException)
        {
            // A sign-in file that can't be deleted is still ignored once Google revokes it.
        }
    }
}

public enum GoogleError { SignInNeeded, NotFound, NoPermission, ReadOnly, Quota, Network, Server, Refused, Cancelled, NotSetUp, DiskFull, DriveFull, CopyRefused }

public sealed class GoogleException : Exception
{
    public GoogleError Kind { get; }
    public int Status { get; }

    public GoogleException(GoogleError kind, string message, int status = 0, Exception? inner = null) : base(message, inner)
    {
        Kind = kind;
        Status = status;
    }
}

/// <summary>
/// Signs in with Google the way desktop apps should: the browser opens Google's own page (NACHO never sees the
/// password), Google sends the answer back to a one-time address on this computer (127.0.0.1, a random port),
/// and the code is protected with PKCE. Only the refresh token is kept, through an <see cref="ITokenStore"/>.
/// </summary>
public sealed class GoogleAuth
{
    /// <summary>Only "the files you choose" (non-sensitive): the Docs picked in Google's picker and the files NACHO creates.</summary>
    public const string Scopes = "https://www.googleapis.com/auth/drive.file";

    public const string DocMime = "application/vnd.google-apps.document";

    /// <summary>A Word file (.docx) stored in Drive.</summary>
    public const string WordMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

    private readonly GoogleClientInfo _client;
    private readonly ITokenStore _store;
    private string? _access;
    private DateTime _accessUntil;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public HttpClient Http { get; }

    /// <summary>Opens the sign-in page; the default uses the system browser. Only Google's sign-in address is ever passed.</summary>
    public Func<string, bool> OpenBrowser { get; set; } = OpenInSystemBrowser;

    /// <summary>The page the browser shows after signing in (the apps set it in the user's language).</summary>
    public string PageDone { get; set; } = "Signed in. You can close this tab and go back to NACHO.";
    public string PageFailed { get; set; } = "The sign-in did not finish. Go back to NACHO and try again.";

    public GoogleAuth(GoogleClientInfo client, ITokenStore store, HttpMessageHandler? handler = null)
    {
        _client = client;
        _store = store;
        Http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        Http.Timeout = TimeSpan.FromMinutes(5);
    }

    public bool HasSignIn => _store.Load() is not null;

    public static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>The sign-in address; with <paramref name="pick"/> Google shows its file picker (only Google Docs,
    /// pre-filtered to <paramref name="fileId"/> when NACHO already knows which Doc).</summary>
    public string AuthUrl(string redirect, string challenge, string state, bool pick = false, string? fileId = null, bool chooseAccount = false,
        string mimeTypes = DocMime)
    {
        var q = new Dictionary<string, string>
        {
            ["client_id"] = _client.ClientId,
            ["redirect_uri"] = redirect,
            ["response_type"] = "code",
            ["scope"] = Scopes,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = chooseAccount ? "select_account consent" : "consent",
        };
        if (pick)
        {
            q["trigger_onepick"] = "true";
            q["mimetypes"] = mimeTypes;
            if (!string.IsNullOrEmpty(fileId)) q["file_ids"] = fileId;
        }
        return AuthEndpoint + "?" + string.Join("&", q.Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value)));
    }

    /// <summary>Opens Google's sign-in page (and with <paramref name="pick"/> its file picker) and waits for the answer.
    /// Returns the IDs of the files the user picked (empty for a plain sign-in).</summary>
    public async Task<IReadOnlyList<string>> SignInAsync(TimeSpan timeout, CancellationToken ct = default, bool pick = false, string? fileId = null, bool chooseAccount = false,
        string mimeTypes = DocMime)
    {
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        string state = Base64Url(RandomNumberGenerator.GetBytes(16));
        int port = FreePort();
        string redirect = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();
        string url = AuthUrl(redirect, Challenge(verifier), state, pick, fileId, chooseAccount, mimeTypes);
        if (!OpenBrowser(url)) throw new GoogleException(GoogleError.Network, "The browser could not be opened.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        HttpListenerContext ctx;
        try
        {
            ctx = await listener.GetContextAsync().WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new GoogleException(GoogleError.Cancelled, ct.IsCancellationRequested ? "Sign-in cancelled." : "No answer from the sign-in page.");
        }

        string? code = ctx.Request.QueryString["code"], gotState = ctx.Request.QueryString["state"], error = ctx.Request.QueryString["error"];
        var picked = (ctx.Request.QueryString["picked_file_ids"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool ok = code is not null && gotState == state && (!pick || picked.Length > 0);
        byte[] page = Encoding.UTF8.GetBytes("<!doctype html><html><head><meta charset=\"utf-8\"><title>NACHO</title></head>"
            + "<body style=\"font-family:Segoe UI,system-ui,sans-serif;padding:40px;line-height:1.5\"><h1 style=\"font-size:22px\">NACHO</h1><p>"
            + WebUtility.HtmlEncode(ok ? PageDone : PageFailed) + "</p></body></html>");
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = page.Length;
        await ctx.Response.OutputStream.WriteAsync(page, ct).ConfigureAwait(false);
        ctx.Response.Close();

        if (code is null) throw new GoogleException(error == "access_denied" ? GoogleError.Refused : GoogleError.Cancelled, "Sign-in not finished: " + (error ?? "no code"));
        if (gotState != state) throw new GoogleException(GoogleError.Refused, "The sign-in answer did not match this request.");
        if (pick && picked.Length == 0) throw new GoogleException(GoogleError.Cancelled, "No Doc was chosen.");

        var tok = await PostFormAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["client_id"] = _client.ClientId,
            ["client_secret"] = _client.ClientSecret,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirect,
            ["grant_type"] = "authorization_code",
        }, ct).ConfigureAwait(false);
        string scope = (string?)tok["scope"] ?? "";
        if (!scope.Contains("auth/drive.file", StringComparison.Ordinal))
        {
            throw new GoogleException(GoogleError.Refused, "NACHO needs access to the files you choose.");
        }
        string refresh = (string?)tok["refresh_token"] ?? throw new GoogleException(GoogleError.Server, "Google did not return a sign-in that lasts.");
        _store.Save(refresh);
        Remember(tok);
        return picked;
    }

    /// <summary>A short-lived access token, renewed from the stored sign-in when needed.</summary>
    public async Task<string> AccessTokenAsync(CancellationToken ct = default, bool forceRefresh = false)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _access is not null && DateTime.UtcNow < _accessUntil) return _access;
            string refresh = _store.Load() ?? throw new GoogleException(GoogleError.SignInNeeded, "Not signed in.");
            JsonNode tok;
            try
            {
                tok = await PostFormAsync(TokenEndpoint, new Dictionary<string, string>
                {
                    ["client_id"] = _client.ClientId,
                    ["client_secret"] = _client.ClientSecret,
                    ["refresh_token"] = refresh,
                    ["grant_type"] = "refresh_token",
                }, ct).ConfigureAwait(false);
            }
            catch (GoogleException ex) when (ex.Status is 400 or 401)
            {
                // invalid_grant: the sign-in was revoked or expired (Testing mode: after 7 days).
                _store.Clear();
                throw new GoogleException(GoogleError.SignInNeeded, "The Google sign-in has expired.", ex.Status, ex);
            }
            return Remember(tok);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Forgets the sign-in here and asks Google to revoke it.</summary>
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        string? refresh = _store.Load();
        _store.Clear();
        _access = null;
        if (refresh is null) return;
        try
        {
            using var resp = await Http.PostAsync(RevokeEndpoint, new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refresh }), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Offline: the sign-in is gone from this computer; the user can also remove NACHO at myaccount.google.com.
        }
    }

    private string Remember(JsonNode tok)
    {
        _access = (string?)tok["access_token"] ?? throw new GoogleException(GoogleError.Server, "No access token in Google's answer.");
        int seconds = (int?)tok["expires_in"] ?? 3600;
        _accessUntil = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 120));
        return _access;
    }

    private async Task<JsonNode> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try
        {
            resp = await Http.PostAsync(url, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new GoogleException(GoogleError.Network, ex.Message, 0, ex);
        }
        using (resp)
        {
            string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                // Google's error text for the token endpoint never contains the token itself.
                throw new GoogleException(GoogleError.Server, $"Sign-in server said {(int)resp.StatusCode}: {GoogleApi.ErrorText(body)}", (int)resp.StatusCode);
            }
            return JsonNode.Parse(body) ?? throw new GoogleException(GoogleError.Server, "Empty answer from the sign-in server.");
        }
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private static bool OpenInSystemBrowser(string url)
    {
        if (!url.StartsWith(AuthEndpoint + "?", StringComparison.Ordinal)) return false;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else
            {
                var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                psi.ArgumentList.Add(url);
                Process.Start(psi);
            }
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
