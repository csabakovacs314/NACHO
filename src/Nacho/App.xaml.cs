using System.Windows;

namespace Nacho;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            Log(ex.Exception);
            MessageBox.Show(ex.Exception.Message, "NACHO", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        string[] args = e.Args;
        if (args.Length >= 1 && args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int code;
            try
            {
                code = await SelfTest.RunAsync(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "nacho-selftest"));
            }
            catch (Exception ex)
            {
                Log(ex);
                code = 3;
            }
            Shutdown(code);
            return;
        }

        if (!SingleInstance.TryBecomePrimary(args))
        {
            Shutdown(0);
            return;
        }

        ApplyTheme("System");
        var settings = AppSettings.Load();
        Loc.Instance.Set(settings.Language);
        var window = new MainWindow(settings);
        MainWindow = window;
        window.Show();

        var files = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (files.Length > 0) window.ReceiveExternal(files);
        SingleInstance.StartServer(paths => Dispatcher.BeginInvoke(() => window.ReceiveExternal(paths)));
    }

    /// <summary>Fluent theme that follows Windows; the classic theme when High Contrast is on.</summary>
    public static void ApplyTheme(string mode)
    {
        try
        {
            if (SystemParameters.HighContrast)
            {
                Current.ThemeMode = ThemeMode.None;
                return;
            }
            Current.ThemeMode = mode switch
            {
                "Light" => ThemeMode.Light,
                "Dark" => ThemeMode.Dark,
                _ => ThemeMode.System,
            };
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    public static void Log(Exception ex)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NACHO");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "nacho.log"), $"{DateTime.Now:s} {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
