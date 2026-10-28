using Avalonia.Threading;
using Avalonia;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Nacho.Core;
using Nacho.Core.Google;

namespace Nacho.Linux;

public partial class OptionsWindow : Window
{
    private static readonly string[] Ids = { "small", "balanced", "sharp" };
    private readonly AppSettings _s;

    private TextBox[] Edges => new[] { EdgeSmall, EdgeBalanced, EdgeSharp };
    private TextBox[] Budgets => new[] { BudgetSmall, BudgetBalanced, BudgetSharp };
    private RadioButton[] Defaults => new[] { DefSmall, DefBalanced, DefSharp };
    private TextBlock[] Names => new[] { NameSmall, NameBalanced, NameSharp };

    public OptionsWindow() : this(new AppSettings()) { }

    public OptionsWindow(AppSettings settings)
    {
        _s = settings;
        InitializeComponent();
        Icon = App.Icon();
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
        bool narrow = Bounds.Width > 0 && Bounds.Width < 640;
        NavPane.IsVisible = !narrow;
        SaveHint.IsVisible = !narrow;
    }

    private Control? Section(object? tag) => tag is string name ? this.FindControl<Control>(name) : null;

    private void Nav_Click(object? sender, RoutedEventArgs e)
    {
        if (Section((sender as Control)?.Tag) is { } section) ShowSection(section);
    }

    /// <summary>Scrolls a section's card to the top of the page.</summary>
    private void ShowSection(Control section)
    {
        _navScrolling = true;
        double top = section.TranslatePoint(new Point(0, 0), Sections)?.Y ?? 0;
        Scroller.Offset = new Vector(Scroller.Offset.X, Math.Max(0, top));
        Dispatcher.UIThread.Post(() => _navScrolling = false, DispatcherPriority.Background);
    }

    /// <summary>While you scroll, the section list marks the card at the top.</summary>
    private void Scroller_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_navScrolling || !IsLoaded) return;
        RadioButton? current = null;
        foreach (var nav in NavList.Children.OfType<RadioButton>())
        {
            if (Section(nav.Tag) is not { } section) continue;
            double top = section.TranslatePoint(new Point(0, 0), Scroller)?.Y ?? 0;
            if (top <= 48 || current is null) current = nav;
            if (top > 48) break;
        }
        if (Scroller.Extent.Height > Scroller.Viewport.Height && Scroller.Offset.Y >= Scroller.Extent.Height - Scroller.Viewport.Height - 1)
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
        FolderBox.Text = s.OutputFolder;
        SuffixBox.Text = s.Suffix;
        CtxMenu.IsChecked = s.ContextMenu;
        AppMenuBox.IsChecked = s.AppMenu;
        UpdatesBox.IsChecked = s.CheckUpdates;
        LangBox.SelectedIndex = Math.Max(0, Array.IndexOf(Strings.Languages, s.Language));
        GoogleKeepBox.IsChecked = s.GoogleKeepOriginals;
        (s.GoogleIntoCopy ? DestCopy : DestDoc).IsChecked = true;
        ClientFileBox.Text = s.GoogleClientFile;
        bool builtIn = GoogleClientInfo.BuiltIn is not null || GoogleUi.SelfTestClient;
        ClientBuiltIn.IsEnabled = builtIn;
        (builtIn && string.IsNullOrWhiteSpace(s.GoogleClientFile) ? ClientBuiltIn : ClientOwn).IsChecked = true;
        GoogleAdvanced.IsExpanded = !builtIn || !string.IsNullOrWhiteSpace(s.GoogleClientFile);
        ClientNote.Text = builtIn ? "" : Loc.T("Opt.GoogleClientNone");
        ClientNote.IsVisible = !builtIn;
    }

    // ---------- Google Docs ----------

    /// <summary>The client file the dialog currently points at ("" = built in).</summary>
    private string ChosenClientFile => ClientOwn.IsChecked == true ? (ClientFileBox.Text ?? "").Trim() : "";

    public string GoogleStatusText => GoogleStatus.Text ?? "";

    /// <summary>For the self-test's screenshot of the Google Docs card.</summary>
    public void ScrollToGoogle()
    {
        GoogleAdvanced.IsExpanded = true;
        NavGoogle.IsChecked = true;
        ShowSection(SecGoogle);
    }

    private void RenderGoogle()
    {
        bool signedIn = GoogleUi.Session.HasSignIn;
        GoogleStatus.Text = !signedIn ? Loc.T("Opt.GoogleSignedOut")
            : string.IsNullOrEmpty(_s.GoogleEmail) ? Loc.T("Gd.SignedIn") : Loc.F("Gd.SignedInAs", _s.GoogleEmail);
        GoogleSignInButton.IsVisible = !signedIn;
        GoogleSignOutButton.IsVisible = signedIn;
    }

    private void ShowGoogleProblem(string? text)
    {
        GoogleProblem.Text = text ?? "";
        GoogleProblem.IsVisible = text is not null;
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
        catch (Exception ex) when (ex is GoogleException or HttpRequestException or System.Net.HttpListenerException)
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

    private async void GoogleSignIn_Click(object? sender, RoutedEventArgs e) => await SignInAsync();

    private async void GoogleSignOut_Click(object? sender, RoutedEventArgs e)
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

    private void Client_Changed(object? sender, RoutedEventArgs e)
    {
        if (GoogleProblem is not null) ShowGoogleProblem(null);
    }

    private async void ChooseClient_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.T("Dlg.ClientTitle"),
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } },
            });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
            if (GoogleClientInfo.FromFile(path) is null)
            {
                ShowGoogleProblem(Loc.T("Opt.GoogleClientBad"));
                return;
            }
            ClientFileBox.Text = path;
            ClientOwn.IsChecked = true;
            ShowGoogleProblem(null);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
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

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        Control? firstBad = null;
        var edges = new int[3];
        var budgets = new int[3];
        for (int i = 0; i < 3; i++)
        {
            string name = Loc.T("Preset." + Ids[i]);
            if (!int.TryParse((Edges[i].Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out edges[i])
                || edges[i] < 320 || edges[i] > 8000)
            {
                errors.Add(Loc.F("Opt.ErrEdge", name));
                firstBad ??= Edges[i];
            }
            if (!int.TryParse((Budgets[i].Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out budgets[i])
                || budgets[i] < 30 || budgets[i] > 20000)
            {
                errors.Add(Loc.F("Opt.ErrBudget", name));
                firstBad ??= Budgets[i];
            }
        }
        string suffix = (SuffixBox.Text ?? "").Trim();
        if (suffix.Length == 0 || AppSettings.HasBadNameChars(suffix))
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
            ErrorText.IsVisible = true;
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
        _s.OutputFolder = FolderBox.Text!.Trim();
        _s.Suffix = suffix;
        _s.ContextMenu = CtxMenu.IsChecked == true;
        _s.AppMenu = AppMenuBox.IsChecked == true;
        _s.CheckUpdates = UpdatesBox.IsChecked == true;
        if (LangBox.SelectedItem is ComboBoxItem { Tag: string lang }) _s.Language = lang;
        _s.GoogleKeepOriginals = GoogleKeepBox.IsChecked == true;
        _s.GoogleIntoCopy = DestDoc.IsChecked != true;
        _s.GoogleClientFile = ChosenClientFile;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void Reset_Click(object? sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings { Language = _s.Language, ContextMenu = _s.ContextMenu, AppMenu = _s.AppMenu };
        defaults.Normalise();
        Fill(defaults);
        ErrorText.IsVisible = false;
    }

    private async void ChangeFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var options = new FolderPickerOpenOptions { Title = Loc.T("Dlg.FolderTitle"), AllowMultiple = false };
            if (Directory.Exists(FolderBox.Text))
            {
                options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(FolderBox.Text!);
            }
            var picked = await StorageProvider.OpenFolderPickerAsync(options);
            if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) FolderBox.Text = path;
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }
}
