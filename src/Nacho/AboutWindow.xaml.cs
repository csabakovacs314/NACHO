using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
        BuildWays();
        BuildKeys();
    }

    public const string PrivacyUrl = "https://csabakovacs314.github.io/privacy.html";

    /// <summary>The four ways in, each with a numbered dot like the Google Doc steps.</summary>
    private void BuildWays()
    {
        for (int i = 1; i <= 4; i++)
        {
            var dot = new Border
            {
                Style = (Style)FindResource("StepDot"),
                Width = 22,
                Height = 22,
                Child = new TextBlock { Text = i.ToString(Loc.Instance.Culture), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var text = new TextBlock { Text = Loc.T("About.Way" + i), FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(text);
            AutomationProperties.SetName(row, $"{i}. {text.Text}");
            WaysList.Children.Add(row);
        }
    }

    /// <summary>"Ctrl+O add · Ctrl+V paste · …" shown as key caps, two columns.</summary>
    private void BuildKeys()
    {
        foreach (string entry in Loc.T("About.KeysList").Split(" · ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int space = entry.IndexOf(' ');
            string key = space > 0 ? entry[..space] : entry;
            string what = space > 0 ? entry[(space + 1)..] : "";
            var cap = new Border
            {
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 8, 0),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1, 1, 1, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = key, FontSize = 12 },
            };
            cap.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            cap.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 6) };
            row.Children.Add(cap);
            row.Children.Add(new TextBlock { Text = what, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            AutomationProperties.SetName(row, key + ": " + what);
            KeysGrid.Children.Add(row);
        }
    }

    private void Privacy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(PrivacyUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    /// <summary>Colours the version strip by the check's outcome; the icon and text carry the meaning too.</summary>
    private void ShowOutcome(string background, string border, string iconBrush, string glyph)
    {
        VersionStrip.SetResourceReference(Border.BackgroundProperty, background);
        VersionStrip.SetResourceReference(Border.BorderBrushProperty, border);
        VersionIcon.SetResourceReference(TextBlock.ForegroundProperty, iconBrush);
        VersionIcon.Text = glyph;
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
        switch (result.Outcome)
        {
            case UpdateCheck.Outcome.UpToDate:
                ShowOutcome("SystemFillColorSuccessBackgroundBrush", "SystemFillColorSuccessBrush", "SystemFillColorSuccessBrush", "\uE930");
                break;
            case UpdateCheck.Outcome.Newer:
                ShowOutcome("SystemFillColorAttentionBackgroundBrush", "AccentFillColorDefaultBrush", "AccentTextFillColorPrimaryBrush", "\uE946");
                break;
            default:
                ShowOutcome("SystemFillColorCautionBackgroundBrush", "SystemFillColorCautionBrush", "SystemFillColorCautionBrush", "\uE7BA");
                break;
        }
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
