using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using Microsoft.Win32;
using Nacho.Core;

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
        SendToBox.IsChecked = s.SendTo;
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
        if (LangBox.SelectedItem is ComboBoxItem { Tag: string lang }) _s.Language = lang;
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
