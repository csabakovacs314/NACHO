using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Nacho.Core;
using Nacho.Core.Google;

namespace Nacho;

/// <summary>Keeps the Google sign-in encrypted with Windows DPAPI: only this Windows user on this computer can read it.</summary>
public sealed class DpapiTokenStore : ITokenStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NACHO Google sign-in");
    private readonly string _path;

    public DpapiTokenStore(string path) => _path = path;

    public string? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain) is { Length: > 0 } t ? t : null;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string refreshToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllBytes(_path, ProtectedData.Protect(Encoding.UTF8.GetBytes(refreshToken), Entropy, DataProtectionScope.CurrentUser));
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>The app's Google session and the small helpers both Google windows use.</summary>
public static class GoogleUi
{
    private static GoogleSession? _session;

    /// <summary>The sign-in file sits next to settings.json, so the self-test never sees the real one.</summary>
    public static string TokenPath => Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "google-signin.bin");

    public static GoogleSession Session
    {
        get => _session ??= new GoogleSession(new DpapiTokenStore(TokenPath));
        set => _session = value;
    }

    /// <summary>Points the session at the client the settings ask for and sets the browser page texts.</summary>
    public static GoogleSession For(AppSettings s)
    {
        var session = Session;
        if (!SelfTestClient) session.Client = GoogleClientInfo.Choose(s.GoogleClientFile);
        session.PageDone = Loc.T("Gd.PageDone");
        session.PageFailed = Loc.T("Gd.PageFailed");
        return session;
    }

    /// <summary>Set by the self-test, which brings its own client and pretend Google.</summary>
    public static bool SelfTestClient { get; set; }

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
        try
        {
            Process.Start(new ProcessStartInfo(GoogleDocLink.EditUrl(docId)) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    public static void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder } });
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }
}
