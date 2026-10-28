using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Nacho.Core;
using Nacho.Core.Google;
using Nacho.Core.Word;

namespace Nacho.Linux;

/// <summary>
/// Shrink a Word file in the same five steps as a Google Doc (H1, H4): Choose it on this computer or in Google Drive,
/// Check it (reads only; the smaller file is made in a private work folder), Decide (a new copy by default, or the file
/// itself), Replace, Done. Changing the file itself first saves the original in Docs-Ready/Word originals, so
/// "Restore original" can put it back (H3); in Drive, the old content stays in Manage versions.
/// </summary>
public partial class WordDocWindow : Window
{
    private enum Stage { Choose, Checking, Summary, Applying, Done }

    private readonly AppSettings _s;
    private Stage _stage = Stage.Choose;
    private CancellationTokenSource? _cts;
    private WordCheck? _check;
    private WordOutcome? _outcome;
    private string? _error;
    private bool _intoCopy;
    private bool _restored;
    private bool _rendering;
    private string _safeLine = "";
    private Func<string>? _busy;
    private int _checkDone, _checkTotal;
    private readonly Stopwatch _clock = new();
    private TimeSpan _applyTime;
    private readonly List<string> _queue = new();

    public event Action<WordOutcome>? Finished;

    public event Action<WordOutcome>? Restored;

    public event Func<Task>? OptionsRequested;

    public WordDocWindow() : this(new AppSettings()) { }

    public WordDocWindow(AppSettings settings)
    {
        _s = settings;
        _intoCopy = settings.WordIntoCopy;
        InitializeComponent();
        Icon = App.Icon();
        BuildSteps();
        Loc.Instance.Changed += Render;
        ActualThemeVariantChanged += (_, _) => Render();
        Closing += OnClosing;
        SizeChanged += (_, _) => ApplyLayout();
        AddHandler(DragDrop.DragEnterEvent, Window_DragOver);
        AddHandler(DragDrop.DragOverEvent, Window_DragOver);
        AddHandler(DragDrop.DropEvent, Window_Drop);
        Render();
        Opened += (_, _) =>
        {
            ApplyLayout();
            if (_stage == Stage.Choose) PcButton.Focus();
        };
    }

    // ---------- hooks for the main window and the self-test ----------

    public string StageName => _stage.ToString();
    public string Busy => _busy?.Invoke() ?? "";
    public string Done => DoneTitle.Text ?? "";
    public string DoneDetail => DoneSub.Text ?? "";
    public string? Error => _error;
    public bool IsWorking => _stage is Stage.Checking or Stage.Applying;
    public string SafeLine => _safeLine;
    public string PrimaryLabel => PrimaryButton.IsVisible ? PrimaryButton.Content as string ?? "" : "";
    public string WillLine => WillText.Text ?? "";
    public int RecentCount => RecentList.Children.Count;
    public int QueueCount => _queue.Count;
    public WordCheck? Check => _check;
    public WordOutcome? Outcome => _outcome;
    public bool WasRestored => _restored;

    public bool IntoCopy
    {
        get => _intoCopy;
        set
        {
            _intoCopy = value;
            Render();
        }
    }

    /// <summary>Checks a Word file on this computer; while another one is being worked on, it waits its turn.</summary>
    public async Task OpenLocalAsync(string path)
    {
        if (IsWorking || _stage == Stage.Summary)
        {
            if (!_queue.Contains(path) && _check?.SourcePath != path) _queue.Add(path);
            Render();
            return;
        }
        Reset();
        await RunCheckLocalAsync(path);
    }

    public void Enqueue(IEnumerable<string> paths)
    {
        foreach (var p in paths)
            if (!_queue.Contains(p)) _queue.Add(p);
        Render();
    }

    public Task ChooseInDriveAsync() => RunCheckDriveAsync();

    public Task ApplyAsync() => RunApplyAsync();

    public void RestoreOriginal() => DoRestore();

    public void Refresh()
    {
        if (!IsWorking && _stage is Stage.Choose or Stage.Summary) _intoCopy = _s.WordIntoCopy;
        Render();
    }

    // ---------- steps ----------

    private async void Pc_Click(object? sender, RoutedEventArgs e)
    {
        if (IsWorking) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.T("Wd.DlgTitle"),
                AllowMultiple = true,
                FileTypeFilter = MainWindow.ParseFilter(Loc.T("Wd.DlgFilter")),
            });
            var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().Where(WordText.IsWordFile).ToList();
            if (paths.Count == 0) return;
            Enqueue(paths.Skip(1));
            await OpenLocalAsync(paths[0]);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private async void Drive_Click(object? sender, RoutedEventArgs e) => await RunCheckDriveAsync();

    private async void Primary_Click(object? sender, RoutedEventArgs e)
    {
        switch (_stage)
        {
            case Stage.Summary:
                await RunApplyAsync();
                break;
            case Stage.Done:
                OpenResult();
                break;
        }
    }

    private void BeginCheck()
    {
        _check?.Dispose();
        _check = null;
        _outcome = null;
        _restored = false;
        _error = null;
        _checkDone = _checkTotal = 0;
        _cts = new CancellationTokenSource();
        _stage = Stage.Checking;
        _clock.Restart();
    }

    private Action<int, int> Reporter()
    {
        IProgress<(int Done, int Total)> p = new Progress<(int Done, int Total)>(v =>
        {
            if (_stage != Stage.Checking) return;
            _checkDone = v.Done;
            _checkTotal = v.Total;
            if (v.Total > 0) _busy = () => Loc.F("Gd.CheckingN", _checkDone, _checkTotal);
            RenderCheck();
        });
        return (d, t) => p.Report((d, t));
    }

    private async Task RunCheckLocalAsync(string path)
    {
        if (IsWorking) return;
        BeginCheck();
        var token = _cts!.Token;
        _busy = () => Loc.T("Wd.Reading");
        Render();
        try
        {
            var preset = _s.GetPreset().Clone();
            var options = _s.ToOptions();
            var progress = Reporter();
            _check = await Task.Run(() => new WordShrinker(new ImageShrinker()).CheckLocal(path, preset, options, progress, token), token);
            _stage = Stage.Summary;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _stage = Stage.Choose;
        }
        catch (Exception ex) when (Expected(ex))
        {
            App.Log(ex);
            _stage = Stage.Choose;
            ShowError(Message(ex, Path.GetFileName(path)));
        }
        finally
        {
            EndWork();
        }
        Render();
        FocusPrimary();
    }

    private async Task RunCheckDriveAsync()
    {
        if (IsWorking) return;
        var session = GoogleUi.For(_s);
        if (!session.IsAvailable)
        {
            ShowError(Loc.T("Gd.NotSetUp"));
            return;
        }
        BeginCheck();
        var token = _cts!.Token;
        _busy = () => Loc.T("Wd.Picking");
        Render();
        try
        {
            var (id, email) = await Task.Run(() => session.PickAsync(null, token, GoogleAuth.WordMime), token);
            _s.GoogleEmail = email;
            _s.Save();
            if (!GoogleUi.SelfTestClient) Activate(); // back from the browser
            _busy = () => Loc.T("Wd.Downloading");
            Render();
            var api = session.Api();
            var preset = _s.GetPreset().Clone();
            var options = _s.ToOptions();
            var progress = Reporter();
            var shrinker = new WordShrinker(new ImageShrinker());
            _check = await Task.Run(() => shrinker.CheckDriveAsync(api, id, preset, options, progress, token), token);
            _stage = Stage.Summary;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _stage = Stage.Choose;
        }
        catch (Exception ex) when (Expected(ex))
        {
            App.Log(ex);
            _stage = Stage.Choose;
            if (ex is GoogleException { Kind: GoogleError.SignInNeeded }) _s.GoogleEmail = "";
            ShowError(Message(ex, ""));
        }
        finally
        {
            EndWork();
        }
        Render();
        FocusPrimary();
    }

    private async Task RunApplyAsync()
    {
        if (_check is not { CanShrink: true } check || IsWorking) return;
        _error = null;
        _s.WordIntoCopy = _intoCopy;
        _s.Save();
        bool copy = _intoCopy;
        bool drive = check.Where == WordWhere.Drive;
        string pattern = Loc.T("Gd.CopyName");
        string backup = WordText.BackupFolder(_s.OutputFolder);
        string fallback = _s.OutputFolder;
        bool keep = _s.GoogleKeepOriginals;
        _cts = drive ? new CancellationTokenSource() : null;
        var token = _cts?.Token ?? CancellationToken.None;
        _stage = Stage.Applying;
        _busy = () => Loc.T(drive ? "Wd.Uploading" : copy ? "Wd.Writing" : "Wd.SavingOriginal");
        _clock.Restart();
        Render();
        try
        {
            var shrinker = new WordShrinker(new ImageShrinker());
            _outcome = drive
                ? await Task.Run(() => shrinker.ApplyDriveAsync(GoogleUi.For(_s).Api(), check, copy, pattern, backup, keep, token), token)
                : await Task.Run(() => shrinker.ApplyLocal(check, copy, pattern, fallback, backup));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            EndWork();
            _stage = Stage.Summary;
            Render();
            FocusPrimary();
            return;
        }
        catch (Exception ex) when (Expected(ex))
        {
            App.Log(ex);
            EndWork();
            if (ex is GoogleException { Kind: GoogleError.SignInNeeded }) _s.GoogleEmail = "";
            _stage = Stage.Summary;
            ShowError(Message(ex, check.Name));
            Render();
            FocusPrimary();
            return;
        }
        EndWork();
        _applyTime = _clock.Elapsed;
        _stage = Stage.Done;
        var o = _outcome!;
        string target = o.Where == WordWhere.ThisPc ? o.LocalPath! : o.DriveResult!.Id;
        _s.WordRecent = WordRecent.Add(_s.WordRecent, new WordRecent(o.Where, o.Name, target, DateTime.Now, o.Replaced, o.BytesBefore, o.BytesAfter, o.IntoCopy));
        _s.Save();
        Render();
        Finished?.Invoke(o);
        FocusPrimary();
    }

    private void EndWork()
    {
        _cts?.Dispose();
        _cts = null;
        _busy = null;
    }

    private static bool Expected(Exception ex) => ex is WordException or GoogleException or IOException or UnauthorizedAccessException
        or InvalidDataException or OperationCanceledException or HttpRequestException or System.Net.HttpListenerException;

    private static string Message(Exception ex, string name) => ex switch
    {
        WordException { Kind: WordError.InUse } => Loc.F("Wd.ErrInUse", name),
        WordException w when w.Kind != WordError.None => Loc.T(WordText.ErrorKey(w.Kind)),
        UnauthorizedAccessException => Loc.T("Wd.ErrNoWrite"),
        IOException io => Loc.F("Gd.ErrOther", io.Message),
        _ => GoogleUi.Message(ex),
    };

    private void DoRestore()
    {
        if (_outcome is not { IntoCopy: false, Where: WordWhere.ThisPc } o || _restored) return;
        try
        {
            WordShrinker.Restore(o);
            _restored = true;
            _error = null;
            _s.WordRecent = _s.WordRecent.Where(r => !(r.Where == WordWhere.ThisPc && !r.Copy && r.Target == o.LocalPath)).ToList();
            _s.Save();
            Restored?.Invoke(o);
        }
        catch (Exception ex) when (Expected(ex) || ex is InvalidOperationException)
        {
            App.Log(ex);
            ShowError(ex is WordException { Kind: WordError.InUse } ? Loc.F("Wd.ErrInUse", o.Name) : Loc.F("Wd.ErrRestore", ex.Message));
        }
        Render();
    }

    private void OtherAccount_Click(object? sender, RoutedEventArgs e)
    {
        if (IsWorking) return;
        var session = GoogleUi.For(_s);
        session.Forget();
        session.ChooseAccountNext = true;
        _s.GoogleEmail = "";
        _s.Save();
        Render();
        DriveButton.Focus();
    }

    private async void Left_Click(object? sender, RoutedEventArgs e)
    {
        if (_stage == Stage.Summary)
        {
            Reset();
            PcButton.Focus();
        }
        else if (_stage == Stage.Done)
        {
            if (_queue.Count > 0)
            {
                string next = _queue[0];
                _queue.RemoveAt(0);
                Reset();
                await RunCheckLocalAsync(next);
            }
            else
            {
                Reset();
                PcButton.Focus();
            }
        }
    }

    private async void Change_Click(object? sender, RoutedEventArgs e)
    {
        if (OptionsRequested is { } open) await open();
        Refresh();
    }

    private void Dest_Changed(object? sender, RoutedEventArgs e)
    {
        if (_rendering || _stage != Stage.Summary) return;
        _intoCopy = CopyRadio.IsChecked == true;
        Render();
    }

    private void FileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_check is not { } c) return;
        if (c.Where == WordWhere.ThisPc) WordUi.ShowInFolder(c.SourcePath);
        else if (c.Drive is { } d) WordUi.OpenDriveFile(d.Id);
    }

    private void RowFile_Click(object? sender, RoutedEventArgs e) => OpenResult(inFolder: true);

    private void RowOrig_Click(object? sender, RoutedEventArgs e)
    {
        if (_outcome is not { } o || _check is not { } c) return;
        if (o.Where == WordWhere.ThisPc && !o.IntoCopy) DoRestore();
        else if (o.IntoCopy && c.Where == WordWhere.ThisPc) WordUi.ShowInFolder(c.SourcePath);
        else if (o.IntoCopy && c.Drive is { } d) WordUi.OpenDriveFile(d.Id);
        else if (o.BackupPath is { } b) WordUi.ShowInFolder(b);
    }

    private void OpenResult(bool inFolder = false)
    {
        if (_outcome is not { } o) return;
        if (o.Where == WordWhere.Drive && o.DriveResult is { } d) WordUi.OpenDriveFile(d.Id);
        else if (o.LocalPath is { } p)
        {
            if (inFolder) WordUi.ShowInFolder(p);
            else WordUi.OpenFile(p);
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        if (IsWorking)
        {
            Stop();
            return;
        }
        Close();
    }

    public void Stop()
    {
        if (_cts is null || _cts.IsCancellationRequested) return;
        _cts.Cancel();
        Render();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!IsWorking) return;
        e.Cancel = true;
        bool checking = _stage == Stage.Checking;
        Stop();
        if (!checking) ShowError(Loc.T("Wd.CloseWhileWorking"));
    }

    protected override void OnClosed(EventArgs e)
    {
        Loc.Instance.Changed -= Render;
        _check?.Dispose();
        base.OnClosed(e);
    }

    private void Reset()
    {
        _check?.Dispose();
        _check = null;
        _outcome = null;
        _restored = false;
        _error = null;
        _busy = null;
        _intoCopy = _s.WordIntoCopy;
        _stage = Stage.Choose;
        Render();
    }

    // ---------- dropping a .docx on the window ----------

    private void Window_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = !IsWorking && DroppedWordFiles(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object? sender, DragEventArgs e)
    {
        var files = DroppedWordFiles(e);
        e.Handled = true;
        if (files.Count == 0) return;
        if (IsWorking || _stage == Stage.Summary)
        {
            Enqueue(files);
            return;
        }
        Enqueue(files.Skip(1));
        await OpenLocalAsync(files[0]);
    }

    private static List<string> DroppedWordFiles(DragEventArgs e)
    {
        try
        {
            return e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().Where(WordText.IsWordFile).ToList() ?? new List<string>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return new List<string>();
        }
    }

    // ---------- showing it ----------

    private IBrush? Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out object? v) ? v as IBrush : null;

    private Geometry? Glyph(string key) => this.TryFindResource(key, ActualThemeVariant, out object? v) ? v as Geometry : null;

    private void ShowError(string message)
    {
        _error = message;
        ErrorText.Text = message;
        ErrorPanel.IsVisible = true;
    }

    /// <summary>Below 640 px the two places stack, so nothing is cut off (WCAG 1.4.10).</summary>
    private void ApplyLayout()
    {
        bool narrow = Bounds.Width > 0 && Bounds.Width < 640;
        SourceGrid.Columns = narrow ? 1 : 2;
        PcCard.Margin = new Thickness(0, 0, narrow ? 0 : 6, 10);
        DriveCard.Margin = new Thickness(narrow ? 0 : 6, 0, 0, 10);
        CopyPoints.Columns = FilePoints.Columns = narrow ? 1 : 2;
    }

    private void Render()
    {
        _rendering = true;
        try
        {
            RenderAll();
        }
        finally
        {
            _rendering = false;
        }
    }

    private void RenderAll()
    {
        var session = GoogleUi.For(_s);
        string email = _s.GoogleEmail;
        AccountText.Text = !session.IsAvailable ? Loc.T("Gd.NotSetUp")
            : !session.HasSignIn ? Loc.T("Gd.NotSignedIn")
            : string.IsNullOrEmpty(email) ? Loc.T("Gd.SignedIn")
            : Loc.F("Gd.SignedInAs", email);
        OtherAccountButton.IsVisible = session.IsAvailable && session.HasSignIn;
        OtherAccountButton.IsEnabled = !IsWorking;
        DriveButton.IsEnabled = session.IsAvailable && !IsWorking;
        PcButton.IsEnabled = !IsWorking;
        PcTitle.Text = Loc.T("Wd.PcTitle");

        RenderSteps();
        bool checking = _stage == Stage.Checking && _checkTotal > 0;
        SafeBar.IsVisible = _stage is Stage.Choose or Stage.Checking;
        SafeTag.Text = Loc.T("Gd.ReadOnlyTag");
        string safeTitle = Loc.T(checking ? "Wd.SafeCheckTitle" : "Wd.SafeTitle");
        string safeBody = Loc.T(checking ? "Wd.SafeCheck" : "Gd.SafeChoose");
        _safeLine = safeTitle + " " + safeBody;
        SafeText.Inlines = new InlineCollection
        {
            new Run(safeTitle + " ") { FontWeight = FontWeight.SemiBold },
            new Run(safeBody),
        };

        ChoosePanel.IsVisible = _stage == Stage.Choose;
        var p = _s.GetPreset();
        PresetChip.Text = Loc.F("Gd.ChipPreset", Loc.T("Preset." + p.Id), p.LongEdge);
        OriginalsChip.Text = Loc.T(_s.GoogleKeepOriginals ? "Gd.ChipOriginalsOn" : "Gd.ChipOriginalsOff");
        RenderRecent();

        FileCard.IsVisible = _check is not null && _stage == Stage.Summary;
        if (_check is { } c)
        {
            FileName.Text = c.Name;
            FileLine.Text = c.Where == WordWhere.ThisPc
                ? string.Join(" · ", Loc.T("Wd.PcTitle"), MainWindow.FolderName(Path.GetDirectoryName(c.SourcePath) ?? ""), Loc.Bytes(c.BytesBefore))
                : string.Join(" · ", Loc.T("Wd.DriveTitle"), Loc.Bytes(c.BytesBefore));
            FileButton.Content = Loc.T(c.Where == WordWhere.ThisPc ? "Wd.ShowFolder" : "Gd.OpenBrowser");
        }

        CheckPanel.IsVisible = _stage == Stage.Checking;
        DecidePanel.IsVisible = _stage == Stage.Summary;
        ApplyPanel.IsVisible = _stage == Stage.Applying;
        DonePanel.IsVisible = _stage == Stage.Done;
        ErrorPanel.IsVisible = _error is not null;
        if (_error is not null) ErrorText.Text = _error;

        if (_stage == Stage.Checking) RenderCheck();
        if (_stage == Stage.Summary) RenderDecide();
        if (_stage == Stage.Applying) ApplyTitle.Text = _busy?.Invoke() ?? "";
        if (_stage == Stage.Done) RenderDone();
        RenderButtons();
        RenderTitle();
    }

    private readonly List<(Border Dot, TextBlock Mark, StrokeIcon Tick, TextBlock Label, Control Item)> _steps = new();
    private readonly List<Border> _stepLines = new();

    private void BuildSteps()
    {
        StepsGrid.ColumnDefinitions.Clear();
        for (int i = 0; i < 5; i++)
        {
            StepsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            if (i < 4) StepsGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 12 });
            var mark = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, FontWeight = FontWeight.SemiBold };
            var tick = new StrokeIcon { Width = 14, Height = 14, Thickness = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Child = new Panel { Children = { mark, tick } } };
            var label = new TextBlock { FontSize = 13, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var item = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, label } };
            Grid.SetColumn(item, i * 2);
            StepsGrid.Children.Add(item);
            _steps.Add((dot, mark, tick, label, item));
            if (i < 4)
            {
                var line = new Border { Height = 2, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(line, i * 2 + 1);
                StepsGrid.Children.Add(line);
                _stepLines.Add(line);
            }
        }
    }

    private int CurrentStep => _stage switch
    {
        Stage.Choose => 0,
        Stage.Checking => 1,
        Stage.Summary => 2,
        Stage.Applying => 3,
        _ => 4,
    };

    private void RenderSteps()
    {
        int current = CurrentStep;
        bool allDone = _stage == Stage.Done;
        for (int i = 0; i < _steps.Count; i++)
        {
            var (dot, mark, tick, label, item) = _steps[i];
            bool done = i < current || (allDone && i == current);
            bool now = i == current && !allDone;
            label.Text = Loc.T("Gd.Step" + (i + 1));
            label.FontWeight = i == current ? FontWeight.SemiBold : FontWeight.Normal;
            label.Foreground = Brush(i == current ? "N.Text" : "N.TextSecondary");
            tick.IsVisible = done;
            mark.IsVisible = !done;
            mark.Text = (i + 1).ToString(Loc.Instance.Culture);
            if (done)
            {
                tick.Data = Glyph("Icon.Check");
                tick.Foreground = Brush("N.OnAccent");
                dot.Background = Brush("N.Success");
                dot.BorderThickness = new Thickness(0);
            }
            else if (now)
            {
                dot.Background = Brush("N.Accent");
                dot.BorderThickness = new Thickness(0);
                mark.Foreground = Brush("N.OnAccent");
            }
            else
            {
                dot.Background = Brushes.Transparent;
                dot.BorderThickness = new Thickness(1);
                dot.BorderBrush = Brush("N.StrongStroke");
                mark.Foreground = Brush("N.TextSecondary");
            }
            string state = done ? Loc.T("Gd.StepDoneName") : now ? Loc.T("Gd.StepNow") : "";
            AutomationProperties.SetName(item, Loc.F("Gd.StepName", i + 1, 5, label.Text) + (state.Length > 0 ? ", " + state : ""));
            if (i < _stepLines.Count) _stepLines[i].Background = Brush(i < current || allDone ? "N.Success" : "N.StrongStroke");
        }
    }

    private void RenderRecent()
    {
        RecentList.Children.Clear();
        foreach (var r in _s.WordRecent)
        {
            var open = new Button { Content = Loc.T("Gd.Open"), VerticalAlignment = VerticalAlignment.Center };
            open.Classes.Add("link");
            var item = r;
            open.Click += (_, _) =>
            {
                if (item.Where == WordWhere.Drive) WordUi.OpenDriveFile(item.Target);
                else WordUi.OpenFile(item.Target);
            };
            open.IsEnabled = r.Where == WordWhere.Drive || File.Exists(r.Target);
            AutomationProperties.SetName(open, Loc.T("Gd.Open") + " " + r.Name);
            var icon = new StrokeIcon
            {
                Data = Glyph(r.Where == WordWhere.Drive ? "Icon.Cloud" : "Icon.Computer"),
                Width = 18, Height = 18, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush("N.TextSecondary"),
            };
            var title = new TextBlock { Text = r.Name, FontSize = 14, TextWrapping = TextWrapping.Wrap };
            var line = new TextBlock
            {
                Text = Loc.F("Wd.RecentLine", When(r.When), Loc.T(r.Where == WordWhere.Drive ? "Wd.WhereDrive" : "Wd.WherePc"), r.Replaced,
                    Loc.Bytes(r.BytesBefore), Loc.Bytes(r.BytesAfter), Loc.T(r.Copy ? "Gd.RecentCopy" : "Wd.RecentInPlace")),
            };
            line.Classes.Add("hint");
            var dock = new DockPanel();
            DockPanel.SetDock(open, Dock.Right);
            DockPanel.SetDock(icon, Dock.Left);
            dock.Children.Add(open);
            dock.Children.Add(icon);
            dock.Children.Add(new StackPanel { Children = { title, line } });
            var card = new Border { Padding = new Thickness(14, 8, 8, 8), Margin = new Thickness(0, 0, 0, 6), Child = dock };
            card.Classes.Add("card");
            RecentList.Children.Add(card);
        }
        RecentPanel.IsVisible = RecentList.Children.Count > 0;
    }

    private static string When(DateTime t) => t.Date == DateTime.Today
        ? Loc.F("Gd.Today", t.ToString("t", Loc.Instance.Culture))
        : t.Date == DateTime.Today.AddDays(-1)
            ? Loc.F("Wd.Yesterday", t.ToString("t", Loc.Instance.Culture))
            : t.ToString("g", Loc.Instance.Culture);

    private static string Duration(TimeSpan t)
    {
        var (key, args) = GoogleText.Duration(t);
        return Loc.F(key, args);
    }

    private void RenderCheck()
    {
        CheckTitle.Text = _busy?.Invoke() ?? Loc.T("Wd.Reading");
        CheckBar.IsIndeterminate = _checkTotal == 0;
        CheckBar.Maximum = Math.Max(1, _checkTotal);
        CheckBar.Value = _checkDone;
        RenderTitle();
    }

    private void RenderDecide()
    {
        if (_check is not { } c) return;
        bool pc = c.Where == WordWhere.ThisPc;
        long before = c.BytesBefore, after = c.BytesAfter;
        ResultTitle.Text = Loc.F("Wd.ResultTitle", GoogleText.PercentSmaller(before, after));
        CheckedAt.Text = Loc.F("Gd.Checked", c.CheckedAt.ToString("t", Loc.Instance.Culture));
        BeforeText.Text = Loc.Bytes(before);
        AfterText.Text = Loc.Bytes(after);
        double share = before > 0 ? Math.Clamp((double)after / before, 0.01, 1) : 1;
        AfterBar.ColumnDefinitions[0].Width = new GridLength(share, GridUnitType.Star);
        AfterBar.ColumnDefinitions[1].Width = new GridLength(1 - share + 0.0001, GridUnitType.Star);

        var report = c.Report;
        CountChips.Children.Clear();
        AddChip(Loc.F("Gd.ChipWill", c.CanShrink ? report.Shrunk : 0), success: true);
        AddChip(Loc.F("Gd.ChipKept", report.Kept), success: false);
        AddChip(Loc.F("Gd.ChipSkip", report.Skipped), success: false);

        bool any = c.CanShrink;
        NothingText.IsVisible = !any;
        WherePanel.IsVisible = any;
        string copyName = GoogleText.CopyName(Loc.T("Gd.CopyName"), c.Stem) + c.Extension;
        CopySub.Text = Loc.F(pc ? "Wd.CopySubPc" : "Gd.IntoCopySub", copyName);
        Points(CopyPoints, pc
            ? new[] { ("Gd.CopyPro1", true), ("Gd.CopyPro2", true), ("Wd.CopyProOpen", true), ("Wd.CopyConTwo", false) }
            : new[] { ("Gd.CopyPro1", true), ("Wd.DriveCopyPro", true), ("Gd.CopyPro2", true), ("Gd.CopyCon1", false) });
        Points(FilePoints, pc
            ? new[] { ("Wd.FilePro1", true), ("Wd.FilePro2", true), ("Wd.FileCon1", false), ("Wd.FileCon2", false) }
            : new[] { ("Gd.DocPro1", true), ("Wd.DriveFilePro2", true), ("Wd.DriveFileCon1", false), ("Wd.DriveFileCon2", false) });
        CopyRadio.IsChecked = _intoCopy;
        FileRadio.IsChecked = !_intoCopy;
        CopyCard.Classes.Set("selected", _intoCopy);
        FileChoiceCard.Classes.Set("selected", !_intoCopy);
        WillText.Text = (pc, _intoCopy) switch
        {
            (true, true) => Loc.F("Wd.WillCopyPc", c.Name),
            (true, false) => Loc.F("Wd.WillFilePc", c.Name),
            (false, true) => Loc.F("Wd.WillCopyDrive", c.Name),
            _ => Loc.F("Wd.WillFileDrive", c.Name) + (_s.GoogleKeepOriginals ? " " + Loc.T("Wd.WillKeepPc") : ""),
        };
        WillBanner.Background = Brush(_intoCopy ? "N.AttentionBg" : "N.CautionBg");
        WillBanner.BorderBrush = Brush(_intoCopy ? "N.Accent" : "N.Caution");
        WillIcon.Foreground = Brush(_intoCopy ? "N.Accent" : "N.Caution");
    }

    /// <summary>Pros (a tick) and cons (an info mark): told apart by shape, not only by colour (WCAG 1.4.1).</summary>
    private void Points(UniformGrid target, (string Key, bool Pro)[] points)
    {
        target.Children.Clear();
        foreach (var (key, pro) in points)
        {
            var icon = new StrokeIcon
            {
                Data = Glyph(pro ? "Icon.Check" : "Icon.Info"),
                Width = 14, Height = 14, Margin = new Thickness(0, 2, 6, 0), VerticalAlignment = VerticalAlignment.Top,
                Foreground = Brush(pro ? "N.Success" : "N.Caution"),
            };
            var dock = new DockPanel { Margin = new Thickness(0, 0, 10, 4) };
            DockPanel.SetDock(icon, Dock.Left);
            dock.Children.Add(icon);
            dock.Children.Add(new TextBlock { Text = Loc.T(key), FontSize = 13, TextWrapping = TextWrapping.Wrap });
            target.Children.Add(dock);
        }
    }

    private void AddChip(string text, bool success)
    {
        var t = new TextBlock { Text = text, FontSize = 13 };
        var b = new Border { Child = t };
        b.Classes.Add("chip");
        if (success)
        {
            b.Background = Brush("N.SuccessBg");
            t.Foreground = Brush("N.Success");
            t.FontWeight = FontWeight.SemiBold;
        }
        CountChips.Children.Add(b);
    }

    private void RenderDone()
    {
        if (_outcome is not { } o || _check is not { } c) return;
        bool pc = o.Where == WordWhere.ThisPc;
        DoneTitle.Text = o.IntoCopy ? Loc.T("Wd.DoneCopyTitle") : Loc.F("Wd.DoneTitle", o.Name);
        DoneSub.Text = Loc.F("Wd.DoneSub", o.Replaced, o.Replaced, Duration(_applyTime));
        DoneBefore.Text = Loc.Bytes(o.BytesBefore);
        DoneAfter.Text = Loc.Bytes(o.BytesAfter);
        DonePct.Text = Loc.F("Gd.Smaller", GoogleText.PercentSmaller(o.BytesBefore, o.BytesAfter));

        RowFileName.Text = o.Name;
        string folder = pc && o.LocalPath is { } lp ? MainWindow.FolderName(Path.GetDirectoryName(lp) ?? "") : "";
        RowFileSub.Text = (pc, o.IntoCopy) switch
        {
            (true, true) => o.WentToFallbackFolder ? Loc.T("Wd.RowFallback") : Loc.F("Wd.RowNewPc", folder),
            (true, false) => Loc.F("Wd.RowThisPc", folder),
            (false, true) => Loc.T(o.WentToFallbackFolder ? "Wd.RowDriveRoot" : "Gd.RowCopySub"),
            _ => Loc.T("Wd.RowThisDrive"),
        };
        RowFileButton.Content = Loc.T(pc ? "Wd.ShowFolder" : "Gd.OpenBrowser");

        bool origRow = o.IntoCopy || pc || o.BackupPath is not null;
        RowOrig.IsVisible = origRow;
        RowOrigLine.IsVisible = origRow;
        RowOrigButton.IsEnabled = true;
        if (o.IntoCopy)
        {
            RowOrigIcon.Data = Glyph("Icon.Lock");
            RowOrigName.Text = c.Name;
            RowOrigSub.Text = Loc.T("Gd.RowOriginalSub");
            RowOrigButton.Content = Loc.T(pc ? "Wd.ShowFolder" : "Gd.OpenOriginal");
        }
        else if (pc)
        {
            RowOrigIcon.Data = Glyph("Icon.Restore");
            RowOrigName.Text = Loc.F("Wd.RowOriginal", Loc.Bytes(o.BytesBefore));
            RowOrigSub.Text = o.BackupPath is { } b ? WordText.ShortPath(b) : "";
            RowOrigButton.Content = Loc.T(_restored ? "Wd.Restored" : "Wd.Restore");
            RowOrigButton.IsEnabled = !_restored;
        }
        else
        {
            RowOrigIcon.Data = Glyph("Icon.Folder");
            RowOrigName.Text = Loc.F("Wd.RowOriginalPc", Loc.Bytes(o.BytesBefore));
            RowOrigSub.Text = o.BackupPath is { } b ? WordText.ShortPath(b) : "";
            RowOrigButton.Content = Loc.T("Gd.Show");
        }
        AutomationProperties.SetName(RowOrigButton, (RowOrigButton.Content as string ?? "") + " " + RowOrigName.Text);
        AutomationProperties.SetName(RowFileButton, (RowFileButton.Content as string ?? "") + " " + RowFileName.Text);
        DoneNote.Text = _restored ? Loc.F("Status.WordRestored", o.Name)
            : !pc && !o.IntoCopy ? Loc.T("Wd.NoteDrive")
            : o.IntoCopy ? Loc.T("Wd.NoteCopy") : Loc.T("Wd.NoteThisPc");
    }

    private void RenderButtons()
    {
        PrimaryButton.IsDefault = _stage is Stage.Summary or Stage.Done;
        PcButton.IsDefault = _stage == Stage.Choose;
        HintText.Text = "";
        switch (_stage)
        {
            case Stage.Choose:
                PrimaryButton.IsVisible = false;
                CloseButton.Content = Loc.T("Gd.Close");
                LeftButton.IsVisible = false;
                HintText.Text = Loc.T("Gd.HintChoose");
                break;
            case Stage.Checking:
                PrimaryButton.IsVisible = false;
                CloseButton.Content = Loc.T("Gd.Cancel");
                LeftButton.IsVisible = false;
                HintText.Text = Loc.T("Gd.HintCheck");
                break;
            case Stage.Summary:
                bool any = _check?.CanShrink == true;
                int n = _check?.Report.Shrunk ?? 0;
                bool drive = _check?.Where == WordWhere.Drive;
                PrimaryButton.Content = _intoCopy ? Loc.T("Gd.MakeCopy") : drive ? Loc.T("Wd.Upload") : n == 1 ? Loc.T("Wd.Replace1") : Loc.F("Wd.ReplaceN", n);
                PrimaryButton.IsVisible = any;
                PrimaryButton.IsEnabled = any;
                CloseButton.Content = Loc.T("Gd.Cancel");
                LeftButton.Content = Loc.T("Gd.Back");
                LeftButton.IsVisible = true;
                break;
            case Stage.Applying:
                PrimaryButton.IsVisible = false;
                CloseButton.Content = Loc.T(_cts is not null ? "Gd.Stop" : "Gd.Close");
                LeftButton.IsVisible = false;
                break;
            case Stage.Done:
                PrimaryButton.Content = Loc.T(_outcome?.Where == WordWhere.Drive ? "Gd.OpenBrowser" : "Wd.OpenWord");
                PrimaryButton.IsVisible = true;
                PrimaryButton.IsEnabled = true;
                CloseButton.Content = Loc.T("Gd.Close");
                LeftButton.Content = _queue.Count > 0 ? Loc.F("Wd.Next", _queue.Count) : Loc.T("Wd.Another");
                LeftButton.IsVisible = true;
                break;
        }
        CloseButton.IsEnabled = _stage != Stage.Applying || _cts is { IsCancellationRequested: false };
        AutomationProperties.SetName(PrimaryButton, PrimaryButton.Content as string ?? "");
    }

    private void RenderTitle()
    {
        string title = _stage == Stage.Done ? Loc.T("Gd.Step5") + " · " + Loc.T("Wd.Title") : Loc.T("Wd.Title");
        int pct = _stage == Stage.Checking && _checkTotal > 0 ? 100 * _checkDone / _checkTotal : -1;
        Title = pct >= 0 ? $"{pct}% · {title}" : title;
    }

    private void FocusPrimary()
    {
        if (_stage == Stage.Choose)
        {
            PcButton.Focus();
            return;
        }
        if (PrimaryButton.IsVisible && PrimaryButton.IsEnabled) PrimaryButton.Focus();
        else CloseButton.Focus();
    }
}

/// <summary>Opening files, folders and Drive files for the Word window. Addresses are always rebuilt from the file ID.</summary>
public static class WordUi
{
    public static void OpenDriveFile(string fileId)
    {
        if (GoogleUi.SelfTestClient || !System.Text.RegularExpressions.Regex.IsMatch(fileId, "^[A-Za-z0-9_-]{10,200}$")) return;
        Shell.Open(DriveFile.OpenUrl(fileId));
    }

    public static void OpenFile(string path)
    {
        if (GoogleUi.SelfTestClient || !File.Exists(path) || !WordText.IsWordFile(path)) return;
        Shell.Open(path);
    }

    public static void ShowInFolder(string path)
    {
        if (GoogleUi.SelfTestClient) return;
        if (File.Exists(path)) Shell.ShowInFolder(path);
        else GoogleUi.OpenFolder(Path.GetDirectoryName(path) ?? "");
    }
}
