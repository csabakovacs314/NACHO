using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Nacho.Core;

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
