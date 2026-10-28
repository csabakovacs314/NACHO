namespace Nacho.Core.Google;

/// <summary>
/// The app's one connection to Google: which sign-in client is used, where the sign-in is kept, and who is signed in.
/// Both apps hold one; the self-test gives it a <see cref="SampleGoogle"/> instead of the network.
/// </summary>
public sealed class GoogleSession
{
    private readonly ITokenStore _store;
    private readonly HttpMessageHandler? _handler;
    private GoogleAuth? _auth;
    private GoogleClientInfo? _client;

    public GoogleSession(ITokenStore store, HttpMessageHandler? handler = null)
    {
        _store = store;
        _handler = handler;
    }

    /// <summary>The client NACHO signs in with; null when this build has none and no own client file is set.</summary>
    public GoogleClientInfo? Client
    {
        get => _client;
        set
        {
            if (value == _client) return;
            _client = value;
            _auth = null;
        }
    }

    public bool IsAvailable => Client is not null;

    public bool HasSignIn => _store.Load() is not null;

    public string PageDone { get; set; } = "Signed in. You can close this tab and go back to NACHO.";
    public string PageFailed { get; set; } = "The sign-in did not finish. Go back to NACHO and try again.";

    /// <summary>Lets the self-test answer the browser step itself.</summary>
    public Func<string, bool>? OpenBrowser { get; set; }

    /// <summary>Waits between retries (the self-test makes it instant).</summary>
    public Func<TimeSpan, CancellationToken, Task>? Delay { get; set; }

    public GoogleAuth Auth
    {
        get
        {
            var client = Client ?? throw new GoogleException(GoogleError.NotSetUp, "No Google sign-in client.");
            _auth ??= new GoogleAuth(client, _store, _handler);
            _auth.PageDone = PageDone;
            _auth.PageFailed = PageFailed;
            if (OpenBrowser is not null) _auth.OpenBrowser = OpenBrowser;
            return _auth;
        }
    }

    public GoogleApi Api()
    {
        var api = new GoogleApi(Auth);
        if (Delay is not null) api.Delay = Delay;
        return api;
    }

    /// <summary>Signs in through the browser and returns the account's e-mail address (or "" if Google does not say).</summary>
    public async Task<string> SignInAsync(CancellationToken ct = default)
    {
        await Auth.SignInAsync(TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
        return await EmailAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Opens Google's file picker in the browser (signing in if needed) and returns the chosen Doc's ID and
    /// the account's e-mail address. With <paramref name="docId"/>, the picker shows just that Doc.</summary>
    public async Task<(string DocId, string Email)> PickAsync(string? docId, CancellationToken ct = default)
    {
        bool choose = ChooseAccountNext;
        var picked = await Auth.SignInAsync(TimeSpan.FromMinutes(10), ct, pick: true, fileId: docId, chooseAccount: choose).ConfigureAwait(false);
        ChooseAccountNext = false;
        return (picked[0], await EmailAsync(ct).ConfigureAwait(false));
    }

    private async Task<string> EmailAsync(CancellationToken ct)
    {
        try
        {
            return await Api().AccountEmailAsync(ct).ConfigureAwait(false) ?? "";
        }
        catch (GoogleException)
        {
            return "";
        }
    }

    /// <summary>Forgets the sign-in on this computer only (Google keeps NACHO's access to the Docs already picked).</summary>
    public void Forget() => _store.Clear();

    /// <summary>Set by "Use another account": the next picker shows Google's account chooser.</summary>
    public bool ChooseAccountNext { get; set; }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        if (Client is null)
        {
            _store.Clear();
            return;
        }
        await Auth.SignOutAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>Which message the apps show for a Google problem (keys in <see cref="Strings"/>).</summary>
public static class GoogleText
{
    /// <summary>True when NACHO may simply not have been given this Doc yet: the picker can fix that.</summary>
    public static bool NeedsPick(GoogleError kind) => kind is GoogleError.NotFound or GoogleError.NoPermission;

    public static string ErrorKey(GoogleError kind) => kind switch
    {
        GoogleError.SignInNeeded => "Gd.ErrSignIn",
        GoogleError.NotFound => "Gd.ErrNotFound",
        GoogleError.NoPermission => "Gd.ErrNoPermission",
        GoogleError.ReadOnly => "Gd.ErrReadOnly",
        GoogleError.Quota => "Gd.ErrQuota",
        GoogleError.Network => "Gd.ErrNetwork",
        GoogleError.Refused => "Gd.ErrRefused",
        GoogleError.Cancelled => "Gd.ErrCancelled",
        GoogleError.NotSetUp => "Gd.NotSetUp",
        GoogleError.DiskFull => "Gd.ErrDiskFull",
        GoogleError.DriveFull => "Gd.ErrDriveFull",
        GoogleError.CopyRefused => "Gd.ErrCopyRefused",
        _ => "Gd.ErrOther",
    };

    /// <summary>The copy's name: the Doc's title (trimmed; Docs titles can end in a space) in the language's pattern.</summary>
    public static string CopyName(string pattern, string title) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, string.IsNullOrWhiteSpace(title) ? "Google Doc" : title.Trim());

    /// <summary>A short duration as a string key and its numbers: "45 s", "3 min 20 s", "12 min".</summary>
    public static (string Key, object[] Args) Duration(TimeSpan t)
    {
        int s = Math.Max(1, (int)Math.Round(t.TotalSeconds));
        if (s < 60) return ("Gd.Sec", new object[] { s });
        int m = s / 60, r = s % 60;
        if (m >= 10 || r == 0) return ("Gd.Min", new object[] { (int)Math.Round(t.TotalMinutes) });
        return ("Gd.MinSec", new object[] { m, r });
    }

    /// <summary>Why a picture is left alone, as a string key.</summary>
    public static string SkipKey(GoogleSkip skip) => skip switch
    {
        GoogleSkip.Linked => "Gd.SkipLinked",
        GoogleSkip.Effects => "Gd.SkipEffects",
        _ => "Gd.SkipDrawing",
    };

    /// <summary>A rough time for replacing pictures (measured: about 2.5 s a picture on a real book, plus the copy).</summary>
    public static TimeSpan ReplaceEstimate(int pictures, bool copy) =>
        TimeSpan.FromSeconds(pictures * 2.5 + (copy ? 25 : 0) + 5);

    /// <summary>Time left from the work done so far; null until there is enough to tell.</summary>
    public static TimeSpan? TimeLeft(TimeSpan elapsed, int done, int total)
    {
        if (done < 3 || total <= done || elapsed.TotalSeconds < 2) return done >= total && total > 0 ? TimeSpan.Zero : null;
        return TimeSpan.FromSeconds(elapsed.TotalSeconds / done * (total - done));
    }

    /// <summary>Percent smaller, rounded down so "100%" never shows for a picture that still has bytes.</summary>
    public static int PercentSmaller(long before, long after) =>
        before <= 0 || after >= before ? 0 : (int)Math.Floor(100.0 * (before - after) / before);
}

/// <summary>One Doc NACHO shrank, for the "Recently shrunk" list (newest first, a few kept).</summary>
public sealed record GoogleRecent(string DocId, string Title, DateTime When, int Replaced, long BytesBefore, long BytesAfter, bool Copy)
{
    public const int Keep = 5;

    public static List<GoogleRecent> Add(IEnumerable<GoogleRecent>? list, GoogleRecent item) =>
        new[] { item }.Concat((list ?? Enumerable.Empty<GoogleRecent>()).Where(r => r is not null && r.DocId != item.DocId)).Take(Keep).ToList();
}
