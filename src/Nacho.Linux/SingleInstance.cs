using System.Net.Sockets;
using System.Text;
using Nacho.Core.Linux;

namespace Nacho.Linux;

/// <summary>
/// One NACHO window per user. A second launch (Open With on several files, the right-click entry) hands its
/// file paths to the running copy through a Unix socket in $XDG_RUNTIME_DIR and exits.
/// </summary>
public static class SingleInstance
{
    private static Socket? _listener;
    private static string SocketPath => Path.Combine(XdgPaths.Current.RuntimeDir, $"nacho-{Environment.UserName}.sock");

    public static bool TryBecomePrimary(string[] args)
    {
        var endpoint = new UnixDomainSocketEndPoint(SocketPath);
        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(endpoint);
            string payload = args.Length == 0
                ? "--activate"
                : string.Join('\0', args.Select(a => a.StartsWith("--", StringComparison.Ordinal) ? a : Path.GetFullPath(a)));
            client.Send(Encoding.UTF8.GetBytes(payload));
            client.Shutdown(SocketShutdown.Send);
            return false;
        }
        catch (SocketException)
        {
            // Nobody is listening: the socket is missing or left over from a crash.
        }

        try
        {
            if (File.Exists(SocketPath)) File.Delete(SocketPath);
            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(endpoint);
            _listener.Listen(8);
        }
        catch (Exception ex)
        {
            // Without the socket NACHO still works; a second launch just opens a second window.
            App.Log(ex);
            _listener = null;
        }
        return true;
    }

    public static void StartServer(Action<string[]> onArgs)
    {
        if (_listener is null) return;
        var thread = new Thread(() =>
        {
            while (_listener is not null)
            {
                try
                {
                    using var conn = _listener.Accept();
                    using var stream = new NetworkStream(conn);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    string text = reader.ReadToEnd();
                    onArgs(text.Split('\0', StringSplitOptions.RemoveEmptyEntries));
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }
        })
        {
            IsBackground = true,
            Name = "NACHO socket",
        };
        thread.Start();
    }

    public static void Stop()
    {
        var l = _listener;
        _listener = null;
        if (l is null) return;
        try { l.Dispose(); } catch { }
        try { File.Delete(SocketPath); } catch { }
    }
}
