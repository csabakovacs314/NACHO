using System.IO.Pipes;

namespace Nacho;

/// <summary>
/// One NACHO window per user. A second launch (right-click on several files, Send to) hands its
/// file paths to the running copy through a named pipe and exits.
/// </summary>
public static class SingleInstance
{
    private static Mutex? _mutex;
    private static string Name => "NACHO-" + Environment.UserName;

    public static bool TryBecomePrimary(string[] args)
    {
        _mutex = new Mutex(true, @"Local\" + Name, out bool created);
        if (created) return true;

        string payload = args.Length == 0
            ? "--activate"
            : string.Join("\n", args.Select(a => a.StartsWith("--", StringComparison.Ordinal) ? a : Path.GetFullPath(a)));
        for (int attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out);
                client.Connect(1000);
                using var writer = new StreamWriter(client);
                writer.Write(payload);
                writer.Flush();
                return false;
            }
            catch (TimeoutException) { }
            catch (IOException) { Thread.Sleep(150); }
        }
        return false;
    }

    public static void StartServer(Action<string[]> onArgs)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(Name, PipeDirection.In, 1);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    string text = reader.ReadToEnd();
                    onArgs(text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }
        })
        {
            IsBackground = true,
            Name = "NACHO pipe",
        };
        thread.Start();
    }
}
