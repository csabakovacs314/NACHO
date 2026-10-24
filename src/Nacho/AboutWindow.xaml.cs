using System.Diagnostics;
using System.Windows;
using Nacho.Core;

namespace Nacho;

public partial class AboutWindow : Window
{
    private readonly AppSettings _s;

    public AboutWindow(AppSettings settings)
    {
        _s = settings;
        InitializeComponent();
        var v = typeof(AboutWindow).Assembly.GetName().Version ?? new Version(0, 1, 0);
        VersionText.Text = Loc.F("About.Version", $"{v.Major}.{v.Minor}.{v.Build}");
        OtherExpansion.Text = string.Join(Environment.NewLine, Strings.Languages
            .Where(l => l != Loc.Instance.Language)
            .Select(l => Strings.For(l)["App.Expansion"]));
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_s.OutputFolder);
            Process.Start("explorer.exe", $"\"{_s.OutputFolder}\"");
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }
}
