using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Nacho.Core;
using Path = System.IO.Path;

namespace Nacho;

public partial class MainWindow : Window
{
    private static readonly string[] ImageExtensions =
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".gif", ".heic", ".heif", ".webp", ".avif", ".tif", ".tiff", ".bmp",
    };

    private readonly AppSettings _s;
    private readonly ImageShrinker _shrinker = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<string> _pending = new();
    private readonly DispatcherTimer _pendingTimer;
    private List<ResultItem> _lastBatch = new();
    private Point _dragStart;
    private bool _draggingOut;
    private bool _loading = true;
    private Func<string> _status = () => "";

    public static readonly DependencyProperty IsNarrowProperty =
        DependencyProperty.Register(nameof(IsNarrow), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    /// <summary>True below 900 px: tile buttons move under the text and the preview becomes a toggle.</summary>
    public bool IsNarrow
    {
        get => (bool)GetValue(IsNarrowProperty);
        set => SetValue(IsNarrowProperty, value);
    }

    public ObservableCollection<ResultItem> Items { get; } = new();

    public MainWindow(AppSettings settings)
    {
        _s = settings;
        InitializeComponent();
        Results.ItemsSource = Items;
        Items.CollectionChanged += (_, _) => UpdateCounts();
        _pendingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _pendingTimer.Tick += async (_, _) =>
        {
            _pendingTimer.Stop();
            var files = _pending.ToList();
            _pending.Clear();
            await AddFilesAsync(files);
        };

        SyncLanguageButtons();
        SelectPresetButton();
        (_s.PageSize == "Letter" ? PageLetter : PageA4).IsChecked = true;
        Loc.Instance.Changed += ApplyLanguage;
        SizeChanged += (_, _) => ApplyLayout();
        ApplyLanguage();
        SetStatus(() => Loc.T("Status.Ready"));
        _loading = false;
    }

    // ---------- public hooks (single instance, self-test) ----------

    public void ReceiveExternal(string[] args)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        var files = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (files.Count == 0) return;
        _pending.AddRange(files);
        _pendingTimer.Stop();
        _pendingTimer.Start();
    }

    public async Task WaitIdleAsync()
    {
        await _gate.WaitAsync();
        _gate.Release();
    }

    public string StatusMessage => StatusText.Text;

    public void Select(ResultItem item)
    {
        Results.SelectedItem = item;
        Results.ScrollIntoView(item);
    }

    // ---------- adding and shrinking ----------

    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        var files = Expand(paths).ToList();
        if (files.Count == 0) return;
        var batch = files.Select(f => new ResultItem(f, Path.GetFileName(f))).ToList();
        for (int i = batch.Count - 1; i >= 0; i--) Items.Insert(0, batch[i]);
        Results.SelectedItem = batch[0];
        Results.ScrollIntoView(batch[0]);
        _lastBatch = batch;
        await RunAsync(batch);
        SetStatus(() => Summary(batch));
    }

    private static IEnumerable<string> Expand(IEnumerable<string> paths)
    {
        foreach (string p in paths)
        {
            if (Directory.Exists(p))
            {
                foreach (string f in Directory.EnumerateFiles(p)
                             .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    yield return f;
                }
            }
            else if (File.Exists(p))
            {
                yield return p;
            }
        }
    }

    private async Task RunAsync(List<ResultItem> batch)
    {
        await _gate.WaitAsync();
        try
        {
            var preset = _s.GetPreset().Clone();
            var options = _s.ToOptions();
            for (int i = 0; i < batch.Count; i++)
            {
                var item = batch[i];
                if (!Items.Contains(item)) continue;
                SetStatus(() => Loc.F("Status.Working", i + 1, batch.Count));
                var (result, thumb, preview) = await Task.Run(() =>
                {
                    var r = _shrinker.Shrink(item.SourcePath, preset, options);
                    string? path = r.DeliverPath;
                    return (r, path is null ? null : Images.Load(path, 192), path is null ? null : Images.Load(path, 600));
                });
                item.SetResult(result, thumb, preview);
                if (result.Kind == ResultKind.Shrunk) _s.AddSavings(result.OriginalBytes - result.OutputBytes);
                if (ReferenceEquals(Results.SelectedItem, item)) UpdatePreview();
            }
            _s.Save();
            UpdateSavings();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Summary(IEnumerable<ResultItem> batch)
    {
        var list = batch.ToList();
        return Loc.F("Status.Summary", list.Count(i => i.IsOk), list.Count(i => i.IsKept), list.Count(i => i.IsError));
    }

    // ---------- commands ----------

    private void Add_Click(object sender, RoutedEventArgs e) => AddImages();
    private void Paste_Click(object sender, RoutedEventArgs e) => Paste();
    private void Options_Click(object sender, RoutedEventArgs e) => OpenOptions();
    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    private void Help_Click(object sender, RoutedEventArgs e) => OpenAbout();

    private async void AddImages()
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("Dlg.OpenTitle"),
            Filter = Loc.T("Dlg.OpenFilter"),
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == true) await AddFilesAsync(dlg.FileNames);
    }

    private async void Paste()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string>().ToList();
                await AddFilesAsync(files);
                return;
            }

            byte[]? png = null;
            if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is MemoryStream ms)
            {
                png = ms.ToArray();
            }
            else if (Clipboard.ContainsImage() && Clipboard.GetImage() is BitmapSource bs)
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bs));
                using var m = new MemoryStream();
                enc.Save(m);
                png = m.ToArray();
            }

            if (png is null)
            {
                SetStatus(() => Loc.T("Status.ClipboardEmpty"));
                return;
            }
            string dir = Path.Combine(Path.GetTempPath(), "NACHO");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"Clipboard {DateTime.Now:yyyy-MM-dd HHmmss}.png");
            await File.WriteAllBytesAsync(path, png);
            await AddFilesAsync(new[] { path });
        }
        catch (ExternalException)
        {
            SetStatus(() => Loc.T("Status.CopyFailed"));
        }
    }

    private void Undo()
    {
        var made = _lastBatch.Where(i => Items.Contains(i)).ToList();
        if (made.Count == 0)
        {
            SetStatus(() => Loc.T("Status.NothingToUndo"));
            return;
        }
        int deleted = 0;
        foreach (var item in made)
        {
            if (item.IsOk && item.Result!.OutputPath is { } output && TryDelete(output))
            {
                deleted++;
                _s.AddSavings(-(item.Result.OriginalBytes - item.Result.OutputBytes));
            }
            Items.Remove(item);
        }
        _lastBatch = new List<ResultItem>();
        _s.Save();
        UpdateSavings();
        UpdatePreview();
        SetStatus(() => Loc.F("Status.Undone", deleted));
    }

    private void OpenOptions()
    {
        string before = _s.Language;
        var w = new OptionsWindow(_s) { Owner = this };
        if (w.ShowDialog() != true) return;
        _s.Save();
        if (_s.Language != before) Loc.Instance.Set(_s.Language);
        _loading = true;
        SelectPresetButton();
        _loading = false;
        ApplyLanguage();
        string? shellError = ApplyShell();
        SetStatus(() => shellError ?? Loc.T("Status.OptionsSaved"));
    }

    /// <summary>Returns an error message, or null when the right-click menu and Send to were updated.</summary>
    private string? ApplyShell()
    {
        try
        {
            ShellIntegration.Apply(_s.ContextMenu, _s.SendTo, Loc.T("Shell.MenuText"));
            return null;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return Loc.F("Status.ShellFailed", ex.Message);
        }
    }

    private void OpenAbout() => new AboutWindow(_s) { Owner = this }.ShowDialog();

    private async void Preset_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not FrameworkElement { Tag: string id } || _s.CurrentPreset == id) return;
        _s.CurrentPreset = id;
        _s.Save();
        var p = _s.GetPreset();
        string Message() => Loc.F("Status.PresetSet", Loc.T("Preset." + id), p.LongEdge, p.BudgetKB);

        var redo = Items.Where(i => !i.IsWorking && !i.IsError && File.Exists(i.SourcePath)).ToList();
        if (redo.Count == 0)
        {
            SetStatus(Message);
            return;
        }
        foreach (var item in redo)
        {
            if (item.IsOk && item.Result!.OutputPath is { } old)
            {
                TryDelete(old);
                _s.AddSavings(-(item.Result.OriginalBytes - item.Result.OutputBytes));
            }
            item.Reset();
        }
        _lastBatch = redo;
        UpdatePreview();
        await RunAsync(redo);
        int n = redo.Count;
        SetStatus(() => Message() + " " + Loc.F("Status.Redone", n == 1 ? Loc.T("List.Count1") : Loc.F("List.CountN", n)));
    }

    private void Page_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string page }) return;
        _s.PageSize = page;
        UpdatePreview();
        if (_loading) return;
        _s.Save();
        SetStatus(() => Loc.F("Status.PageSet", Loc.T("Page." + page)));
    }

    private void Language_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not FrameworkElement { Tag: string lang } || lang == Loc.Instance.Language) return;
        _s.Language = lang;
        _s.Save();
        Loc.Instance.Set(lang);
        string? shellError = _s.ContextMenu ? ApplyShell() : null;
        SetStatus(() => shellError ?? Loc.T("Status.Language"));
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.Control && e.Key == Key.O) { AddImages(); e.Handled = true; }
        else if (mods == ModifierKeys.Control && e.Key == Key.V) { Paste(); e.Handled = true; }
        else if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.C) { CopyFile(Results.SelectedItem as ResultItem); e.Handled = true; }
        else if (mods == ModifierKeys.Control && e.Key == Key.Z) { Undo(); e.Handled = true; }
        else if (mods == ModifierKeys.Control && e.Key == Key.OemComma) { OpenOptions(); e.Handled = true; }
        else if (mods == ModifierKeys.None && e.Key == Key.F1) { OpenAbout(); e.Handled = true; }
    }

    private void Results_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Results.SelectedItem is ResultItem item)
        {
            RemoveItem(item);
            e.Handled = true;
        }
    }

    // ---------- tile actions ----------

    private static ResultItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as ResultItem;

    private void CopyFile_Click(object sender, RoutedEventArgs e) => CopyFile(ItemOf(sender));
    private void ShowFolder_Click(object sender, RoutedEventArgs e) => ShowInFolder(ItemOf(sender));
    private void Remove_Click(object sender, RoutedEventArgs e) { if (ItemOf(sender) is { } i) RemoveItem(i); }
    private void CtxCopyFile_Click(object sender, RoutedEventArgs e) => CopyFile(ItemOf(sender));
    private void CtxCopyPicture_Click(object sender, RoutedEventArgs e) => CopyPicture(ItemOf(sender));
    private void CtxCopyPath_Click(object sender, RoutedEventArgs e) => CopyPath(ItemOf(sender));
    private void CtxShowFolder_Click(object sender, RoutedEventArgs e) => ShowInFolder(ItemOf(sender));
    private void CtxRemove_Click(object sender, RoutedEventArgs e) { if (ItemOf(sender) is { } i) RemoveItem(i); }

    private void CtxOpenOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item && File.Exists(item.SourcePath))
        {
            try { Process.Start(new ProcessStartInfo(item.SourcePath) { UseShellExecute = true }); }
            catch (Exception ex) { App.Log(ex); }
        }
    }

    private void CopyFile(ResultItem? item)
    {
        if (item?.DeliverPath is not { } path || !File.Exists(path)) return;
        var data = new DataObject();
        data.SetFileDropList(new StringCollection { path });
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));
        if (TrySetClipboard(data)) SetStatus(() => Loc.F("Status.Copied", Path.GetFileName(path)));
    }

    private void CopyPicture(ResultItem? item)
    {
        if (item?.DeliverPath is not { } path || Images.Load(path, 0) is not BitmapSource bmp) return;
        var data = new DataObject();
        data.SetImage(bmp);
        if (TrySetClipboard(data)) SetStatus(() => Loc.F("Status.CopiedPicture", Path.GetFileName(path)));
    }

    /// <summary>The full path as text, for the File name box of Docs' Replace image > Upload from computer.</summary>
    private void CopyPath(ResultItem? item)
    {
        if (item?.DeliverPath is not { } path || !File.Exists(path)) return;
        var data = new DataObject();
        data.SetText(path, TextDataFormat.UnicodeText);
        if (TrySetClipboard(data)) SetStatus(() => Loc.F("Status.CopiedPath", Path.GetFileName(path)));
    }

    private bool TrySetClipboard(DataObject data)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(60);
            }
        }
        SetStatus(() => Loc.T("Status.CopyFailed"));
        return false;
    }

    private void ShowInFolder(ResultItem? item)
    {
        if (item?.DeliverPath is not { } path || !File.Exists(path)) return;
        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
            SetStatus(() => Loc.T("Status.Opened"));
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private void RemoveItem(ResultItem item)
    {
        Items.Remove(item);
        _lastBatch.Remove(item);
        UpdatePreview();
        SetStatus(() => Loc.F("Status.Removed", item.DisplayName));
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return false;
        }
    }

    // ---------- drag in, drag out ----------

    private void Thumb_MouseDown(object sender, MouseButtonEventArgs e) => _dragStart = e.GetPosition(this);

    private void Thumb_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (ItemOf(sender) is not { DeliverPath: { } path } || !File.Exists(path)) return;

        var data = new DataObject(DataFormats.FileDrop, new[] { path });
        _draggingOut = true;
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        }
        finally
        {
            _draggingOut = false;
        }
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        SetDragEffects(e);
        if (e.Effects == DragDropEffects.Copy) ShowDropHighlight(true);
    }

    private void Window_DragOver(object sender, DragEventArgs e) => SetDragEffects(e);

    private void Window_DragLeave(object sender, DragEventArgs e) => ShowDropHighlight(false);

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        ShowDropHighlight(false);
        if (_draggingOut) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) await AddFilesAsync(files);
    }

    private void SetDragEffects(DragEventArgs e)
    {
        e.Effects = !_draggingOut && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ShowDropHighlight(bool on)
    {
        DropRect.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, on ? "AccentFillColorDefaultBrush" : "ControlStrongStrokeColorDefaultBrush");
        DropTitle.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        DropRelease.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- view updates ----------

    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void SelectPresetButton()
    {
        (_s.CurrentPreset switch { "small" => PresetSmall, "sharp" => PresetSharp, _ => PresetBalanced }).IsChecked = true;
    }

    private void SyncLanguageButtons()
    {
        bool wasLoading = _loading;
        _loading = true;
        (Loc.Instance.Language == "es" ? LangEs : LangEn).IsChecked = true;
        _loading = wasLoading;
    }

    private void PreviewToggle_Changed(object sender, RoutedEventArgs e) => ApplyLayout();

    private void ApplyLanguage()
    {
        SyncLanguageButtons();
        RenderStatus();
        Title = "NACHO · " + Loc.T("App.Expansion");
        PresetSmall.Content = PresetLabel(_s.Presets[0]);
        PresetBalanced.Content = PresetLabel(_s.Presets[1]);
        PresetSharp.Content = PresetLabel(_s.Presets[2]);
        foreach (var item in Items) item.Refresh();
        UpdateCounts();
        UpdateSavings();
        UpdatePreview();
    }

    private static string PresetLabel(Preset p) => Loc.F("Preset.Label", Loc.T("Preset." + p.Id), p.LongEdge);

    private void UpdateCounts()
    {
        int n = Items.Count;
        CountRun.Text = n == 0 ? "" : " · " + (n == 1 ? Loc.T("List.Count1") : Loc.F("List.CountN", n));
        EmptyState.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSavings() => SavingsText.Text = Loc.F("Footer.Savings", Loc.Bytes(_s.MonthSavings()));

    private void ApplyLayout()
    {
        bool narrow = ActualWidth < 900;
        IsNarrow = narrow;
        PreviewToggle.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
        PreviewPanel.Visibility = !narrow || PreviewToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(PreviewPanel, narrow ? 0 : 1);
        Grid.SetRow(PreviewPanel, narrow ? 1 : 0);
        PreviewPanel.Width = narrow ? double.NaN : 380;
        PreviewPanel.MaxHeight = narrow ? 320 : double.PositiveInfinity;
        PreviewPanel.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(16, 0, 0, 0);
    }

    private void UpdatePreview()
    {
        PreviewOk.Visibility = Visibility.Collapsed;
        PreviewKept.Visibility = Visibility.Collapsed;
        PreviewError.Visibility = Visibility.Collapsed;
        PreviewEmpty.Visibility = Visibility.Collapsed;

        if (Results.SelectedItem is not ResultItem item || item.IsWorking)
        {
            PreviewEmpty.Text = Loc.T(Results.SelectedItem is ResultItem ? "Preview.Working" : "Preview.Empty");
            PreviewEmpty.Visibility = Visibility.Visible;
            return;
        }
        if (item.IsError)
        {
            PreviewErrorBody.Text = item.ErrorBody;
            PreviewError.Visibility = Visibility.Visible;
            return;
        }
        if (item.IsKept)
        {
            PreviewKeptBody.Text = Loc.F("Preview.KeptBody", item.DisplayName);
            PreviewKept.Visibility = Visibility.Visible;
            return;
        }

        var r = item.Result!;
        bool a4 = _s.PageSize != "Letter";
        double pageHeight = a4 ? 283 : 259;
        int textPx = a4 ? 602 : 624;
        string pageName = Loc.T(a4 ? "Page.A4" : "Page.Letter");
        PageSheet.Height = pageHeight;
        AutomationProperties.SetName(PageSheet, Loc.F("Preview.PageName", pageName));

        // The mini page's text column is 150 units wide and has room for about (height - 127) units of picture.
        const double textUnits = 150;
        double maxImageHeight = pageHeight - 127;
        double width = textUnits * Math.Min(1.0, (double)r.OutputWidth / textPx);
        double height = width * r.OutputHeight / Math.Max(1, r.OutputWidth);
        if (height > maxImageHeight)
        {
            width *= maxImageHeight / height;
            height = maxImageHeight;
        }
        PageImage.Source = item.PreviewImage;
        PageImage.Width = width;
        PageImage.Height = height;

        double zoom = (double)r.OutputWidth / textPx;
        PreviewCaption.Text = r.OutputWidth < textPx
            ? Loc.F("Preview.Narrow", r.OutputWidth, textPx, pageName)
            : Loc.F("Preview.Caption", textPx, pageName, r.OutputWidth, Loc.T(zoom >= 3 ? "Sharp.Any" : zoom >= 2 ? "Sharp.2x" : "Sharp.1x"));

        CmpSizeA.Text = Loc.Bytes(r.OriginalBytes);
        CmpSizeB.Text = Loc.Bytes(r.OutputBytes);
        CmpPxA.Text = ResultItem.Dim(r.OriginalWidth, r.OriginalHeight);
        CmpPxB.Text = ResultItem.Dim(r.OutputWidth, r.OutputHeight);
        CmpShapeA.Text = ResultItem.ShapeLabel(r.OriginalWidth, r.OriginalHeight);
        CmpShapeB.Text = ResultItem.ShapeDescription(r);
        PreviewShapeNote.Text = Loc.T("Preview.ShapeNote");
        CmpFmtA.Text = r.SourceFormat;
        CmpFmtB.Text = r.OutputFormat;
        CmpLocA.Text = Loc.T(r.OriginalHadLocation ? "Loc.Present" : "Loc.None");
        CmpLocB.Text = Loc.T(r.OutputHasLocation ? "Loc.Present" : r.OriginalHadLocation ? "Loc.Removed" : "Loc.None");
        PreviewOk.Visibility = Visibility.Visible;
    }

    /// <summary>The status is kept as a recipe, so a language switch re-renders it in the new language.</summary>
    private void SetStatus(Func<string> text)
    {
        _status = text;
        RenderStatus();
    }

    private void RenderStatus()
    {
        StatusText.Text = _status();
        var peer = UIElementAutomationPeer.FromElement(StatusText) ?? UIElementAutomationPeer.CreatePeerForElement(StatusText);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    protected override void OnClosed(EventArgs e)
    {
        Loc.Instance.Changed -= ApplyLanguage;
        base.OnClosed(e);
    }
}
