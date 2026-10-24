using Avalonia;
using Avalonia.Media;
using Nacho.Core.Linux;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("linux")]

namespace Nacho.Linux;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "--version":
                    Console.WriteLine("NACHO " + App.Version);
                    return 0;
                case "--help":
                case "-h":
                    Console.WriteLine(Cli.Help);
                    return 0;
                case "--setup-desktop":
                    return Cli.SetupDesktop(args.Skip(1).ToArray());
                case "--remove-desktop":
                    return Cli.RemoveDesktop();
            }
        }

        bool selfTest = args.Length > 0 && args[0] == "--selftest";
        if (!selfTest && !SingleInstance.TryBecomePrimary(args)) return 0;

        App.Args = args;
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            Console.Error.WriteLine("NACHO could not start: " + ex.Message);
            Console.Error.WriteLine("Details: " + XdgPaths.Current.LogFile);
            return 2;
        }
        finally
        {
            SingleInstance.Stop();
        }
    }

    // Inter is bundled so the window looks the same on every distro; it is close to GNOME's own Adwaita Sans.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
        .LogToTrace();
}
