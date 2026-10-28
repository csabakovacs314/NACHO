using Nacho.Core;
using Nacho.Core.Google;

namespace Nacho.Linux;

/// <summary>The app's Google session and the small helpers both Google windows use.</summary>
public static class GoogleUi
{
    private static GoogleSession? _session;

    /// <summary>The sign-in file (mode 600) sits next to settings.json, so the self-test never sees the real one.</summary>
    public static string TokenPath => Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "google-signin");

    public static GoogleSession Session
    {
        get => _session ??= new GoogleSession(new FileTokenStore(TokenPath));
        set => _session = value;
    }

    /// <summary>Set by the self-test, which brings its own client and pretend Google.</summary>
    public static bool SelfTestClient { get; set; }

    /// <summary>Points the session at the client the settings ask for and sets the browser page texts.</summary>
    public static GoogleSession For(AppSettings s)
    {
        var session = Session;
        if (!SelfTestClient) session.Client = GoogleClientInfo.Choose(s.GoogleClientFile);
        session.PageDone = Loc.T("Gd.PageDone");
        session.PageFailed = Loc.T("Gd.PageFailed");
        return session;
    }

    public static string Message(Exception ex) => ex switch
    {
        GoogleException g when g.Kind is GoogleError.Server => Loc.F("Gd.ErrOther", g.Message),
        GoogleException g => Loc.T(GoogleText.ErrorKey(g.Kind)),
        OperationCanceledException => Loc.T("Gd.ErrCancelled"),
        _ => Loc.F("Gd.ErrOther", ex.Message),
    };

    /// <summary>Opens a Doc in the browser; the address is always rebuilt from the Doc's ID.</summary>
    public static void OpenDoc(string docId)
    {
        if (GoogleDocLink.TryGetId(GoogleDocLink.EditUrl(docId)) != docId) return;
        Shell.Open(GoogleDocLink.EditUrl(docId));
    }

    public static void OpenFolder(string folder)
    {
        if (Directory.Exists(folder)) Shell.Open(folder);
    }
}
