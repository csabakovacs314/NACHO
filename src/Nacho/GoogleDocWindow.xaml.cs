using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using Nacho.Core;
using Nacho.Core.Google;

namespace Nacho;

/// <summary>
/// Shrink a Google Doc in five visible steps (H1): Choose the Doc in Google's picker (or paste its link), Check it
/// (reads only), Decide where the smaller pictures go (a new copy by default, or the Doc itself), Replace, Done.
/// What NACHO may change is always on screen: "read only" while choosing and checking, the two Docs side by side while
/// replacing. Stop is one key away (Esc). NACHO only has access to the Docs the user picks (drive.file).
/// </summary>
public partial class GoogleDocWindow : Window
{
    private enum Stage { Link, Checking, Summary, Applying, Done }

    private const int MaxTiles = 240;

    private readonly AppSettings _s;
    private Stage _stage = Stage.Link;
    private CancellationTokenSource? _cts;
    private GoogleDocPlan? _plan;
    private GoogleApi? _api;
    private string? _docId;
    private string? _error;
    private bool _applyStopped;
    private bool _intoCopy;
    private bool _copyRun;

    // Live state of the check and the replacement, for the progress views.
    private GoogleDocInfo? _doc;
    private Func<string>? _busy;
    private readonly Dictionary<int, GooglePictureState> _states = new();
    private int _checkDone, _checkTotal;
    private long _downloaded;
    private GooglePictureJob? _last;
    private readonly Stopwatch _clock = new();
    private DateTime _checkedAt;
    private TimeSpan? _copyTime;
    private int _applyDone, _applyTotal;
    private TimeSpan _applyTime;
    private readonly List<(DateTime When, GooglePictureJob Job)> _log = new();
    private Rectangle[] _tiles = Array.Empty<Rectangle>();

    /// <summary>Raised when pictures were replaced, so the main window can show it and count the savings.</summary>
    public event Action<GoogleDocPlan>? Finished;

    /// <summary>Raised by "Change…" next to the preset: the main window opens More options.</summary>
    public event Action? OptionsRequested;

    public GoogleDocWindow(AppSettings settings, string? link = null)
    {
        _s = settings;
        _intoCopy = settings.GoogleIntoCopy;
        InitializeComponent();
        LinkBox.Text = link ?? "";
        BuildSteps();
        Loc.Instance.Changed += Render;
        Closing += OnClosing;
        Render();
        Loaded += (_, _) =>
        {
            if (LinkBox.Text.Length > 0)
            {
                LinkBox.Focus();
                LinkBox.SelectAll();
            }
            else
            {
                ChooseButton.Focus();
            }
        };
    }

    // ---------- hooks for the main window and the self-test ----------

    public string? DocId => _docId;
    public GoogleDocPlan? Plan => _plan;
    public string Busy => _busy?.Invoke() ?? "";
    public string Done => DoneTitle.Text;
    public string DoneDetail => DoneSub.Text;
    public string? Error => _error;
    public bool IsWorking => _stage is Stage.Checking or Stage.Applying;
    public bool LinkErrorShown => LinkError.Visibility == Visibility.Visible;
    public string StageName => _stage.ToString();
    public string SafeLine => SafeText.Text;
    public string PrimaryLabel => _stage == Stage.Link
        ? (string)(LinkBox.Text.Trim().Length == 0 ? ChooseButton.Content : CheckLinkButton.Content)
        : PrimaryButton.Content as string ?? "";
    public int RecentCount => RecentList.Children.Count;

    /// <summary>Where the smaller pictures go: a new copy (default) or the Doc itself.</summary>
    public bool IntoCopy
    {
        get => _intoCopy;
        set
        {
            _intoCopy = value;
            Render();
        }
    }

    /// <summary>Puts a link in and, when NACHO is signed in, starts the check (checking changes nothing; the picker
    /// opens only if Google says NACHO hasn't been given this Doc yet).</summary>
    public async Task UseLinkAsync(string link, bool autoCheck)
    {
        if (IsWorking) return;
        if (_stage is not Stage.Link) Reset();
        LinkBox.Text = link;
        if (autoCheck && GoogleDocLink.TryGetId(link) is not null && GoogleUi.For(_s).HasSignIn) await CheckAsync();
    }

    /// <summary>With a link in the box, checks that Doc; with an empty box, opens Google's picker first.</summary>
    public Task CheckAsync() => RunCheckAsync();

    public Task ReplaceAsync() => RunApplyAsync();

    /// <summary>Re-reads the settings (after More options).</summary>
    public void Refresh()
    {
        if (!IsWorking && _stage is Stage.Link or Stage.Summary) _intoCopy = _s.GoogleIntoCopy;
        Render();
    }

    // ---------- steps ----------

    private async void Choose_Click(object sender, RoutedEventArgs e)
    {
        LinkBox.Text = "";
        await RunCheckAsync();
    }

    private async void CheckLink_Click(object sender, RoutedEventArgs e) => await RunCheckAsync();

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        switch (_stage)
        {
            case Stage.Summary:
                await RunApplyAsync();
                break;
            case Stage.Done:
                OpenTarget();
                break;
        }
    }

    private async Task RunCheckAsync()
    {
        if (IsWorking) return;
        _error = null;
        string text = LinkBox.Text.Trim();
        string? linkId = text.Length == 0 ? null : GoogleDocLink.TryGetId(text);
        if (text.Length > 0 && linkId is null)
        {
            ShowLinkError();
            return;
        }
        var session = GoogleUi.For(_s);
        if (!session.IsAvailable)
        {
            ShowError(Loc.T("Gd.NotSetUp"));
            return;
        }

        _plan?.Dispose();
        _plan = null;
        _doc = null;
        _states.Clear();
        _checkDone = _checkTotal = 0;
        _downloaded = 0;
        _last = null;
        _cts = new CancellationTokenSource();
        _stage = Stage.Checking;
        _api = session.Api();
        Render();
        var token = _cts.Token;
        try
        {
            // No link, or no sign-in yet: Google's picker does both, and gives NACHO this one Doc.
            bool picked = linkId is null || !session.HasSignIn;
            string id = picked ? await PickAsync(session, linkId, token) : linkId!;
            try
            {
                _plan = await CheckDocAsync(id, token);
            }
            catch (GoogleException ex) when (!picked && (GoogleText.NeedsPick(ex.Kind) || ex.Kind == GoogleError.SignInNeeded))
            {
                // NACHO hasn't been given this Doc yet (or the sign-in was removed): ask once in the picker, then read again.
                id = await PickAsync(session, id, token);
                _plan = await CheckDocAsync(id, token);
            }
            _checkedAt = DateTime.Now;
            _stage = Stage.Summary;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _stage = Stage.Link;
        }
        catch (Exception ex) when (ex is GoogleException or OperationCanceledException or System.Net.Http.HttpRequestException or System.Net.HttpListenerException)
        {
            App.Log(ex);
            _stage = Stage.Link;
            if (ex is GoogleException { Kind: GoogleError.SignInNeeded }) _s.GoogleEmail = "";
            ShowError(GoogleUi.Message(ex));
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
        Render();
        FocusPrimary();
    }

    /// <summary>Opens Google's picker in the browser (it signs in too). With <paramref name="docId"/> it offers just that Doc.</summary>
    private async Task<string> PickAsync(GoogleSession session, string? docId, CancellationToken token)
    {
        _busy = () => Loc.T(docId is null ? "Gd.Picking" : "Gd.PickThis");
        Render();
        var (id, email) = await Task.Run(() => session.PickAsync(docId, token), token);
        _s.GoogleEmail = email;
        _s.Save();
        _docId = id;
        LinkBox.Text = GoogleDocLink.EditUrl(id);
        LinkError.Visibility = Visibility.Collapsed;
        if (!GoogleUi.SelfTestClient) Activate(); // back from the browser
        return id;
    }

    private async Task<GoogleDocPlan> CheckDocAsync(string id, CancellationToken token)
    {
        _docId = id;
        _busy = () => Loc.T("Gd.Reading");
        _clock.Restart();
        Render();
        var progress = new Progress<GoogleProgress>(OnCheckProgress);
        var preset = _s.GetPreset().Clone();
        var options = _s.ToOptions();
        string? originals = _s.GoogleKeepOriginals ? _s.OutputFolder : null;
        var shrinker = new GoogleDocShrinker(_api!);
        return await Task.Run(() => shrinker.CheckAsync(id, preset, options, originals, progress, token), token);
    }

    private void OnCheckProgress(GoogleProgress p)
    {
        if (_stage != Stage.Checking) return;
        if (p.Doc is { } doc && _doc is null)
        {
            _doc = doc;
            _states.Clear();
            for (int i = 0; i < doc.Pictures.Count; i++)
            {
                _states[i + 1] = doc.Pictures[i].Skip != GoogleSkip.None ? GooglePictureState.Skipped : GooglePictureState.Pending;
            }
            _clock.Restart();
            BuildTiles(doc.Pictures.Count);
            Render();
        }
        _checkDone = p.Done;
        _checkTotal = p.Total;
        if (p.Job is { } job)
        {
            _states[job.Index] = job.State;
            _downloaded += job.OriginalBytes;
            _last = job;
        }
        _busy = () => Loc.F("Gd.CheckingN", _checkDone, _checkTotal);
        RenderCheck();
    }

    private async Task RunApplyAsync()
    {
        if (_plan is null || _plan.ToReplace == 0 || IsWorking) return;
        _error = null;
        _applyStopped = false;
        _copyRun = _intoCopy;
        _copyTime = null;
        _log.Clear();
        _applyDone = 0;
        _applyTotal = _plan.ToReplace;
        _s.GoogleIntoCopy = _intoCopy;
        _s.Save();
        _cts = new CancellationTokenSource();
        _stage = Stage.Applying;
        _clock.Restart();
        var plan = _plan;
        _api = GoogleUi.For(_s).Api();
        var shrinker = new GoogleDocShrinker(_api);
        var token = _cts.Token;
        _busy = null;
        Render();
        try
        {
            if (_copyRun && plan.CopyId is null)
            {
                _busy = () => Loc.T("Gd.MakingCopy");
                Render();
                string name = GoogleText.CopyName(Loc.T("Gd.CopyName"), plan.Doc.Title);
                // Not cancelled half way: a copy Google may already be making would be left behind unseen.
                await Task.Run(() => shrinker.CopyAsync(plan, name, CancellationToken.None));
                _copyTime = _clock.Elapsed;
            }
            var progress = new Progress<GoogleProgress>(p =>
            {
                _applyDone = p.Done;
                _applyTotal = p.Total;
                if (p.Job is { } job)
                {
                    _log.Insert(0, (DateTime.Now, job));
                    if (_log.Count > 3) _log.RemoveAt(3);
                }
                RenderApply();
            });
            if (_cts?.IsCancellationRequested != true) _busy = null;
            Render();
            await Task.Run(() => shrinker.ApplyAsync(plan, progress, token));
            _applyStopped = plan.Cancelled;
        }
        catch (OperationCanceledException)
        {
            _applyStopped = true;
        }
        catch (GoogleException ex)
        {
            App.Log(ex);
            if (ex.Kind == GoogleError.SignInNeeded) _s.GoogleEmail = "";
            if ((_copyRun && plan.CopyId is null) || (!_copyRun && plan.Count(GooglePictureState.Replaced) == 0))
            {
                // Nothing changed anywhere (the copy could not be made, or the Doc refused the first pictures):
                // back to the decision, where the message points at the other choice.
                _cts?.Dispose();
                _cts = null;
                _busy = null;
                _stage = Stage.Summary;
                ShowError(GoogleUi.Message(ex));
                Render();
                FocusPrimary();
                return;
            }
            _applyStopped = true;
            ShowError(GoogleUi.Message(ex));
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
        _busy = null;
        _applyTime = _clock.Elapsed;
        _stage = Stage.Done;
        int replaced = plan.Count(GooglePictureState.Replaced);
        if (replaced > 0)
        {
            long now = plan.Jobs.Sum(j => j.State == GooglePictureState.Replaced ? j.NewBytes : j.OriginalBytes);
            _s.GoogleRecent = GoogleRecent.Add(_s.GoogleRecent, new GoogleRecent(plan.TargetDocId, plan.CopyName ?? plan.Doc.Title.Trim(),
                DateTime.Now, replaced, plan.BytesBefore, now, plan.CopyId is not null));
            _s.Save();
        }
        Render();
        if (replaced > 0) Finished?.Invoke(plan);
        FocusPrimary();
    }

    private void OtherAccount_Click(object sender, RoutedEventArgs e)
    {
        if (IsWorking) return;
        var session = GoogleUi.For(_s);
        session.Forget();
        session.ChooseAccountNext = true;
        _s.GoogleEmail = "";
        _s.Save();
        Render();
        ChooseButton.Focus();
    }

    private void Left_Click(object sender, RoutedEventArgs e)
    {
        if (_stage is Stage.Summary or Stage.Done)
        {
            Reset();
            ChooseButton.Focus();
        }
    }

    private void Change_Click(object sender, RoutedEventArgs e)
    {
        OptionsRequested?.Invoke();
        Refresh();
    }

    private void Dest_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _stage != Stage.Summary) return;
        _intoCopy = CopyRadio.IsChecked == true;
        RenderDecide();
        RenderButtons();
    }

    private void AllPictures_Expanded(object sender, RoutedEventArgs e)
    {
        if (_plan is null || PictureList.Items.Count > 0) return;
        foreach (var j in _plan.Jobs) PictureList.Items.Add(PictureLine(j));
    }

    private static string PictureLine(GooglePictureJob j) => j.State switch
    {
        GooglePictureState.WillShrink or GooglePictureState.Replaced => Loc.F("Gd.PicShrink", j.Index, Loc.Bytes(j.OriginalBytes), Loc.Bytes(j.NewBytes)),
        GooglePictureState.Kept => Loc.F("Gd.PicKept", j.Index, Loc.Bytes(j.OriginalBytes)),
        GooglePictureState.Skipped => Loc.F("Gd.PicSkip", j.Index, Loc.T(GoogleText.SkipKey(j.Picture.Skip))),
        _ => Loc.F("Gd.PicFail", j.Index),
    };

    private void OpenOriginal_Click(object sender, RoutedEventArgs e)
    {
        string? id = _plan?.Doc.Id ?? _docId;
        if (id is not null) GoogleUi.OpenDoc(id);
    }

    private void OpenCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_plan?.CopyId is { } id) GoogleUi.OpenDoc(id);
    }

    private void OpenTarget()
    {
        if (_plan is not null) GoogleUi.OpenDoc(_plan.TargetDocId);
        else if (_docId is not null) GoogleUi.OpenDoc(_docId);
    }

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        if (_plan?.CopyId is not { } id) return;
        try
        {
            Clipboard.SetText(GoogleDocLink.EditUrl(id));
            HintText.Text = Loc.T("Gd.LinkCopied");
            Announce(HintText);
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            App.Log(ex);
        }
    }

    private void Originals_Click(object sender, RoutedEventArgs e)
    {
        if (_plan?.OriginalsFolder is { } f) GoogleUi.OpenFolder(f);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (IsWorking)
        {
            Stop();
            return;
        }
        Close();
    }

    /// <summary>Stops the check at once, or the replacing after the pictures already in progress.</summary>
    public void Stop()
    {
        if (_cts is null || _cts.IsCancellationRequested) return;
        _cts.Cancel();
        if (_stage == Stage.Applying) _busy = () => Loc.T("Gd.Stopping");
        Render();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!IsWorking) return;
        // Closing in the middle would leave the work half done without saying so: stop first.
        e.Cancel = true;
        Stop();
        ShowError(Loc.T("Gd.CloseWhileWorking"));
    }

    protected override void OnClosed(EventArgs e)
    {
        Loc.Instance.Changed -= Render;
        _plan?.Dispose();
        base.OnClosed(e);
    }

    private void Reset()
    {
        _plan?.Dispose();
        _plan = null;
        _doc = null;
        _error = null;
        _busy = null;
        PictureList.Items.Clear();
        AllPictures.IsExpanded = false;
        _intoCopy = _s.GoogleIntoCopy;
        _stage = Stage.Link;
        LinkBox.Text = ""; // "Shrink another Doc" starts at the picker, not at the Doc just done
        Render();
    }

    // ---------- showing it ----------

    private void LinkBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (LinkError.Visibility == Visibility.Visible && (LinkBox.Text.Trim().Length == 0 || GoogleDocLink.TryGetId(LinkBox.Text) is not null))
            LinkError.Visibility = Visibility.Collapsed;
        if (IsLoaded) RenderButtons();
    }

    private void ShowLinkError()
    {
        LinkError.Text = Loc.T("Gd.LinkBad");
        LinkError.Visibility = Visibility.Visible;
        Announce(LinkError);
        LinkBox.Focus();
        LinkBox.SelectAll();
    }

    private void ShowError(string message)
    {
        _error = message;
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        Announce(ErrorText);
    }

    private static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

    private void Render()
    {
        var session = GoogleUi.For(_s);
        string email = _s.GoogleEmail;
        AccountText.Text = !session.IsAvailable ? Loc.T("Gd.NotSetUp")
            : !session.HasSignIn ? Loc.T("Gd.NotSignedIn")
            : string.IsNullOrEmpty(email) ? Loc.T("Gd.SignedIn")
            : Loc.F("Gd.SignedInAs", email);
        AccountInitial.Text = session.HasSignIn && email.Length > 0 ? email[..1].ToUpperInvariant() : "?";
        OtherAccountButton.Visibility = Show(session.IsAvailable && session.HasSignIn);
        OtherAccountButton.IsEnabled = !IsWorking;
        AccountCard.Visibility = Show(_stage is Stage.Link or Stage.Checking);

        RenderSteps();
        bool checkingDoc = _stage == Stage.Checking && _doc is not null;
        SafeBar.Visibility = Show(_stage is Stage.Link or Stage.Checking);
        SafeTag.Text = Loc.T("Gd.ReadOnlyTag");
        SafeText.Inlines.Clear();
        SafeText.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(Loc.T(checkingDoc ? "Gd.SafeCheckTitle" : "Gd.SafeTitle") + " ")));
        SafeText.Inlines.Add(new System.Windows.Documents.Run(Loc.T(checkingDoc ? "Gd.SafeCheck" : "Gd.SafeChoose")));

        ChoosePanel.Visibility = Show(_stage == Stage.Link);
        LinkBox.IsReadOnly = IsWorking;
        if (LinkError.Visibility == Visibility.Visible) LinkError.Text = Loc.T("Gd.LinkBad");
        var p = _s.GetPreset();
        PresetChip.Text = Loc.F("Gd.ChipPreset", Loc.T("Preset." + p.Id), p.LongEdge);
        OriginalsChip.Text = Loc.T(_s.GoogleKeepOriginals ? "Gd.ChipOriginalsOn" : "Gd.ChipOriginalsOff");
        RenderRecent();

        var doc = _plan?.Doc ?? _doc;
        DocCard.Visibility = Show(doc is not null && _stage is Stage.Checking or Stage.Summary);
        if (doc is not null)
        {
            DocTitle.Text = doc.Title.Trim();
            DocInfo.Text = doc.Tabs == 1 ? Loc.F("Gd.DocInfo1", doc.Pictures.Count) : Loc.F("Gd.DocInfoN", doc.Tabs, doc.Pictures.Count);
        }

        CheckPanel.Visibility = Show(_stage == Stage.Checking);
        DecidePanel.Visibility = Show(_stage == Stage.Summary);
        ReplacePanel.Visibility = Show(_stage == Stage.Applying);
        DonePanel.Visibility = Show(_stage == Stage.Done);
        ErrorPanel.Visibility = Show(_error is not null);
        if (_error is not null) ErrorText.Text = _error;

        if (_stage == Stage.Checking) RenderCheck();
        if (_stage == Stage.Summary) RenderDecide();
        if (_stage == Stage.Applying) RenderApply();
        if (_stage == Stage.Done) RenderDone();
        RenderButtons();
        RenderTitle();
    }

    // Five steps, built once; their look follows the stage.
    private readonly List<(Border Dot, TextBlock Mark, TextBlock Label, FrameworkElement Item)> _steps = new();
    private readonly List<Border> _stepLines = new();

    private void BuildSteps()
    {
        StepsGrid.ColumnDefinitions.Clear();
        for (int i = 0; i < 5; i++)
        {
            StepsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (i < 4) StepsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 12 });
            var mark = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, FontWeight = FontWeights.SemiBold };
            var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Child = mark };
            var label = new TextBlock { FontSize = 13, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var item = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, label }, Focusable = false };
            Grid.SetColumn(item, i * 2);
            StepsGrid.Children.Add(item);
            _steps.Add((dot, mark, label, item));
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
        Stage.Link => 0,
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
            var (dot, mark, label, item) = _steps[i];
            bool done = i < current || (allDone && i == current);
            bool now = i == current && !allDone;
            label.Text = Loc.T("Gd.Step" + (i + 1));
            label.FontWeight = i == current ? FontWeights.SemiBold : FontWeights.Normal;
            label.SetResourceReference(TextBlock.ForegroundProperty, i == current ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
            if (done)
            {
                mark.Text = "";
                mark.FontFamily = (FontFamily)FindResource("Icons");
                dot.SetResourceReference(Border.BackgroundProperty, "SystemFillColorSuccessBrush");
                dot.BorderThickness = new Thickness(0);
                mark.SetResourceReference(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
            }
            else
            {
                mark.Text = (i + 1).ToString(Loc.Instance.Culture);
                mark.FontFamily = FontFamily;
                if (now)
                {
                    dot.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
                    dot.BorderThickness = new Thickness(0);
                    mark.SetResourceReference(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
                }
                else
                {
                    dot.Background = Brushes.Transparent;
                    dot.BorderThickness = new Thickness(1);
                    dot.SetResourceReference(Border.BorderBrushProperty, "ControlStrongStrokeColorDefaultBrush");
                    mark.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                }
            }
            string state = done ? Loc.T("Gd.StepDoneName") : now ? Loc.T("Gd.StepNow") : "";
            AutomationProperties.SetName(item, Loc.F("Gd.StepName", i + 1, 5, label.Text) + (state.Length > 0 ? ", " + state : ""));
            if (i < _stepLines.Count)
                _stepLines[i].SetResourceReference(Border.BackgroundProperty, i < current || allDone ? "SystemFillColorSuccessBrush" : "ControlStrongStrokeColorDefaultBrush");
        }
    }

    private void RenderRecent()
    {
        RecentList.Children.Clear();
        foreach (var r in _s.GoogleRecent)
        {
            var open = new Button { Content = Loc.T("Gd.Open"), Style = (Style)FindResource("LinkButton"), VerticalAlignment = VerticalAlignment.Center };
            string id = r.DocId;
            open.Click += (_, _) => GoogleUi.OpenDoc(id);
            AutomationProperties.SetName(open, Loc.T("Gd.Open") + " " + r.Title);
            var title = new TextBlock { Text = r.Title, FontSize = 14, TextWrapping = TextWrapping.Wrap };
            var line = new TextBlock
            {
                Text = Loc.F("Gd.RecentLine", When(r.When), r.Replaced, Loc.Bytes(r.BytesBefore), Loc.Bytes(r.BytesAfter), Loc.T(r.Copy ? "Gd.RecentCopy" : "Gd.RecentDoc")),
                Style = (Style)FindResource("Secondary"),
            };
            var dock = new DockPanel();
            DockPanel.SetDock(open, Dock.Right);
            dock.Children.Add(open);
            dock.Children.Add(new StackPanel { Children = { title, line } });
            RecentList.Children.Add(new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(14, 8, 8, 8), Margin = new Thickness(0, 0, 0, 6), Child = dock });
        }
        RecentPanel.Visibility = Show(RecentList.Children.Count > 0);
    }

    private static string When(DateTime t) => t.Date == DateTime.Today
        ? Loc.F("Gd.Today", t.ToString("t", Loc.Instance.Culture))
        : t.ToString("g", Loc.Instance.Culture);

    private static string Duration(TimeSpan t)
    {
        var (key, args) = GoogleText.Duration(t);
        return Loc.F(key, args);
    }

    private void BuildTiles(int pictures)
    {
        Tiles.Children.Clear();
        int n = Math.Min(pictures, MaxTiles);
        _tiles = new Rectangle[n];
        for (int i = 0; i < n; i++)
        {
            var r = new Rectangle { Height = 12, Margin = new Thickness(1.5), RadiusX = 2, RadiusY = 2 };
            _tiles[i] = r;
            Tiles.Children.Add(r);
        }
    }

    private void RenderCheck()
    {
        bool known = _doc is not null;
        CheckTitle.Text = _busy?.Invoke() ?? Loc.T("Gd.Reading");
        CheckBar.IsIndeterminate = !known || _checkTotal == 0;
        CheckBar.Maximum = Math.Max(1, _checkTotal);
        CheckBar.Value = _checkDone;
        Tiles.Visibility = Show(known && _tiles.Length > 0);
        Legend.Visibility = Show(known);
        LastBorder.Visibility = Show(known && _last is not null);
        ResourceGrid.Visibility = Show(known);
        var left = known ? GoogleText.TimeLeft(_clock.Elapsed, _checkDone, _checkTotal) : null;
        CheckLeft.Text = left is { } l && l > TimeSpan.Zero ? Loc.F("Gd.Left", Duration(l)) : "";
        if (!known) return;

        int total = _doc!.Pictures.Count;
        for (int i = 0; i < _tiles.Length; i++)
        {
            int index = (int)((long)i * total / _tiles.Length) + 1;
            PaintTile(_tiles[i], _states.TryGetValue(index, out var st) ? st : GooglePictureState.Pending);
        }
        int will = _states.Values.Count(s => s == GooglePictureState.WillShrink);
        int kept = _states.Values.Count(s => s == GooglePictureState.Kept);
        int skip = _states.Values.Count(s => s == GooglePictureState.Skipped);
        int fail = _states.Values.Count(s => s == GooglePictureState.Failed);
        int wait = _states.Values.Count(s => s == GooglePictureState.Pending);
        Legend.Children.Clear();
        AddLegend("Gd.LegWill", will, GooglePictureState.WillShrink);
        AddLegend("Gd.LegKept", kept, GooglePictureState.Kept);
        AddLegend("Gd.LegSkip", skip, GooglePictureState.Skipped);
        if (fail > 0) AddLegend("Gd.LegFail", fail, GooglePictureState.Failed);
        AddLegend("Gd.LegWait", wait, GooglePictureState.Pending);
        if (_last is { } j)
        {
            LastLine.Text = j.State switch
            {
                GooglePictureState.WillShrink => Loc.F("Gd.LastShrink", j.Index, Loc.Bytes(j.OriginalBytes), Loc.Bytes(j.NewBytes)),
                GooglePictureState.Kept => Loc.F("Gd.LastKept", j.Index, Loc.Bytes(j.OriginalBytes)),
                GooglePictureState.Skipped => Loc.F("Gd.LastSkip", j.Index),
                _ => Loc.F("Gd.LastFail", j.Index),
            };
        }

        string folder = _s.GoogleKeepOriginals ? _s.OutputFolder : System.IO.Path.GetTempPath();
        long? free = GoogleDocShrinker.FreeSpace(folder);
        int seen = _states.Values.Count(s => s is GooglePictureState.WillShrink or GooglePictureState.Kept or GooglePictureState.Failed);
        long average = seen > 0 && _downloaded > 0 ? _downloaded / seen : 2_600_000;
        long need = (long)(total * average * (_s.GoogleKeepOriginals ? 1.1 : 0.15));
        SpaceText.Text = free is long f ? Loc.F("Gd.SpaceLine", Loc.Bytes(f), Loc.Bytes(need)) : "?";
        SpaceText.SetResourceReference(TextBlock.ForegroundProperty, free is long ff && ff < need ? "SystemFillColorCautionBrush" : "TextFillColorPrimaryBrush");
        int retries = _api?.Retries ?? 0;
        ConnText.Text = Loc.F(retries > 5 ? "Gd.ConnSlow" : "Gd.ConnGood", retries);
        RenderTitle();
    }

    private static void PaintTile(Rectangle r, GooglePictureState s)
    {
        r.StrokeThickness = 0;
        switch (s)
        {
            case GooglePictureState.WillShrink:
                r.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "SystemFillColorSuccessBrush");
                break;
            case GooglePictureState.Kept:
                r.Fill = Brushes.Transparent;
                r.StrokeThickness = 2;
                r.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "SystemFillColorSuccessBrush");
                break;
            case GooglePictureState.Skipped:
                r.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "SystemFillColorCautionBrush");
                break;
            case GooglePictureState.Failed:
                r.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "SystemFillColorCriticalBrush");
                break;
            default:
                r.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ControlStrokeColorDefaultBrush");
                break;
        }
    }

    private void AddLegend(string key, int n, GooglePictureState s)
    {
        var swatch = new Rectangle { Width = 12, Height = 12, RadiusX = 2, RadiusY = 2, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        PaintTile(swatch, s);
        var text = new TextBlock { Text = Loc.F(key, n), Style = (Style)FindResource("Secondary"), VerticalAlignment = VerticalAlignment.Center };
        Legend.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 4), Children = { swatch, text } });
    }

    private void RenderDecide()
    {
        if (_plan is not { } plan) return;
        long before = plan.BytesBefore, after = plan.BytesAfter;
        int pct = GoogleText.PercentSmaller(before, after);
        ResultTitle.Text = Loc.F("Gd.ResultTitle", plan.Doc.Title.Trim(), pct);
        CheckedAt.Text = Loc.F("Gd.Checked", _checkedAt.ToString("t", Loc.Instance.Culture));
        BeforeText.Text = Loc.Bytes(before);
        AfterText.Text = Loc.Bytes(after);
        double share = before > 0 ? Math.Clamp((double)after / before, 0.01, 1) : 1;
        AfterBar.ColumnDefinitions[0].Width = new GridLength(share, GridUnitType.Star);
        AfterBar.ColumnDefinitions[1].Width = new GridLength(1 - share + 0.0001, GridUnitType.Star);

        CountChips.Children.Clear();
        AddChip(Loc.F("Gd.ChipWill", plan.ToReplace), success: true);
        AddChip(Loc.F("Gd.ChipKept", plan.Count(GooglePictureState.Kept)), success: false);
        AddChip(Loc.F("Gd.ChipSkip", plan.Count(GooglePictureState.Skipped)), success: false);
        int failed = plan.Count(GooglePictureState.Failed);
        if (failed > 0) AddChip(Loc.F("Gd.ChipFail", failed), success: false);

        bool any = plan.ToReplace > 0;
        NothingText.Visibility = Show(!any);
        WherePanel.Visibility = Show(any);
        EstimateText.Visibility = Show(any);
        string copyName = GoogleText.CopyName(Loc.T("Gd.CopyName"), plan.Doc.Title);
        CopySub.Text = Loc.F("Gd.IntoCopySub", copyName);
        if (CopyRadio.IsChecked != _intoCopy) CopyRadio.IsChecked = _intoCopy;
        if (DocRadio.IsChecked == _intoCopy) DocRadio.IsChecked = !_intoCopy;
        Card(CopyCard, _intoCopy);
        Card(DocChoiceCard, !_intoCopy);
        WillText.Text = _intoCopy ? Loc.F("Gd.WillCopy", plan.Doc.Title.Trim()) : Loc.F("Gd.WillDoc", plan.Doc.Title.Trim());
        WillBanner.SetResourceReference(Border.BackgroundProperty, _intoCopy ? "SystemFillColorAttentionBackgroundBrush" : "SystemFillColorCautionBackgroundBrush");
        WillBanner.SetResourceReference(Border.BorderBrushProperty, _intoCopy ? "AccentFillColorDefaultBrush" : "SystemFillColorCautionBrush");
        WillIcon.SetResourceReference(TextBlock.ForegroundProperty, _intoCopy ? "AccentTextFillColorPrimaryBrush" : "SystemFillColorCautionBrush");
        EstimateText.Text = Loc.F("Gd.AboutTime", Duration(GoogleText.ReplaceEstimate(plan.ToReplace, _intoCopy))) + "  ·  "
            + Loc.T(plan.OriginalsFolder is not null ? "Gd.OriginalsShort" : "Gd.OriginalsNone");
    }

    private static void Card(Border b, bool selected)
    {
        b.BorderThickness = new Thickness(selected ? 2 : 1);
        b.SetResourceReference(Border.BorderBrushProperty, selected ? "AccentFillColorDefaultBrush" : "ControlStrongStrokeColorDefaultBrush");
    }

    private void AddChip(string text, bool success)
    {
        var t = new TextBlock { Text = text, FontSize = 13 };
        var b = new Border { Style = (Style)FindResource("Chip"), Child = t };
        if (success)
        {
            b.SetResourceReference(Border.BackgroundProperty, "SystemFillColorSuccessBackgroundBrush");
            t.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorSuccessBrush");
            t.FontWeight = FontWeights.SemiBold;
        }
        CountChips.Children.Add(b);
    }

    private void RenderApply()
    {
        if (_plan is not { } plan) return;
        bool copy = _copyRun;
        bool copyDone = plan.CopyId is not null;
        string title = plan.Doc.Title.Trim();

        // Which Doc is written is never in doubt: both are on screen.
        SecondCardColumn.Width = copy ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        CopyWriteCard.Visibility = Show(copy);
        OriginalCard.Margin = new Thickness(0, 0, copy ? 6 : 0, 0);
        OriginalName.Text = title;
        if (copy)
        {
            OriginalTag.Text = Loc.T("Gd.TagOriginal");
            OriginalTagIcon.Text = "";
            OriginalBody.Text = Loc.T("Gd.TagOriginalBody");
            OriginalCard.BorderThickness = new Thickness(1);
            OriginalCard.SetResourceReference(Border.BorderBrushProperty, "SystemFillColorSuccessBrush");
            OriginalTag.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorSuccessBrush");
            OriginalTagIcon.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorSuccessBrush");
            CopyName.Text = plan.CopyName ?? GoogleText.CopyName(Loc.T("Gd.CopyName"), plan.Doc.Title);
            CopyBody.Text = Loc.T(copyDone ? "Gd.TagCopyBody" : "Gd.TagCopyWaiting");
            OpenNowButton.Visibility = Show(copyDone);
        }
        else
        {
            OriginalTag.Text = Loc.T("Gd.TagThis");
            OriginalTagIcon.Text = "";
            OriginalBody.Text = Loc.T("Gd.TagThisBody");
            OriginalCard.BorderThickness = new Thickness(2);
            OriginalCard.SetResourceReference(Border.BorderBrushProperty, "SystemFillColorCautionBrush");
            OriginalTag.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
            OriginalTagIcon.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
        }

        bool copying = copy && !copyDone;
        ReplaceTitle.Text = _busy?.Invoke()
            ?? (copy ? Loc.F("Gd.ReplacingCopy", _applyDone, _applyTotal) : Loc.F("Gd.Replacing", _applyDone, _applyTotal));
        PhaseCopy.Visibility = Show(copy);
        Phase(PhaseCopyIcon, copyDone ? 2 : 1);
        PhaseCopyText.Text = Loc.T(copyDone ? "Gd.PhCopyDone" : "Gd.PhCopy");
        PhaseCopyDetail.Text = copyDone && _copyTime is { } ct ? Loc.F("Gd.PhCopyDetail", Duration(ct)) : "";
        PhaseCopyDetail.Visibility = Show(PhaseCopyDetail.Text.Length > 0);
        Phase(PhaseReplaceIcon, copying ? 0 : 1);
        Phase(PhaseCleanIcon, 0);
        ReplaceBar.IsIndeterminate = copying;
        ReplaceBar.Maximum = Math.Max(1, _applyTotal);
        ReplaceBar.Value = _applyDone;
        var left = copying ? (TimeSpan?)GoogleText.ReplaceEstimate(_applyTotal, false)
            : GoogleText.TimeLeft(_clock.Elapsed - (_copyTime ?? TimeSpan.Zero), _applyDone, _applyTotal);
        ReplaceLeft.Text = left is { } l && l > TimeSpan.Zero ? Loc.F("Gd.Left", Duration(l)) : "";
        int groups = Math.Max(1, (int)Math.Ceiling(_applyTotal / 10.0));
        int group = Math.Min(groups, _applyDone / 10 + 1);
        GroupText.Text = Loc.F("Gd.PhGroup", group, groups);
        LogLines.Children.Clear();
        foreach (var (when, job) in _log)
        {
            LogLines.Children.Add(new TextBlock
            {
                Text = when.ToString("HH:mm:ss", Loc.Instance.Culture) + "  " + Loc.F("Gd.LogLine", job.Index, Loc.Bytes(job.OriginalBytes), Loc.Bytes(job.NewBytes)),
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
                Margin = new Thickness(0, 1, 0, 1),
            });
        }
        ((UIElement)LogLines.Parent).Visibility = Show(_log.Count > 0);
        StopSafeText.Text = Loc.T(copy ? "Gd.StopSafeCopy" : "Gd.StopSafeDoc");
        RenderTitle();
    }

    /// <summary>0 waiting, 1 running, 2 done: an icon that differs in shape, not only in colour.</summary>
    private static void Phase(TextBlock icon, int state)
    {
        icon.Text = state switch { 2 => "", 1 => "", _ => "" };
        icon.SetResourceReference(TextBlock.ForegroundProperty, state switch
        {
            2 => "SystemFillColorSuccessBrush",
            1 => "AccentTextFillColorPrimaryBrush",
            _ => "TextFillColorSecondaryBrush",
        });
    }

    private void RenderDone()
    {
        if (_plan is not { } plan) return;
        bool copy = plan.CopyId is not null;
        int replaced = plan.Count(GooglePictureState.Replaced);
        int changed = plan.Count(GooglePictureState.Changed);
        int notDone = plan.Jobs.Count(j => j.NewPath is not null && j.State == GooglePictureState.Failed);
        int target = replaced + plan.Count(GooglePictureState.WillShrink) + changed + notDone;
        bool clean = replaced > 0 && !_applyStopped && notDone == 0 && changed == 0;
        DoneIcon.Text = clean ? "" : "";
        DoneIcon.SetResourceReference(TextBlock.ForegroundProperty, clean ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush");
        DoneTitle.Text = clean ? Loc.T(copy ? "Gd.DoneCopyTitle" : "Gd.DoneDocTitle")
            : replaced == 0 ? Loc.T("Gd.DoneNothingTitle")
            : _applyStopped ? Loc.T("Gd.DoneStoppedTitle") : Loc.T("Gd.DoneProblemTitle");
        DoneSub.Text = Loc.F("Gd.DoneSub", replaced, target, Duration(_applyTime));
        long before = plan.BytesBefore;
        long now = plan.Jobs.Sum(j => j.State == GooglePictureState.Replaced ? j.NewBytes : j.OriginalBytes);
        DoneNumbers.Visibility = Show(replaced > 0);
        DoneBefore.Text = Loc.Bytes(before);
        DoneAfter.Text = Loc.Bytes(now);
        DonePct.Text = Loc.F("Gd.Smaller", GoogleText.PercentSmaller(before, now));
        DoneChanged.Text = changed > 0 ? Loc.F("Gd.Changed", changed) : "";
        DoneChanged.Visibility = Show(changed > 0);
        DoneFailed.Text = notDone > 0 ? Loc.F("Gd.FailedN", notDone) : "";
        DoneFailed.Visibility = Show(notDone > 0);

        RowCopy.Visibility = Show(copy);
        RowCopyLine.Visibility = Show(copy);
        RowCopyName.Text = plan.CopyName ?? "";
        RowOriginalName.Text = plan.Doc.Title.Trim();
        RowOriginalSub.Text = Loc.T(copy || replaced == 0 ? "Gd.RowOriginalSub" : "Gd.RowThisSub");
        RowOriginalButton.Content = Loc.T(copy ? "Gd.OpenOriginal" : "Gd.OpenDoc");
        RowOriginalIcon.Text = copy ? "" : "";
        RowOriginalIcon.SetResourceReference(TextBlock.ForegroundProperty, copy ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush");
        int saved = plan.Jobs.Count(j => j.OriginalPath is not null);
        bool pc = plan.OriginalsFolder is { } folder && System.IO.Directory.Exists(folder) && saved > 0;
        RowPc.Visibility = Show(pc);
        RowPcLine.Visibility = Show(pc);
        RowPcTitle.Text = Loc.F("Gd.RowPc", saved);
        RowPcPath.Text = plan.OriginalsFolder ?? "";
        DoneNote.Text = Loc.T(copy ? "Gd.DoneNoteCopy" : "Gd.DoneNoteDoc");
    }

    private void RenderButtons()
    {
        bool linkEmpty = LinkBox.Text.Trim().Length == 0;
        ChooseButton.IsDefault = _stage == Stage.Link && linkEmpty;
        CheckLinkButton.IsDefault = _stage == Stage.Link && !linkEmpty;
        CheckLinkButton.IsEnabled = !linkEmpty;
        PrimaryButton.IsDefault = _stage is Stage.Summary or Stage.Done;
        HintText.Text = "";
        switch (_stage)
        {
            case Stage.Link:
                PrimaryButton.Visibility = Visibility.Collapsed;
                CloseButton.Content = Loc.T("Gd.Close");
                LeftButton.Visibility = Visibility.Collapsed;
                HintText.Text = Loc.T("Gd.HintChoose");
                break;
            case Stage.Checking:
                PrimaryButton.Visibility = Visibility.Collapsed;
                CloseButton.Content = Loc.T("Gd.Cancel");
                LeftButton.Visibility = Visibility.Collapsed;
                HintText.Text = Loc.T("Gd.HintCheck");
                break;
            case Stage.Summary:
                int n = _plan?.ToReplace ?? 0;
                PrimaryButton.Content = _intoCopy ? Loc.T("Gd.MakeCopy") : n == 1 ? Loc.T("Gd.Replace1") : Loc.F("Gd.ReplaceN", n);
                PrimaryButton.Visibility = Show(n > 0);
                PrimaryButton.IsEnabled = n > 0;
                CloseButton.Content = Loc.T("Gd.Cancel");
                LeftButton.Content = Loc.T("Gd.Back");
                LeftButton.Visibility = Visibility.Visible;
                break;
            case Stage.Applying:
                PrimaryButton.Visibility = Visibility.Collapsed;
                CloseButton.Content = Loc.T("Gd.Stop");
                LeftButton.Visibility = Visibility.Collapsed;
                HintText.Text = Loc.T("Gd.HintStop");
                break;
            case Stage.Done:
                PrimaryButton.Content = Loc.T(_plan?.CopyId is not null ? "Gd.OpenCopy" : "Gd.OpenDoc");
                PrimaryButton.Visibility = Visibility.Visible;
                PrimaryButton.IsEnabled = true;
                CloseButton.Content = Loc.T("Gd.Close");
                LeftButton.Content = Loc.T("Gd.Another");
                LeftButton.Visibility = Visibility.Visible;
                break;
        }
        CloseButton.IsEnabled = _stage != Stage.Applying || _cts is { IsCancellationRequested: false };
        AutomationProperties.SetName(PrimaryButton, PrimaryButton.Content as string ?? "");
    }

    /// <summary>The title bar and the taskbar button show the progress too, so it is visible from anywhere.</summary>
    private void RenderTitle()
    {
        string title = Loc.T("Gd.Title");
        int pct = -1;
        if (_stage == Stage.Checking && _doc is not null && _checkTotal > 0) pct = 100 * _checkDone / _checkTotal;
        if (_stage == Stage.Applying && _applyTotal > 0 && (_plan?.CopyId is not null || !_copyRun)) pct = 100 * _applyDone / _applyTotal;
        Title = pct >= 0 ? $"{pct}% · {title}" : title;
        if (Taskbar is null) return;
        Taskbar.ProgressState = !IsWorking ? TaskbarItemProgressState.None
            : pct < 0 ? TaskbarItemProgressState.Indeterminate
            : _cts?.IsCancellationRequested == true ? TaskbarItemProgressState.Paused
            : TaskbarItemProgressState.Normal;
        Taskbar.ProgressValue = pct < 0 ? 0 : pct / 100.0;
    }

    private void FocusPrimary()
    {
        if (_stage == Stage.Link)
        {
            if (LinkBox.Text.Trim().Length > 0) LinkBox.Focus();
            else ChooseButton.Focus();
            return;
        }
        if (PrimaryButton.Visibility == Visibility.Visible && PrimaryButton.IsEnabled) PrimaryButton.Focus();
        else CloseButton.Focus();
    }

    private static void Announce(UIElement e)
    {
        var peer = UIElementAutomationPeer.FromElement(e) ?? UIElementAutomationPeer.CreatePeerForElement(e);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
