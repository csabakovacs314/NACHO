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

    private string? _releaseUrl;

    private async void Check_Click(object? sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        ReleaseButton.IsVisible = false;
        CheckText.IsVisible = true;
        CheckText.Text = Loc.T("Update.Checking");
        var current = UpdateCheck.Current(typeof(App).Assembly);
        var result = await UpdateCheck.CheckAsync(current);
        _s.LastUpdateCheckUtc = DateTime.UtcNow;
        _s.Save();
        CheckText.Text = result.Outcome switch
        {
            UpdateCheck.Outcome.Newer => Loc.F("Update.Available", result.Update!.Version.ToString(3), current.ToString(3)),
            UpdateCheck.Outcome.UpToDate => Loc.F("Update.UpToDate", current.ToString(3)),
            _ => Loc.T("Update.Failed"),
        };
        if (result.Outcome == UpdateCheck.Outcome.Newer)
        {
            _releaseUrl = result.Update!.PageUrl;
            ReleaseButton.IsVisible = true;
            if (Owner is MainWindow main) main.ShowUpdate(result.Update);
        }
        CheckButton.IsEnabled = true;
    }

    private void Release_Click(object? sender, RoutedEventArgs e)
    {
        if (_releaseUrl is not null) MainWindow.OpenWeb(_releaseUrl);
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
