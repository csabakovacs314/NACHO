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

    private string? _releaseUrl;

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        ReleaseButton.Visibility = Visibility.Collapsed;
        CheckText.Visibility = Visibility.Visible;
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
            ReleaseButton.Visibility = Visibility.Visible;
            if (Owner is MainWindow main) main.ShowUpdate(result.Update);
        }
        CheckButton.IsEnabled = true;
    }

    private void Release_Click(object sender, RoutedEventArgs e)
    {
        if (_releaseUrl is not null) MainWindow.OpenWeb(_releaseUrl);
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
