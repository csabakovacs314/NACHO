using System.Collections.ObjectModel;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nacho.Core;
using Nacho.Core.Google;
using Nacho.Core.Word;

namespace Nacho.Linux;

public partial class MainWindow : Window
{
    private static readonly string[] ImageExtensions =
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".gif", ".heic", ".heif", ".webp", ".avif", ".tif", ".tiff", ".bmp",
    };

    private static readonly DataFormat<byte[]> GnomeCopiedFiles = DataFormat.CreateBytesPlatformFormat("x-special/gnome-copied-files");

    private readonly AppSettings _s;
    private readonly ImageShrinker _shrinker = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<string> _pending = new();
    private readonly DispatcherTimer _pendingTimer;
    private List<ResultItem> _lastBatch = new();
    private PointerPressedEventArgs? _press;
    private Point _pressAt;
    private IStorageFile? _pressFile;
    private bool _draggingOut;
    private bool _loading = true;
    private Func<string> _status = () => "";
    private UpdateInfo? _update;
    private GoogleDocWindow? _googleWindow;
    private WordDocWindow? _wordWindow;

    public ObservableCollection<ResultItem> Items { get; } = new();

    /// <summary>True below 900 px: tile buttons move under the text and the preview becomes a toggle.</summary>
    public bool IsNarrow => Classes.Contains("narrow");

    public MainWindow() : this(new AppSettings()) { }

    public MainWindow(AppSettings settings)
    {
        _s = settings;
        InitializeComponent();
        Icon = App.Icon();
        Results.ItemsSource = Items;
        Items.CollectionChanged += (_, _) => UpdateCounts();
        _pendingTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(400) };
        _pendingTimer.Tick += async (_, _) =>
        {
            _pendingTimer.Stop();
            var files = _pending.ToList();
            _pending.Clear();
            await AddFilesAsync(files);
        };

        AddHandler(DragDrop.DragEnterEvent, Window_DragEnter);
        AddHandler(DragDrop.DragOverEvent, Window_DragOver);
        AddHandler(DragDrop.DragLeaveEvent, Window_DragLeave);
        AddHandler(DragDrop.DropEvent, Window_Drop);
        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);

        SyncLanguageButtons();
        SelectPresetButton();
        (_s.PageSize == "Letter" ? PageLetter : PageA4).IsChecked = true;
        Loc.Instance.Changed += ApplyLanguage;
        SizeChanged += (_, _) => ApplyLayout();
        // The start cards fill the window when it is wide enough, and scroll when it is not.
        HomePanel.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ViewportProperty) HomeGrid.MinHeight = IsNarrow ? 0 : HomePanel.Viewport.Height;
        };
        ApplyLanguage();
        ApplyLayout();
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

    public string StatusMessage => StatusText.Text ?? "";

    // ---------- updates ----------

    /// <summary>Once a day (when turned on), asks GitHub for a newer release and shows the bar if there is one.</summary>
    public async Task CheckForUpdatesAsync()
    {
        if (!_s.CheckUpdates || !UpdateCheck.Due(_s.LastUpdateCheckUtc, DateTime.UtcNow)) return;
        _s.LastUpdateCheckUtc = DateTime.UtcNow;
        _s.Save();
        var result = await UpdateCheck.CheckAsync(UpdateCheck.Current(typeof(App).Assembly));
        if (result.Outcome == UpdateCheck.Outcome.Newer) ShowUpdate(result.Update!);
    }

    public void ShowUpdate(UpdateInfo info)
    {
        _update = info;
        RenderUpdate();
    }

    public bool UpdateBarVisible => UpdateBar.IsVisible;

    public string UpdateMessage => UpdateText.Text ?? "";

    private void RenderUpdate()
    {
        UpdateBar.IsVisible = _update is not null;
        if (_update is null) return;
        UpdateText.Text = Loc.F("Update.Available", _update.Version.ToString(3), UpdateCheck.Current(typeof(App).Assembly).ToString(3));
    }

    private void UpdateOpen_Click(object? sender, RoutedEventArgs e)
    {
        if (_update is not null) OpenWeb(_update.PageUrl);
    }

    private void UpdateLater_Click(object? sender, RoutedEventArgs e)
    {
        _update = null;
        RenderUpdate();
    }

    /// <summary>Opens NACHO's own release page in the browser; any other address is refused.</summary>
    public static void OpenWeb(string url)
    {
        if (!UpdateCheck.IsReleasePage(url) && url != UpdateCheck.ReleasesPage) return;
        Shell.Open(url);
    }

    public void Select(ResultItem item)
    {
        Results.SelectedItem = item;
        Results.ScrollIntoView(item);
    }

    // ---------- adding and shrinking ----------

    /// <summary>Adds files to the list. Word files open in the Word window instead (it checks them first and asks),
    /// unless <paramref name="wordWindow"/> is false: then a .docx gets a smaller copy in Docs-Ready like a picture.</summary>
    public async Task AddFilesAsync(IEnumerable<string> paths, bool wordWindow = true)
    {
        var files = Expand(paths).ToList();
        if (wordWindow)
        {
            var word = files.Where(WordText.IsWordFile).ToList();
            if (word.Count > 0)
            {
                files = files.Except(word).ToList();
                OpenWordDoc(word);
            }
        }
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
        foreach (string raw in paths)
        {
            string p = raw.StartsWith("file://", StringComparison.Ordinal) ? new Uri(raw).LocalPath : raw;
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
                int n = i + 1;
                SetStatus(() => Loc.F("Status.Working", n, batch.Count));
                var (result, thumb, preview) = await Task.Run(() =>
                {
                    var r = _shrinker.Shrink(item.SourcePath, preset, options);
                    string? path = r.IsDocument ? null : r.DeliverPath;
                    return (r, path is null ? null : Images.Load(path, 192), path is null ? null : Images.Load(path, 600));
                });
                item.SetResult(result, thumb, preview);
                UpdateCounts();
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

    private void Add_Click(object? sender, RoutedEventArgs e) => AddImages();
    private void Paste_Click(object? sender, RoutedEventArgs e) => Paste();
    private void Options_Click(object? sender, RoutedEventArgs e) => OpenOptions();
    private void Undo_Click(object? sender, RoutedEventArgs e) => Undo();
    private void Help_Click(object? sender, RoutedEventArgs e) => OpenAbout();
    // The button and Ctrl+G always start at Choose (Google's picker). Only an explicit paste or drop of a Docs link
    // checks that Doc straight away; a link that merely sits on the clipboard from earlier is not used.
    private void GoogleDoc_Click(object? sender, RoutedEventArgs e) => OpenGoogleDoc(null);

    private void WordDoc_Click(object? sender, RoutedEventArgs e) => OpenWordDoc(null);

    /// <summary>Opens (or brings back) the Word window. With files, it checks the first one and queues the rest.</summary>
    public WordDocWindow OpenWordDoc(IReadOnlyList<string>? files, bool activate = true)
    {
        var w = _wordWindow;
        if (w is null)
        {
            w = new WordDocWindow(_s) { ShowActivated = activate };
            _wordWindow = w;
            w.Finished += WordFinished;
            w.Restored += o => SetStatus(() => Loc.F("Status.WordRestored", o.Name));
            w.OptionsRequested += () => OpenOptionsAsync(google: true);
            w.Closed += (_, _) =>
            {
                _wordWindow = null;
                RenderHome();
            };
            if (files is { Count: > 0 })
            {
                var first = files[0];
                w.Enqueue(files.Skip(1));
                w.Opened += async (_, _) => await w.OpenLocalAsync(first);
            }
            w.Show(this);
        }
        else
        {
            if (activate) w.Activate();
            if (files is { Count: > 0 })
            {
                if (w.IsWorking || w.StageName == "Summary") w.Enqueue(files);
                else
                {
                    w.Enqueue(files.Skip(1));
                    _ = w.OpenLocalAsync(files[0]);
                }
            }
        }
        if (files is { Count: > 1 }) SetStatus(() => Loc.F("Status.WordQueued", files.Count - 1));
        return w;
    }

    private void WordFinished(WordOutcome o)
    {
        long saved = Math.Max(0, o.BytesBefore - o.BytesAfter);
        _s.AddSavings(saved);
        _s.Save();
        UpdateSavings();
        SetStatus(() => Loc.F("Status.WordDone", o.Name, o.Replaced, Loc.Bytes(saved)));
        RenderHome();
    }

    /// <summary>Opens (or brings back) the Google Doc window; with a link, NACHO checks that Doc straight away.</summary>
    public GoogleDocWindow OpenGoogleDoc(string? link, bool activate = true)
    {
        if (_googleWindow is null)
        {
            var w = new GoogleDocWindow(_s, link) { ShowActivated = activate };
            _googleWindow = w;
            w.Finished += GoogleFinished;
            w.OptionsRequested += () => OpenOptionsAsync(google: true);
            w.Closed += (_, _) => _googleWindow = null;
            if (link is not null) w.Opened += async (_, _) => await w.UseLinkAsync(link, autoCheck: true);
            w.Show(this);
        }
        else
        {
            if (activate) _googleWindow.Activate();
            if (link is not null) _ = _googleWindow.UseLinkAsync(link, autoCheck: true);
        }
        return _googleWindow;
    }

    private void GoogleFinished(GoogleDocPlan plan)
    {
        int replaced = plan.Count(GooglePictureState.Replaced);
        long saved = plan.Jobs.Where(j => j.State == GooglePictureState.Replaced).Sum(j => j.OriginalBytes - j.NewBytes);
        _s.AddSavings(saved);
        _s.Save();
        UpdateSavings();
        string title = plan.CopyName ?? plan.Doc.Title.Trim();
        SetStatus(() => Loc.F("Status.GoogleDone", title, replaced, Loc.Bytes(saved)));
        RenderHome();
    }

    public async Task<string?> ClipboardDocsLinkAsync()
    {
        try
        {
            if (Clipboard is not { } clipboard) return null;
            string? text = await clipboard.TryGetTextAsync();
            return GoogleDocLink.TryGetId(text) is not null ? text!.Trim() : null;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return null;
        }
    }

    private async void AddImages()
    {
        try
        {
            var types = ParseFilter(Loc.T("Dlg.OpenFilter"));
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.T("Dlg.OpenTitle"),
                AllowMultiple = true,
                FileTypeFilter = types,
            });
            var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
            if (paths.Count > 0) await AddFilesAsync(paths);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    /// <summary>"Images|*.jpg;*.png|All files|*.*" (the Windows filter text) as picker file types.</summary>
    internal static List<FilePickerFileType> ParseFilter(string filter)
    {
        var parts = filter.Split('|');
        var types = new List<FilePickerFileType>();
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            var patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(p => p == "*.*" ? new[] { "*" } : new[] { p, p.ToUpperInvariant() })
                .Distinct()
                .ToList();
            types.Add(new FilePickerFileType(parts[i]) { Patterns = patterns });
        }
        return types;
    }

    private async void Paste()
    {
        try
        {
            var clipboard = Clipboard;
            if (clipboard is null) return;

            if (await ClipboardDocsLinkAsync() is { } link)
            {
                OpenGoogleDoc(link);
                return;
            }

            var items = await clipboard.TryGetFilesAsync();
            var paths = items?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
            if (paths is { Count: > 0 })
            {
                await AddFilesAsync(paths);
                return;
            }

            // Not disposed: when NACHO itself copied the picture, the clipboard still holds this object.
            byte[]? png = null;
            var bitmap = await clipboard.TryGetBitmapAsync();
            if (bitmap is not null)
            {
                using var ms = new MemoryStream();
                bitmap.Save(ms, new PngBitmapEncoderOptions());
                png = ms.ToArray();
            }
            if (png is null)
            {
                SetStatus(() => Loc.T("Status.ClipboardEmpty"));
                return;
            }
            string dir = Path.Combine(Path.GetTempPath(), "nacho-" + Environment.UserName);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"Clipboard {DateTime.Now:yyyy-MM-dd HHmmss}.png");
            await File.WriteAllBytesAsync(path, png);
            await AddFilesAsync(new[] { path });
        }
        catch (Exception ex)
        {
            App.Log(ex);
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

    private async void OpenOptions() => await OpenOptionsAsync();

    private async Task OpenOptionsAsync(bool google = false)
    {
        string before = _s.Language;
        bool wasContext = _s.ContextMenu, wasApp = _s.AppMenu;
        var w = new OptionsWindow(_s);
        if (google) w.Opened += (_, _) => w.ScrollToGoogle();
        Window owner = google && _wordWindow is { IsActive: true } ? _wordWindow : google && _googleWindow is not null ? _googleWindow : this;
        if (!await w.ShowDialog<bool>(owner)) return;
        _s.Save();
        if (_s.Language != before) Loc.Instance.Set(_s.Language);
        _loading = true;
        SelectPresetButton();
        _loading = false;
        ApplyLanguage();
        bool shellChanged = wasContext != _s.ContextMenu || wasApp != _s.AppMenu || (_s.Language != before && _s.ContextMenu);
        string? shellError = shellChanged || _s.ContextMenu || _s.AppMenu ? ApplyShell() : null;
        SetStatus(() => shellError ?? Loc.T("Status.OptionsSaved"));
    }

    /// <summary>Returns an error message, or null when the file manager menus and the app menu were updated.</summary>
    private string? ApplyShell()
    {
        try
        {
            Shell.Apply(_s.ContextMenu, _s.AppMenu, Loc.T("Shell.MenuText"));
            return null;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return Loc.F("Status.ShellFailed", ex.Message);
        }
    }

    private async void OpenAbout() => await new AboutWindow(_s).ShowDialog(this);

    private async void Preset_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { IsChecked: true, Tag: string id } || _s.CurrentPreset == id) return;
        _s.CurrentPreset = id;
        _s.Save();
        _loading = true;
        SelectPresetButton();
        _loading = false;
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

    private void Page_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true, Tag: string page }) return;
        _s.PageSize = page;
        UpdatePreview();
        if (_loading) return;
        _s.Save();
        SetStatus(() => Loc.F("Status.PageSet", Loc.T("Page." + page)));
    }

    private void Language_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { IsChecked: true, Tag: string lang } || lang == Loc.Instance.Language) return;
        _s.Language = lang;
        _s.Save();
        Loc.Instance.Set(lang);
        // The file manager menu carries the menu text, so it follows the language.
        string? shellError = _s.ContextMenu ? ApplyShell() : null;
        SetStatus(() => shellError ?? Loc.T("Status.Language"));
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        var mods = e.KeyModifiers;
        if (mods == KeyModifiers.Control && e.Key == Key.O) { AddImages(); e.Handled = true; }
        else if (mods == KeyModifiers.Control && e.Key == Key.V && FocusManager?.GetFocusedElement() is not TextBox) { Paste(); e.Handled = true; }
        else if (mods == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.C) { CopyFile(Results.SelectedItem as ResultItem); e.Handled = true; }
        else if (mods == KeyModifiers.Control && e.Key == Key.Z) { Undo(); e.Handled = true; }
        else if (mods == KeyModifiers.Control && e.Key == Key.D) { OpenCompare(Results.SelectedItem as ResultItem); e.Handled = true; }
        else if (mods == KeyModifiers.Control && e.Key == Key.OemComma) { OpenOptions(); e.Handled = true; }
        else if (mods == KeyModifiers.Control && e.Key == Key.G) { GoogleDoc_Click(null, new RoutedEventArgs()); e.Handled = true; }
        else if (mods == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.W) { OpenWordDoc(null); e.Handled = true; }
        else if (mods == KeyModifiers.None && e.Key == Key.F1) { OpenAbout(); e.Handled = true; }
    }

    private void Results_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Results.SelectedItem is ResultItem item)
        {
            RemoveItem(item);
            e.Handled = true;
        }
    }

    // ---------- tile actions ----------

    private static ResultItem? ItemOf(object? sender) => (sender as StyledElement)?.DataContext as ResultItem;

    private void CopyFile_Click(object? sender, RoutedEventArgs e) => CopyFile(ItemOf(sender));
    private void ShowFolder_Click(object? sender, RoutedEventArgs e) => ShowInFolder(ItemOf(sender));
    private void Remove_Click(object? sender, RoutedEventArgs e) { if (ItemOf(sender) is { } i) RemoveItem(i); }
    private void CtxCopyFile_Click(object? sender, RoutedEventArgs e) => CopyFile(ItemOf(sender));
    private void CtxCopyPicture_Click(object? sender, RoutedEventArgs e) => CopyPicture(ItemOf(sender));
    private void CtxCompare_Click(object? sender, RoutedEventArgs e) => OpenCompare(ItemOf(sender));
    private void Compare_Click(object? sender, RoutedEventArgs e) => OpenCompare(Results.SelectedItem as ResultItem);

    /// <summary>Opens the before/after slider for a shrunk picture; returns the window (the self-test drives it).</summary>
    public CompareWindow? OpenCompare(ResultItem? item, bool activate = true)
    {
        if (item is null || !item.CanCompare) return null;
        var w = new CompareWindow(item.Result!, item.DisplayName) { ShowActivated = activate };
        w.Show(this);
        return w;
    }
    private void CtxCopyPath_Click(object? sender, RoutedEventArgs e) => CopyPath(ItemOf(sender));
    private void CtxShowFolder_Click(object? sender, RoutedEventArgs e) => ShowInFolder(ItemOf(sender));
    private void CtxRemove_Click(object? sender, RoutedEventArgs e) { if (ItemOf(sender) is { } i) RemoveItem(i); }

    private void CtxOpenOriginal_Click(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item && File.Exists(item.SourcePath)) Shell.Open(item.SourcePath);
    }

    /// <summary>
    /// The copy as a file: the standard file list (text/uri-list, for Dolphin and browsers) plus the
    /// GNOME list that Files, Nemo and Caja paste from.
    /// </summary>
    public async void CopyFile(ResultItem? item)
    {
        if (item?.DeliverPath is not { } path || !File.Exists(path) || Clipboard is null) return;
        try
        {
            var file = await StorageProvider.TryGetFileFromPathAsync(path);
            if (file is null) return;
            var data = new DataTransferItem();
            data.SetFile(file);
            data.Set(GnomeCopiedFiles, Encoding.UTF8.GetBytes("copy\n" + new Uri(path).AbsoluteUri));
            var transfer = new DataTransfer();
            transfer.Add(data);
            await Clipboard.SetDataAsync(transfer);
            SetStatus(() => Loc.F("Status.Copied", Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            App.Log(ex);
            SetStatus(() => Loc.T("Status.CopyFailed"));
        }
    }

    private async void CopyPicture(ResultItem? item)
    {
        if (item?.DeliverPath is not { } path || Clipboard is null || Images.Load(path, 0) is not { } bmp) return;
        try
        {
            await Clipboard.SetBitmapAsync(bmp);
            SetStatus(() => Loc.F("Status.CopiedPicture", Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            App.Log(ex);
            SetStatus(() => Loc.T("Status.CopyFailed"));
        }
    }

    /// <summary>The full path as text, for Docs' Replace image > Upload from computer (Ctrl+L in the file dialog).</summary>
    private async void CopyPath(ResultItem? item)
    {
        if (item?.DeliverPath is not { } path || !File.Exists(path) || Clipboard is null) return;
        try
        {
            await Clipboard.SetTextAsync(path);
            SetStatus(() => Loc.F("Status.CopiedPath", Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            App.Log(ex);
            SetStatus(() => Loc.T("Status.CopyFailed"));
        }
    }

    private void ShowInFolder(ResultItem? item)
    {
        if (item?.DeliverPath is not { } path || !File.Exists(path)) return;
        if (Shell.ShowInFolder(path)) SetStatus(() => Loc.T("Status.Opened"));
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

    private async void Thumb_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _press = e;
        _pressAt = e.GetPosition(this);
        _pressFile = null;
        if (ItemOf(sender) is { DeliverPath: { } path } && File.Exists(path))
        {
            // Asked for now, so the drag can start without waiting once the pointer moves.
            _pressFile = await StorageProvider.TryGetFileFromPathAsync(path);
        }
    }

    private void Thumb_PointerReleased(object? sender, PointerReleasedEventArgs e) => _press = null;

    private async void Thumb_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_press is null || _pressFile is null || _draggingOut) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _press = null;
            return;
        }
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _pressAt.X) < 4 && Math.Abs(p.Y - _pressAt.Y) < 4) return;

        var press = _press;
        _press = null;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateFile(_pressFile));
        _draggingOut = true;
        try
        {
            await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        finally
        {
            _draggingOut = false;
        }
    }

    private void Window_DragEnter(object? sender, DragEventArgs e)
    {
        SetDragEffects(e);
        if (e.DragEffects == DragDropEffects.Copy) ShowDropHighlight(true);
    }

    private void Window_DragOver(object? sender, DragEventArgs e) => SetDragEffects(e);

    private void Window_DragLeave(object? sender, DragEventArgs e) => ShowDropHighlight(false);

    private async void Window_Drop(object? sender, DragEventArgs e)
    {
        ShowDropHighlight(false);
        if (_draggingOut) return;
        var files = e.DataTransfer.TryGetFiles();
        var paths = files?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths is { Count: > 0 }) await AddFilesAsync(paths);
        else if (DroppedDocsLink(e) is { } link) OpenGoogleDoc(link);
    }

    /// <summary>A Google Docs link dragged from the browser's address bar or a page.</summary>
    private static string? DroppedDocsLink(DragEventArgs e)
    {
        try
        {
            string? text = e.DataTransfer.Contains(DataFormat.Text) ? e.DataTransfer.TryGetText() : null;
            return GoogleDocLink.TryGetId(text) is not null ? text!.Trim() : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private void SetDragEffects(DragEventArgs e)
    {
        e.DragEffects = !_draggingOut && (e.DataTransfer.Contains(DataFormat.File) || DroppedDocsLink(e) is not null)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ShowDropHighlight(bool on)
    {
        var stroke = (Avalonia.Media.IBrush?)this.FindResource(on ? "N.Accent" : "N.StrongStroke");
        DropRect.Stroke = DropStripRect.Stroke = stroke;
        DropRect.StrokeThickness = DropStripRect.StrokeThickness = on ? 3 : 2;
        DropTitle.IsVisible = !on;
        DropRelease.IsVisible = on;
        DropStripText.Text = Loc.T(on ? "Drop.Release" : "Main.DropMore");
    }

    // ---------- view updates ----------

    private void Results_SelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void SelectPresetButton()
    {
        (_s.CurrentPreset switch { "small" => PresetSmall, "sharp" => PresetSharp, _ => PresetBalanced }).IsChecked = true;
        (_s.CurrentPreset switch { "small" => HomeSmall, "sharp" => HomeSharp, _ => HomeBalanced }).IsChecked = true;
    }

    private void SyncLanguageButtons()
    {
        bool wasLoading = _loading;
        _loading = true;
        (Loc.Instance.Language switch { "es" => LangEs, "hu" => LangHu, _ => LangEn }).IsChecked = true;
        _loading = wasLoading;
    }

    private void PreviewToggle_Changed(object? sender, RoutedEventArgs e) => ApplyLayout();

    private void ApplyLanguage()
    {
        SyncLanguageButtons();
        RenderStatus();
        Title = "NACHO · " + Loc.T("App.Expansion");
        // The header keeps the names short; the size each one means is in the tooltip and on the start screen.
        foreach (var (button, p) in new[] { (PresetSmall, _s.Presets[0]), (PresetBalanced, _s.Presets[1]), (PresetSharp, _s.Presets[2]) })
        {
            string spec = Loc.F("Home.PresetSpec", p.LongEdge, p.BudgetKB);
            button.Content = Loc.T("Preset." + p.Id);
            ToolTip.SetTip(button, Loc.T("Preset." + p.Id) + " · " + spec);
            AutomationProperties.SetHelpText(button, spec);
        }
        HomeSmall.Content = PresetCard(_s.Presets[0]);
        HomeBalanced.Content = PresetCard(_s.Presets[1]);
        HomeSharp.Content = PresetCard(_s.Presets[2]);
        RenderHome();
        foreach (var item in Items) item.Refresh();
        RenderUpdate();
        UpdateCounts();
        UpdateSavings();
        UpdatePreview();
    }

    /// <summary>A preset card on the start screen: the name, and what it means in pixels and kilobytes.</summary>
    private static StackPanel PresetCard(Preset p)
    {
        var spec = new TextBlock { Text = Loc.F("Home.PresetSpec", p.LongEdge, p.BudgetKB), FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        spec.Classes.Add("secondary");
        return new StackPanel
        {
            Children = { new TextBlock { Text = Loc.T("Preset." + p.Id), FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold }, spec },
        };
    }

    /// <summary>True while the list is empty and the start screen (files or a whole Google Doc) is shown.</summary>
    public bool HomeVisible => HomePanel.IsVisible;

    /// <summary>The start screen's texts that depend on settings: where copies go, the Google steps and the last Doc.</summary>
    private void RenderHome()
    {
        HomeSafeWhere.Text = Loc.F("Home.SafeBody", FolderName(_s.OutputFolder));
        HomeSteps.Children.Clear();
        NewChipText.Text = Loc.T("Home.New") + ": WORD";
        string[] steps = { "Home.Step1", "Home.Step2", "Home.Step3" };
        for (int i = 0; i < steps.Length; i++)
        {
            var number = new TextBlock { Text = (i + 1).ToString(Loc.Instance.Culture), FontSize = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var dot = new Border { Child = number };
            dot.Classes.Add("stepdot");
            var text = new TextBlock { Text = Loc.T(steps[i]), FontSize = 14, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(text);
            AutomationProperties.SetName(row, $"{i + 1}. {text.Text}");
            HomeSteps.Children.Add(row);
        }

        var last = LastDocument();
        LastTimeBox.IsVisible = last is not null;
        LastTimeText.Inlines?.Clear();
        if (last is null) return;
        LastTimeText.Inlines ??= new Avalonia.Controls.Documents.InlineCollection();
        LastTimeText.Inlines.Add(new Avalonia.Controls.Documents.Run(Loc.T("Home.LastTime") + " "));
        LastTimeText.Inlines.Add(new Avalonia.Controls.Documents.Run(last.Title) { FontWeight = Avalonia.Media.FontWeight.SemiBold });
        LastTimeText.Inlines.Add(new Avalonia.Controls.Documents.Run($" · {Loc.Bytes(last.BytesBefore)} → {Loc.Bytes(last.BytesAfter)}"));
        AutomationProperties.SetName(LastTimeText, Loc.T("Home.LastTime") + " " + last.Title + ", " + Loc.F("Tile.SizeLine", Loc.Bytes(last.BytesBefore), Loc.Bytes(last.BytesAfter)));
        AutomationProperties.SetName(LastTimeOpen, Loc.T("Gd.Open") + " " + last.Title);
    }

    /// <summary>"Pictures › Docs-Ready": the folder and its parent, enough to recognise it.</summary>
    public static string FolderName(string folder)
    {
        string trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);
        string? parent = Path.GetFileName(Path.GetDirectoryName(trimmed) ?? "");
        return string.IsNullOrEmpty(parent) ? (name.Length > 0 ? name : trimmed) : parent + " › " + name;
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

    private void LastTimeOpen_Click(object? sender, RoutedEventArgs e) => LastDocument()?.Open();

    /// <summary>The document shrunk most recently, a Google Doc or a Word file, for "Last time" on the start screen.</summary>
    private LastDoc? LastDocument()
    {
        var g = _s.GoogleRecent.FirstOrDefault();
        var w = _s.WordRecent.FirstOrDefault();
        if (w is not null && (g is null || w.When >= g.When))
        {
            return new LastDoc(w.Name, w.BytesBefore, w.BytesAfter, () =>
            {
                if (w.Where == WordWhere.Drive) WordUi.OpenDriveFile(w.Target);
                else WordUi.OpenFile(w.Target);
            });
        }
        return g is null ? null : new LastDoc(g.Title, g.BytesBefore, g.BytesAfter, () => GoogleUi.OpenDoc(g.DocId));
    }

    private sealed record LastDoc(string Title, long BytesBefore, long BytesAfter, Action Open);

    private void UpdateCounts()
    {
        int n = Items.Count;
        CountRun.Text = n == 0 ? "" : " · " + (n == 1 ? Loc.T("List.Count1") : Loc.F("List.CountN", n));
        HomePanel.IsVisible = n == 0;
        Tagline.IsVisible = n == 0;
        ContentGrid.IsVisible = n > 0;
        ActionsPanel.IsVisible = n > 0;

        int ok = Items.Count(i => i.IsOk), kept = Items.Count(i => i.IsKept), bad = Items.Count(i => i.IsError);
        ChipShrunkText.Text = Loc.F("Main.ChipShrunk", ok);
        ChipShrunk.IsVisible = ok > 0;
        ChipKeptText.Text = Loc.F(kept == 1 ? "Main.ChipKept1" : "Main.ChipKeptN", kept);
        ChipKept.IsVisible = kept > 0;
        ChipErrorText.Text = Loc.F(bad == 1 ? "Main.ChipError1" : "Main.ChipErrorN", bad);
        ChipError.IsVisible = bad > 0;

        long before = Items.Where(i => i.IsOk || i.IsKept).Sum(i => i.Result!.OriginalBytes);
        long after = Items.Where(i => i.IsOk || i.IsKept).Sum(i => i.IsOk ? i.Result!.OutputBytes : i.Result!.OriginalBytes);
        TotalText.Text = ok > 0 ? $"{Loc.Bytes(before)} → {Loc.Bytes(after)}" : "";
        AutomationProperties.SetName(TotalText, ok > 0 ? Loc.F("Tile.SizeLine", Loc.Bytes(before), Loc.Bytes(after)) : "");
        ApplyLayout();
    }

    private void UpdateSavings() => SavingsText.Text = Loc.F("Footer.Savings", Loc.Bytes(_s.MonthSavings()));

    private void ApplyLayout()
    {
        bool narrow = Bounds.Width > 0 ? Bounds.Width < 900 : Width < 900;
        Classes.Set("narrow", narrow);
        PreviewToggle.IsVisible = narrow;
        PreviewPanel.IsVisible = !narrow || PreviewToggle.IsChecked == true;
        Grid.SetColumn(PreviewPanel, narrow ? 0 : 1);
        Grid.SetRow(PreviewPanel, narrow ? 1 : 0);
        PreviewPanel.Width = narrow ? double.NaN : 380;
        PreviewPanel.MaxHeight = narrow ? 320 : double.PositiveInfinity;
        PreviewPanel.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(16, 0, 0, 0);

        // Header: the language, options and help get their own line, then the list commands.
        Grid.SetRow(GlobalPanel, narrow ? 1 : 0);
        Grid.SetColumn(GlobalPanel, narrow ? 0 : 2);
        Grid.SetColumnSpan(GlobalPanel, narrow ? 3 : 1);
        Grid.SetRow(ActionsPanel, narrow ? 2 : 0);
        Grid.SetColumn(ActionsPanel, narrow ? 0 : 1);
        Grid.SetColumnSpan(ActionsPanel, narrow ? 3 : 1);

        // Results summary: the preview button goes under the chips, the total is left out.
        DockPanel.SetDock(PreviewToggle, narrow ? Dock.Bottom : Dock.Right);
        PreviewToggle.HorizontalAlignment = narrow ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Stretch;
        PreviewToggle.Margin = narrow ? new Thickness(0, 8, 0, 0) : new Thickness(8, 0, 0, 0);
        TotalText.IsVisible = !narrow;

        // Start screen: the two cards stack.
        Grid.SetRow(GoogleCard, narrow ? 2 : 1);
        Grid.SetColumn(GoogleCard, narrow ? 0 : 1);
        Grid.SetColumnSpan(GoogleCard, narrow ? 2 : 1);
        Grid.SetColumnSpan(FilesCard, narrow ? 2 : 1);
        HomeGrid.RowDefinitions[1].Height = narrow ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        HomeGrid.MinHeight = narrow ? 0 : HomePanel.Viewport.Height;
        FilesCard.Margin = narrow ? new Thickness(16, 12, 16, 0) : new Thickness(24, 16, 8, 16);
        GoogleCard.Margin = narrow ? new Thickness(16, 12, 16, 16) : new Thickness(8, 16, 24, 16);
    }

    private void UpdatePreview()
    {
        PreviewOk.IsVisible = false;
        PreviewKept.IsVisible = false;
        PreviewError.IsVisible = false;
        PreviewEmpty.IsVisible = false;
        PreviewDoc.IsVisible = false;
        bool doc = Results.SelectedItem is ResultItem { IsDocument: true };
        bool pdf = Results.SelectedItem is ResultItem { IsPdf: true };
        PreviewHeading.Text = Loc.T(pdf ? "Doc.PdfTitle" : doc ? "Doc.Title" : "Preview.Title");
        PageGroup.IsVisible = !doc;

        if (Results.SelectedItem is not ResultItem item || item.IsWorking)
        {
            PreviewEmpty.Text = Loc.T(Results.SelectedItem is ResultItem ? "Preview.Working" : "Preview.Empty");
            PreviewEmpty.IsVisible = true;
            return;
        }
        if (item.IsError)
        {
            PreviewErrorBody.Text = item.ErrorBody;
            PreviewError.IsVisible = true;
            return;
        }
        if (item.IsKept)
        {
            PreviewKeptBody.Text = item.IsDocument ? item.KeptBody : Loc.F("Preview.KeptBody", item.DisplayName);
            PreviewKept.IsVisible = true;
            return;
        }

        var r = item.Result!;
        if (r.Document is { } d)
        {
            DocSizeA.Text = Loc.Bytes(r.OriginalBytes);
            DocSizeB.Text = Loc.Bytes(r.OutputBytes);
            DocPicsA.Text = Loc.Bytes(d.PicturesBefore);
            DocPicsB.Text = Loc.Bytes(d.PicturesAfter);
            DocCounts.Text = Loc.F("Doc.Counts", d.Pictures, d.Shrunk, d.Kept);
            DocSkipped.Text = d.Skipped > 0 ? Loc.F(pdf ? "Doc.PdfSkipped" : "Doc.Skipped", d.Skipped) : "";
            DocSkipped.IsVisible = d.Skipped > 0;
            DocPagesLabel.IsVisible = DocPagesA.IsVisible = DocPagesB.IsVisible = PdfPart.IsVisible = pdf;
            DocxPart.IsVisible = !pdf;
            DocPagesA.Text = d.Pages.ToString(Loc.Instance.Culture);
            DocPagesB.Text = d.Pages.ToString(Loc.Instance.Culture);
            DocDpiNote.Text = pdf ? Loc.F("Doc.PdfDpi", d.Dpi, Loc.T("Preset." + _s.CurrentPreset)) : "";
            DocCompareButton.IsVisible = item.CanCompare;
            PreviewDoc.IsVisible = true;
            return;
        }
        CompareButton.IsVisible = item.CanCompare;
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
        PreviewOk.IsVisible = true;
    }

    /// <summary>The status is kept as a recipe, so a language switch re-renders it in the new language.</summary>
    private void SetStatus(Func<string> text)
    {
        _status = text;
        RenderStatus();
    }

    private void RenderStatus() => StatusText.Text = _status();

    protected override void OnClosed(EventArgs e)
    {
        Loc.Instance.Changed -= ApplyLanguage;
        base.OnClosed(e);
    }
}
