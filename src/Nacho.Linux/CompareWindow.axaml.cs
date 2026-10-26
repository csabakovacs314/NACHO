using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Nacho.Core;

namespace Nacho.Linux;

/// <summary>
/// Before/after slider: the original on the left of the divider, the Docs-ready copy (or the 256-colour version
/// NACHO turned down) on the right. The divider stays where it is on screen while the picture is moved or zoomed,
/// like Squoosh. Zoom is in screen pixels, so 100% shows one picture pixel per screen pixel at any scaling.
/// </summary>
public partial class CompareWindow : Window
{
    private static readonly double[] Zooms = { 0, 1, 2, 4 }; // 0 = fit

    private readonly ShrinkResult _r;
    private ComparePictures? _pics;
    private Bitmap? _original, _copy, _palette;
    private double _zoom = 1;          // screen pixels per picture pixel; 0 = fit
    private double _offsetX, _offsetY; // top-left of the picture in the viewport, in DIPs
    private double _divider = 0.5;     // share of the viewport width
    private bool _centred;
    private bool _syncing;

    private enum DragMode { None, Divider, Pending, Pan }
    private DragMode _drag;
    private Point _dragStart;
    private double _dragOffsetX, _dragOffsetY;

    public CompareWindow() : this(new ShrinkResult(), "") { }

    public CompareWindow(ShrinkResult result, string displayName)
    {
        _r = result;
        InitializeComponent();
        Icon = App.Icon();
        Title = Loc.F("Compare.Title", displayName);
        CheckerBack.Fill = Checker();
        Zoom100.IsChecked = true;
        Divider.ValueChanged += Divider_ValueChanged;
        Viewport.PointerPressed += Viewport_PointerPressed;
        Viewport.PointerMoved += Viewport_PointerMoved;
        Viewport.PointerReleased += Viewport_PointerReleased;
        Viewport.PointerCaptureLost += (_, _) => _drag = DragMode.None;
        Viewport.PointerWheelChanged += Viewport_PointerWheelChanged;
        Viewport.KeyDown += Viewport_KeyDown;
        Stage.SizeChanged += (_, _) => Layout();
        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        ScalingChanged += (_, _) => Layout();
        Opened += async (_, _) => await LoadAsync(0);
        Loc.Instance.Changed += ApplyLanguage;
    }

    /// <summary>For the self-test: true once the pictures are on screen.</summary>
    public bool IsReady => _pics is not null;

    public ComparePictures? Pictures => _pics;

    public bool PaletteOffered => RightGroup.IsVisible;

    public string RightLabel => RightChipText.Text ?? "";

    public double DividerPercent => Divider.Value;

    public void ShowPaletteSide(bool palette) => (palette ? ShowPalette : ShowCopy).IsChecked = true;

    public int PageIndex => _pics?.Page ?? 0;

    public bool PagesOffered => PageGroup.IsVisible;

    public string PageLabel => PageText.Text ?? "";

    public Task GoToPageAsync(int page) => LoadAsync(page);

    public void SetZoom(int factor) => (factor switch { 2 => Zoom200, 4 => Zoom400, 0 => ZoomFit, _ => Zoom100 }).IsChecked = true;

    private async Task LoadAsync(int page)
    {
        try
        {
            LoadingText.IsVisible = true;
            var pics = await Task.Run(() => _r.IsDocument ? ComparePictures.LoadPdfPage(_r, page) : ComparePictures.Load(_r));
            bool first = _pics is null;
            bool newSize = first || _pics!.Width != pics.Width || _pics.Height != pics.Height;
            _original = ToBitmap(pics, pics.Original);
            _copy = ToBitmap(pics, pics.Copy);
            _palette = pics.Palette is null ? null : ToBitmap(pics, pics.Palette);
            _pics = pics;
            if (newSize) _centred = false;
            LoadingText.IsVisible = false;
            PageGroup.IsVisible = pics.IsPdf && pics.PageCount > 1;
            PrevPage.IsEnabled = pics.Page > 0;
            NextPage.IsEnabled = pics.Page < pics.PageCount - 1;
            LeftImage.Source = _original;
            RightGroup.IsVisible = _palette is not null;
            ApplyRight();
            // Only on opening: a keyboard user paging through stays on the page buttons.
            if (first) Viewport.Focus();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            LoadingText.Text = Loc.F("Compare.Failed", ex.Message);
        }
        Layout();
    }

    private static Bitmap ToBitmap(ComparePictures p, byte[] bgra)
    {
        var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, handle.AddrOfPinnedObject(),
                new PixelSize(p.Width, p.Height), new Vector(96, 96), p.Width * 4);
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>Transparent parts show on a checkerboard, like most image editors.</summary>
    private static IBrush Checker()
    {
        var px = new byte[16 * 16 * 4];
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                byte v = (x < 8) == (y < 8) ? (byte)0xFF : (byte)0xD9;
                int i = (y * 16 + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = v;
                px[i + 3] = 0xFF;
            }
        }
        var handle = GCHandle.Alloc(px, GCHandleType.Pinned);
        try
        {
            var bmp = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, handle.AddrOfPinnedObject(), new PixelSize(16, 16), new Vector(96, 96), 64);
            return new ImageBrush(bmp)
            {
                TileMode = TileMode.Tile,
                DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute),
            };
        }
        finally
        {
            handle.Free();
        }
    }

    private void ApplyLanguage()
    {
        if (_pics is null) return;
        Title = Loc.F("Compare.Title", Path.GetFileName(_r.SourcePath));
        LeftChipText.Text = Loc.T("Compare.Original");
        string right = ShowPalette.IsChecked == true && _palette is not null
            ? Loc.T("Compare.PaletteLabel")
            : Loc.F("Compare.CopyLabel", _r.OutputFormat, Loc.Bytes(_r.OutputBytes));
        RightChipText.Text = right;
        RightName.Text = right;
        PageText.Text = Loc.F("Compare.Page", _pics.Page + 1, _pics.PageCount);
        SizeNote.Text = _pics.IsPdf
            ? Loc.F("Compare.PdfNote", ResultItem.Dim(_pics.Width, _pics.Height))
            : _pics.OriginalResized
            ? Loc.F("Compare.Resized", ResultItem.Dim(_pics.Width, _pics.Height))
            : Loc.F("Compare.SameSize", ResultItem.Dim(_pics.Width, _pics.Height));
        if (_pics.PalettePsnr is { } psnr)
        {
            PaletteNote.Text = Loc.F("Compare.PaletteNote", psnr.ToString("0.0", Loc.Instance.Culture),
                ImageShrinker.MinPalettePsnr.ToString("0", Loc.Instance.Culture));
            PaletteNote.IsVisible = true;
        }
        Layout();
    }

    private void ApplyRight()
    {
        RightImage.Source = ShowPalette.IsChecked == true && _palette is not null ? _palette : _copy;
        ApplyLanguage();
    }

    // ---------- layout ----------

    private double Scaling => RenderScaling > 0 ? RenderScaling : 1;

    private double ViewW => Math.Max(1, Stage.Bounds.Width);

    private double ViewH => Math.Max(1, Stage.Bounds.Height);

    /// <summary>DIPs per picture pixel for the current zoom.</summary>
    private double Scale()
    {
        if (_pics is null) return 1;
        if (_zoom > 0) return _zoom / Scaling;
        double fit = Math.Min(ViewW / _pics.Width, ViewH / _pics.Height);
        return Math.Min(fit, 1 / Scaling);
    }

    private void Layout()
    {
        double vw = ViewW, vh = ViewH;
        Canvas.SetLeft(LoadingText, 16);
        Canvas.SetTop(LoadingText, 16);
        LoadingText.MaxWidth = Math.Max(100, vw - 32);
        var parts = new Control[] { LeftImage, RightImage, CheckerBack, LineDark, LineLight, Handle, LeftChip, RightChip };
        foreach (var c in parts) c.IsVisible = _pics is not null;
        if (_pics is null) return;

        double s = Scale();
        double w = _pics.Width * s, h = _pics.Height * s;
        if (!_centred)
        {
            _offsetX = (vw - w) / 2;
            _offsetY = (vh - h) / 2;
            _centred = true;
        }
        _offsetX = w <= vw ? (vw - w) / 2 : Math.Clamp(_offsetX, vw - w, 0);
        _offsetY = h <= vh ? (vh - h) / 2 : Math.Clamp(_offsetY, vh - h, 0);
        // Whole screen pixels, so 100% is really one picture pixel per screen pixel.
        double dpi = Scaling;
        _offsetX = Math.Round(_offsetX * dpi) / dpi;
        _offsetY = Math.Round(_offsetY * dpi) / dpi;

        var mode = s * dpi >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality;
        foreach (var c in new Control[] { LeftImage, RightImage, CheckerBack })
        {
            c.Width = w;
            c.Height = h;
            Canvas.SetLeft(c, _offsetX);
            Canvas.SetTop(c, _offsetY);
            RenderOptions.SetBitmapInterpolationMode(c, mode);
        }

        double divX = Math.Round(_divider * vw * dpi) / dpi;
        LeftImage.Clip = new RectangleGeometry(new Rect(0, 0, Math.Clamp(divX - _offsetX, 0, w), h));

        LineDark.Height = vh;
        LineLight.Height = vh;
        Canvas.SetLeft(LineDark, divX - 2);
        Canvas.SetLeft(LineLight, divX - 1);
        Canvas.SetTop(LineDark, 0);
        Canvas.SetTop(LineLight, 0);
        Canvas.SetLeft(Handle, divX - Handle.Width / 2);
        Canvas.SetTop(Handle, vh / 2 - Handle.Height / 2);

        LeftChip.Measure(Size.Infinity);
        RightChip.Measure(Size.Infinity);
        Canvas.SetLeft(LeftChip, 8);
        Canvas.SetTop(LeftChip, 8);
        // Anchored to the right edge, so a longer label never runs off the picture.
        Canvas.SetRight(RightChip, 8);
        Canvas.SetTop(RightChip, 8);
        LeftChip.Opacity = divX > LeftChip.DesiredSize.Width + 16 ? 1 : 0;
        RightChip.Opacity = vw - divX > RightChip.DesiredSize.Width + 16 ? 1 : 0;
    }

    private void SetDivider(double share)
    {
        _divider = Math.Clamp(share, 0, 1);
        _syncing = true;
        Divider.Value = Math.Round(_divider * 100, 1);
        _syncing = false;
        Layout();
    }

    /// <summary>Changes zoom keeping the picture point under the anchor (in viewport DIPs) where it is.</summary>
    private void ZoomTo(double zoom, Point anchor)
    {
        if (_pics is null)
        {
            _zoom = zoom;
            return;
        }
        double before = Scale();
        double ux = (anchor.X - _offsetX) / before, uy = (anchor.Y - _offsetY) / before;
        _zoom = zoom;
        double after = Scale();
        _offsetX = anchor.X - ux * after;
        _offsetY = anchor.Y - uy * after;
        Layout();
    }

    private Point Centre => new(ViewW / 2, ViewH / 2);

    // ---------- events ----------

    private void Zoom_Changed(object? sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not RadioButton { IsChecked: true, Tag: string tag }) return;
        ZoomTo(double.Parse(tag, System.Globalization.CultureInfo.InvariantCulture), Centre);
    }

    private void Right_Changed(object? sender, RoutedEventArgs e)
    {
        if (_pics is not null && sender is RadioButton { IsChecked: true }) ApplyRight();
    }

    private void Divider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncing) return;
        _divider = e.NewValue / 100;
        Layout();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private async void PrevPage_Click(object? sender, RoutedEventArgs e)
    {
        if (_pics is { IsPdf: true, Page: > 0 }) await LoadAsync(_pics.Page - 1);
    }

    private async void NextPage_Click(object? sender, RoutedEventArgs e)
    {
        if (_pics is { IsPdf: true } p && p.Page < p.PageCount - 1) await LoadAsync(p.Page + 1);
    }

    private void Viewport_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Stage).Properties.IsLeftButtonPressed) return;
        Viewport.Focus();
        var p = e.GetPosition(Stage);
        _drag = Math.Abs(p.X - _divider * ViewW) <= 20 ? DragMode.Divider : DragMode.Pending;
        _dragStart = p;
        _dragOffsetX = _offsetX;
        _dragOffsetY = _offsetY;
        e.Pointer.Capture(Viewport);
        e.Handled = true;
    }

    private void Viewport_PointerMoved(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(Stage);
        if (_drag == DragMode.None)
        {
            bool nearDivider = Math.Abs(p.X - _divider * ViewW) <= 20;
            Viewport.Cursor = new Cursor(nearDivider || !CanPan() ? StandardCursorType.SizeWestEast : StandardCursorType.SizeAll);
            return;
        }
        if (_drag == DragMode.Divider)
        {
            SetDivider(p.X / ViewW);
            return;
        }
        if (_drag == DragMode.Pending && Math.Abs(p.X - _dragStart.X) < 4 && Math.Abs(p.Y - _dragStart.Y) < 4) return;
        _drag = DragMode.Pan;
        _offsetX = _dragOffsetX + p.X - _dragStart.X;
        _offsetY = _dragOffsetY + p.Y - _dragStart.Y;
        Layout();
    }

    private void Viewport_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // A click without a drag puts the divider there: a one-pointer way that needs no dragging.
        if (_drag == DragMode.Pending) SetDivider(e.GetPosition(Stage).X / ViewW);
        _drag = DragMode.None;
        e.Pointer.Capture(null);
    }

    private bool CanPan()
    {
        if (_pics is null) return false;
        double s = Scale();
        return _pics.Width * s > ViewW || _pics.Height * s > ViewH;
    }

    private void Viewport_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            StepZoom(e.Delta.Y > 0 ? 1 : -1, e.GetPosition(Stage));
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _offsetX += e.Delta.Y * 60;
            Layout();
        }
        else
        {
            _offsetX += e.Delta.X * 60;
            _offsetY += e.Delta.Y * 60;
            Layout();
        }
        e.Handled = true;
    }

    private void StepZoom(int direction, Point anchor)
    {
        int i = Array.IndexOf(Zooms, _zoom);
        if (i < 0) i = 1;
        int next = Math.Clamp(i + direction, 0, Zooms.Length - 1);
        if (next == i) return;
        var button = next switch { 0 => ZoomFit, 2 => Zoom200, 3 => Zoom400, _ => Zoom100 };
        ZoomTo(Zooms[next], anchor);
        _syncing = true;
        button.IsChecked = true;
        _syncing = false;
    }

    private void Viewport_KeyDown(object? sender, KeyEventArgs e)
    {
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        double step = shift ? 0.10 : 0.02;
        switch (e.Key)
        {
            case Key.Left when !ctrl: SetDivider(_divider - step); break;
            case Key.Right when !ctrl: SetDivider(_divider + step); break;
            case Key.Left: _offsetX += 60; Layout(); break;
            case Key.Right: _offsetX -= 60; Layout(); break;
            case Key.Up: _offsetY += 60; Layout(); break;
            case Key.Down: _offsetY -= 60; Layout(); break;
            case Key.Home: SetDivider(0); break;
            case Key.End: SetDivider(1); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return;
        switch (e.Key)
        {
            case Key.D0 or Key.NumPad0: ZoomFit.IsChecked = true; break;
            case Key.D1 or Key.NumPad1: Zoom100.IsChecked = true; break;
            case Key.D2 or Key.NumPad2: Zoom200.IsChecked = true; break;
            case Key.D4 or Key.NumPad4: Zoom400.IsChecked = true; break;
            case Key.OemPlus or Key.Add: StepZoom(1, Centre); break;
            case Key.OemMinus or Key.Subtract: StepZoom(-1, Centre); break;
            case Key.Escape: Close(); break;
            case Key.PageUp: PrevPage_Click(this, new RoutedEventArgs()); break;
            case Key.PageDown: NextPage_Click(this, new RoutedEventArgs()); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        Loc.Instance.Changed -= ApplyLanguage;
        base.OnClosed(e);
    }
}
