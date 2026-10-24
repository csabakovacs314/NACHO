using Avalonia.Controls;
using Avalonia.Interactivity;
using Nacho.Core;

namespace Nacho.Linux;

public partial class AboutWindow : Window
{
    private readonly AppSettings _s;

    public AboutWindow() : this(new AppSettings()) { }

    public AboutWindow(AppSettings settings)
    {
        _s = settings;
        InitializeComponent();
        Icon = App.Icon();
        VersionText.Text = Loc.F("About.Version", App.Version) + " · Linux";
        OtherExpansion.Text = string.Join(Environment.NewLine, Strings.Languages
            .Where(l => l != Loc.Instance.Language)
            .Select(l => Strings.For(l)["App.Expansion"]));
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_s.OutputFolder);
            Shell.Open(_s.OutputFolder);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
