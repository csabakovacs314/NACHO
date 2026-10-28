using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using Microsoft.Win32;
using Nacho.Core;
using Nacho.Core.Google;

namespace Nacho;

public partial class OptionsWindow : Window
{
    private static readonly string[] Ids = { "small", "balanced", "sharp" };
    private readonly AppSettings _s;

    private TextBox[] Edges => new[] { EdgeSmall, EdgeBalanced, EdgeSharp };
    private TextBox[] Budgets => new[] { BudgetSmall, BudgetBalanced, BudgetSharp };
    private RadioButton[] Defaults => new[] { DefSmall, DefBalanced, DefSharp };
    private TextBlock[] Names => new[] { NameSmall, NameBalanced, NameSharp };

    public OptionsWindow(AppSettings settings)
    {
        _s = settings;
        InitializeComponent();
        Fill(settings);
        LabelPresets();
        RenderGoogle();
        NavGoogle.IsChecked = true;
        SizeChanged += (_, _) => ApplyLayout();
    }

    // ---------- sections ----------

    private bool _navScrolling;

    private void ApplyLayout()
    {
        bool narrow = ActualWidth < 640;
        NavPane.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        SaveHint.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
    }

    private FrameworkElement? Section(object? tag) => tag is string name ? FindName(name) as FrameworkElement : null;

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (Section((sender as FrameworkElement)?.Tag) is { } section) ShowSection(section);
    }

    /// <summary>Scrolls a section's card to the top of the page.</summary>
    private void ShowSection(FrameworkElement section)
    {
        _navScrolling = true;
        double top = section.TransformToAncestor(Sections).Transform(new Point(0, 0)).Y;
        Scroller.ScrollToVerticalOffset(Math.Max(0, top));
        Dispatcher.BeginInvoke(() => _navScrolling = false, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    /// <summary>While you scroll, the section list marks the card at the top.</summary>
    private void Scroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_navScrolling || !IsLoaded) return;
        RadioButton? current = null;
        foreach (var nav in NavList.Children.OfType<RadioButton>())
        {
            if (Section(nav.Tag) is not { } section) continue;
            double top = section.TransformToAncestor(Scroller).Transform(new Point(0, 0)).Y;
            if (top <= 48 || current is null) current = nav;
            if (top > 48) break;
        }
        if (Scroller.VerticalOffset >= Scroller.ScrollableHeight - 1 && Scroller.ScrollableHeight > 0)
            current = NavList.Children.OfType<RadioButton>().Last();
        if (current is not null && current.IsChecked != true) current.IsChecked = true;
    }

    private void Fill(AppSettings s)
    {
        for (int i = 0; i < 3; i++)
        {
            Edges[i].Text = s.Presets[i].LongEdge.ToString(CultureInfo.InvariantCulture);
            Budgets[i].Text = s.Presets[i].BudgetKB.ToString(CultureInfo.InvariantCulture);
            Defaults[i].IsChecked = s.CurrentPreset == Ids[i];
        }
        FmtAuto.IsChecked = s.Format == FormatMode.Auto;
        FmtJpeg.IsChecked = s.Format == FormatMode.Jpeg;
        FmtPng.IsChecked = s.Format == FormatMode.Png;
        PrivLocation.IsChecked = s.RemoveLocation;
        PrivCamera.IsChecked = s.RemoveCamera;
        UpdatesBox.IsChecked = s.CheckUpdates;
        FolderBox.Text = s.OutputFolder;
        SuffixBox.Text = s.Suffix;
        CtxMenu.IsChecked = s.ContextMenu;
        SendToBox.IsChecked = s.SendTo;
        LangBox.SelectedIndex = Math.Max(0, Array.IndexOf(Strings.Languages, s.Language));
        GoogleKeepBox.IsChecked = s.GoogleKeepOriginals;
        (s.GoogleIntoCopy ? DestCopy : DestDoc).IsChecked = true;
        ClientFileBox.Text = s.GoogleClientFile;
        bool builtIn = GoogleClientInfo.BuiltIn is not null || GoogleUi.SelfTestClient;
        ClientBuiltIn.IsEnabled = builtIn;
        (builtIn && string.IsNullOrWhiteSpace(s.GoogleClientFile) ? ClientBuiltIn : ClientOwn).IsChecked = true;
        GoogleAdvanced.IsExpanded = !builtIn || !string.IsNullOrWhiteSpace(s.GoogleClientFile);
        ClientNote.Text = builtIn ? "" : Loc.T("Opt.GoogleClientNone");
        ClientNote.Visibility = builtIn ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- Google Docs ----------

    /// <summary>The client file the dialog currently points at ("" = built in).</summary>
    private string ChosenClientFile => ClientOwn.IsChecked == true ? ClientFileBox.Text.Trim() : "";

    public string GoogleStatusText => GoogleStatus.Text;

    /// <summary>For the self-test's screenshot of the Google Docs card.</summary>
    public void ScrollToGoogle()
    {
        GoogleAdvanced.IsExpanded = true;
        NavGoogle.IsChecked = true;
        UpdateLayout();
        ShowSection(SecGoogle);
    }

    private void RenderGoogle()
    {
        var session = GoogleUi.Session;
        bool signedIn = session.HasSignIn;
        GoogleStatus.Text = !signedIn ? Loc.T("Opt.GoogleSignedOut")
            : string.IsNullOrEmpty(_s.GoogleEmail) ? Loc.T("Gd.SignedIn") : Loc.F("Gd.SignedInAs", _s.GoogleEmail);
        GoogleSignInButton.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        GoogleSignOutButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowGoogleProblem(string? text)
    {
        GoogleProblem.Text = text ?? "";
        GoogleProblem.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        if (text is not null)
        {
            var peer = UIElementAutomationPeer.FromElement(GoogleProblem) ?? UIElementAutomationPeer.CreatePeerForElement(GoogleProblem);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    /// <summary>Signs in now (not on Save), with the client chosen in this dialog.</summary>
    public async Task SignInAsync()
    {
        ShowGoogleProblem(null);
        string file = ChosenClientFile;
        if (ClientOwn.IsChecked == true && (file.Length == 0 || GoogleClientInfo.FromFile(file) is null))
        {
            ShowGoogleProblem(Loc.T("Opt.GoogleClientBad"));
            return;
        }
        _s.GoogleClientFile = file;
        var session = GoogleUi.For(_s);
        if (!session.IsAvailable)
        {
            ShowGoogleProblem(Loc.T("Gd.NotSetUp"));
            return;
        }
        GoogleSignInButton.IsEnabled = false;
        GoogleStatus.Text = Loc.T("Gd.Signing");
        try
        {
            string email = await Task.Run(() => session.SignInAsync());
            _s.GoogleEmail = email;
            _s.Save();
        }
        catch (Exception ex) when (ex is GoogleException or System.Net.Http.HttpRequestException or System.Net.HttpListenerException)
        {
            App.Log(ex);
            ShowGoogleProblem(GoogleUi.Message(ex));
        }
        finally
        {
            GoogleSignInButton.IsEnabled = true;
            RenderGoogle();
        }
    }

    private async void GoogleSignIn_Click(object sender, RoutedEventArgs e) => await SignInAsync();

    private async void GoogleSignOut_Click(object sender, RoutedEventArgs e)
    {
        ShowGoogleProblem(null);
        GoogleSignOutButton.IsEnabled = false;
        try
        {
            await GoogleUi.For(_s).SignOutAsync();
        }
        catch (GoogleException ex)
        {
            App.Log(ex);
        }
        finally
        {
            _s.GoogleEmail = "";
            _s.Save();
            GoogleSignOutButton.IsEnabled = true;
            RenderGoogle();
            GoogleSignInButton.Focus();
        }
    }

    private void Client_Checked(object sender, RoutedEventArgs e) => ShowGoogleProblem(null);

    private void ChooseClient_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = Loc.T("Dlg.ClientTitle"), Filter = Loc.T("Dlg.ClientFilter") };
        if (dlg.ShowDialog(this) != true) return;
        if (GoogleClientInfo.FromFile(dlg.FileName) is null)
        {
            ShowGoogleProblem(Loc.T("Opt.GoogleClientBad"));
            return;
        }
        ClientFileBox.Text = dlg.FileName;
        ClientOwn.IsChecked = true;
        ShowGoogleProblem(null);
    }

    private void LabelPresets()
    {
        for (int i = 0; i < 3; i++)
        {
            string name = Loc.T("Preset." + Ids[i]);
            Names[i].Text = name;
            AutomationProperties.SetName(Edges[i], Loc.F("Opt.EdgeName", name));
            AutomationProperties.SetName(Budgets[i], Loc.F("Opt.BudgetName", name));
            AutomationProperties.SetName(Defaults[i], Loc.F("Opt.DefaultName", name));
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        Control? firstBad = null;
        var edges = new int[3];
        var budgets = new int[3];
        for (int i = 0; i < 3; i++)
        {
            string name = Loc.T("Preset." + Ids[i]);
            if (!int.TryParse(Edges[i].Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out edges[i])
                || edges[i] < 320 || edges[i] > 8000)
            {
                errors.Add(Loc.F("Opt.ErrEdge", name));
                firstBad ??= Edges[i];
            }
            if (!int.TryParse(Budgets[i].Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out budgets[i])
                || budgets[i] < 30 || budgets[i] > 20000)
            {
                errors.Add(Loc.F("Opt.ErrBudget", name));
                firstBad ??= Budgets[i];
            }
        }
        string suffix = SuffixBox.Text.Trim();
        if (suffix.Length == 0 || suffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            errors.Add(Loc.T("Opt.ErrSuffix"));
            firstBad ??= SuffixBox;
        }
        if (string.IsNullOrWhiteSpace(FolderBox.Text))
        {
            errors.Add(Loc.T("Opt.ErrFolder"));
            firstBad ??= FolderBox;
        }

        if (errors.Count > 0)
        {
            ErrorText.Text = string.Join(Environment.NewLine, errors);
            ErrorText.Visibility = Visibility.Visible;
            var peer = UIElementAutomationPeer.FromElement(ErrorText) ?? UIElementAutomationPeer.CreatePeerForElement(ErrorText);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            firstBad?.Focus();
            return;
        }

        if (ClientOwn.IsChecked == true && GoogleClientInfo.FromFile(ChosenClientFile) is null)
        {
            ShowGoogleProblem(Loc.T("Opt.GoogleClientBad"));
            GoogleAdvanced.IsExpanded = true;
            ClientOwn.Focus();
            return;
        }
        for (int i = 0; i < 3; i++)
        {
            _s.Presets[i].LongEdge = edges[i];
            _s.Presets[i].BudgetKB = budgets[i];
            if (Defaults[i].IsChecked == true) _s.CurrentPreset = Ids[i];
        }
        _s.Format = FmtJpeg.IsChecked == true ? FormatMode.Jpeg : FmtPng.IsChecked == true ? FormatMode.Png : FormatMode.Auto;
        _s.RemoveLocation = PrivLocation.IsChecked == true;
        _s.RemoveCamera = PrivCamera.IsChecked == true;
        _s.OutputFolder = FolderBox.Text.Trim();
        _s.Suffix = suffix;
        _s.ContextMenu = CtxMenu.IsChecked == true;
        _s.SendTo = SendToBox.IsChecked == true;
        _s.CheckUpdates = UpdatesBox.IsChecked == true;
        if (LangBox.SelectedItem is ComboBoxItem { Tag: string lang }) _s.Language = lang;
        _s.GoogleKeepOriginals = GoogleKeepBox.IsChecked == true;
        _s.GoogleIntoCopy = DestDoc.IsChecked != true;
        _s.GoogleClientFile = ChosenClientFile;
        DialogResult = true;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings { Language = _s.Language };
        defaults.Normalise();
        Fill(defaults);
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("Dlg.FolderTitle") };
        if (Directory.Exists(FolderBox.Text)) dlg.InitialDirectory = FolderBox.Text;
        if (dlg.ShowDialog(this) == true) FolderBox.Text = dlg.FolderName;
    }
}
