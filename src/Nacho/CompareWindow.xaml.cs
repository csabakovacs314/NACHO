using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nacho.Core;

namespace Nacho;

/// <summary>
/// Before/after slider: the original on the left of the divider, the Docs-ready copy (or the 256-colour version
/// NACHO turned down) on the right. The divider stays where it is on screen while the picture is moved or zoomed,
/// like Squoosh. Zoom is in screen pixels, so 100% shows one picture pixel per screen pixel at any Windows scaling.
/// </summary>
public partial class CompareWindow : Window
{
    private static readonly double[] Zooms = { 0, 1, 2, 4 }; // 0 = fit

    private readonly ShrinkResult _r;
    private ComparePictures? _pics;
    private BitmapSource? _original, _copy, _palette;
    private double _zoom = 1;          // screen pixels per picture pixel; 0 = fit
    private double _offsetX, _offsetY; // top-left of the picture in the viewport, in DIPs
    private double _divider = 0.5;     // share of the viewport width
    private bool _centred;
    private bool _syncing;

    private enum DragMode { None, Divider, Pending, Pan }
    private DragMode _drag;
    private Point _dragStart;
    private double _dragOffsetX, _dragOffsetY;

    public CompareWindow(ShrinkResult result, string displayName)
    {
        _r = result;
        InitializeComponent();
        Title = Loc.F("Compare.Title", displayName);
        Zoom100.IsChecked = true;
        Loaded += async (_, _) => await LoadAsync();
        Loc.Instance.Changed += ApplyLanguage;
        DpiChanged += (_, _) => Layout();
    }

    /// <summary>For the self-test: true once the pictures are on screen.</summary>
    public bool IsReady => _pics is not null;

    public ComparePictures? Pictures => _pics;

    public bool PaletteOffered => RightGroup.Visibility == Visibility.Visible;

    public string RightLabel => RightChipText.Text;

    public double DividerPercent => Divider.Value;

    public void ShowPaletteSide(bool palette) => (palette ? ShowPalette : ShowCopy).IsChecked = true;

    public void SetZoom(int factor) => (factor switch { 2 => Zoom200, 4 => Zoom400, 0 => ZoomFit, _ => Zoom100 }).IsChecked = true;

    private async Task LoadAsync()
    {
        try
        {
            var pics = await Task.Run(() => ComparePictures.Load(_r));
            _original = ToBitmap(pics, pics.Original);
            _copy = ToBitmap(pics, pics.Copy);
            _palette = pics.Palette is null ? null : ToBitmap(pics, pics.Palette);
            _pics = pics;
            LoadingText.Visibility = Visibility.Collapsed;
            LeftImage.Source = _original;
            RightGroup.Visibility = _palette is null ? Visibility.Collapsed : Visibility.Visible;
            ApplyRight();
            ApplyLanguage();
            Viewport.Focus();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            LoadingText.Text = Loc.F("Compare.Failed", ex.Message);
            LoadingText.TextWrapping = TextWrapping.Wrap;
        }
        Layout();
    }

    private static BitmapSource ToBitmap(ComparePictures p, byte[] bgra)
    {
        var bmp = BitmapSource.Create(p.Width, p.Height, 96, 96, PixelFormats.Bgra32, null, bgra, p.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    private void ApplyLanguage()
    {
        if (_pics is null) return;
        Title = Loc.F("Compare.Title", System.IO.Path.GetFileName(_r.SourcePath));
        LeftChipText.Text = Loc.T("Compare.Original");
        string right = ShowPalette.IsChecked == true && _palette is not null
            ? Loc.T("Compare.PaletteLabel")
            : Loc.F("Compare.CopyLabel", _r.OutputFormat, Loc.Bytes(_r.OutputBytes));
        RightChipText.Text = right;
        RightName.Text = right;
        SizeNote.Text = _pics.OriginalResized
            ? Loc.F("Compare.Resized", ResultItem.Dim(_pics.Width, _pics.Height))
            : Loc.F("Compare.SameSize", ResultItem.Dim(_pics.Width, _pics.Height));
        if (_pics.PalettePsnr is { } psnr)
        {
            PaletteNote.Text = Loc.F("Compare.PaletteNote", psnr.ToString("0.0", Loc.Instance.Culture),
                ImageShrinker.MinPalettePsnr.ToString("0", Loc.Instance.Culture));
            PaletteNote.Visibility = Visibility.Visible;
        }
        Layout();
    }

    private void ApplyRight()
    {
        RightImage.Source = ShowPalette.IsChecked == true && _palette is not null ? _palette : _copy;
        ApplyLanguage();
    }

    // ---------- layout ----------

    private double DpiScale => VisualTreeHelper.GetDpi(Viewport).DpiScaleX;

    /// <summary>DIPs per picture pixel for the current zoom.</summary>
    private double Scale()
    {
        if (_pics is null) return 1;
        double vw = Math.Max(1, Stage.ActualWidth), vh = Math.Max(1, Stage.ActualHeight);
        if (_zoom > 0) return _zoom / DpiScale;
        double fit = Math.Min(vw / _pics.Width, vh / _pics.Height);
        return Math.Min(fit, 1 / DpiScale);
    }

    private void Layout()
    {
        double vw = Math.Max(1, Stage.ActualWidth), vh = Math.Max(1, Stage.ActualHeight);
        Canvas.SetLeft(LoadingText, 16);
        Canvas.SetTop(LoadingText, 16);
        LoadingText.MaxWidth = Math.Max(100, vw - 32);
        if (_pics is null)
        {
            foreach (UIElement e in new UIElement[] { LeftImage, RightImage, CheckerBack, LineDark, LineLight, Handle, LeftChip, RightChip })
            {
                e.Visibility = Visibility.Collapsed;
            }
            return;
        }
        foreach (UIElement e in new UIElement[] { LeftImage, RightImage, CheckerBack, LineDark, LineLight, Handle, LeftChip, RightChip })
        {
            e.Visibility = Visibility.Visible;
        }

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
        double dpi = DpiScale;
        _offsetX = Math.Round(_offsetX * dpi) / dpi;
        _offsetY = Math.Round(_offsetY * dpi) / dpi;

        var mode = s * dpi >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality;
        foreach (var img in new FrameworkElement[] { LeftImage, RightImage, CheckerBack })
        {
            img.Width = w;
            img.Height = h;
            Canvas.SetLeft(img, _offsetX);
            Canvas.SetTop(img, _offsetY);
            RenderOptions.SetBitmapScalingMode(img, mode);
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

        LeftChip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        RightChip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
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

    private Point Centre => new(Stage.ActualWidth / 2, Stage.ActualHeight / 2);

    // ---------- events ----------

    private void Zoom_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (sender is FrameworkElement { Tag: string tag }) ZoomTo(double.Parse(tag, System.Globalization.CultureInfo.InvariantCulture), Centre);
    }

    private void Right_Checked(object sender, RoutedEventArgs e)
    {
        if (_pics is not null) ApplyRight();
    }

    private void Divider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing) return;
        _divider = e.NewValue / 100;
        Layout();
    }

    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) => Layout();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Viewport.Focus();
        var p = e.GetPosition(Stage);
        double divX = _divider * Stage.ActualWidth;
        _drag = Math.Abs(p.X - divX) <= 20 ? DragMode.Divider : DragMode.Pending;
        _dragStart = p;
        _dragOffsetX = _offsetX;
        _dragOffsetY = _offsetY;
        Viewport.CaptureMouse();
        e.Handled = true;
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Stage);
        if (_drag == DragMode.None)
        {
            bool nearDivider = Math.Abs(p.X - _divider * Stage.ActualWidth) <= 20;
            Viewport.Cursor = nearDivider || !CanPan() ? Cursors.SizeWE : Cursors.SizeAll;
            return;
        }
        if (_drag == DragMode.Divider)
        {
            SetDivider(p.X / Math.Max(1, Stage.ActualWidth));
            return;
        }
        if (_drag == DragMode.Pending
            && Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }
        _drag = DragMode.Pan;
        _offsetX = _dragOffsetX + p.X - _dragStart.X;
        _offsetY = _dragOffsetY + p.Y - _dragStart.Y;
        Layout();
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // A click without a drag puts the divider there: a one-pointer way that needs no dragging.
        if (_drag == DragMode.Pending) SetDivider(e.GetPosition(Stage).X / Math.Max(1, Stage.ActualWidth));
        _drag = DragMode.None;
        Viewport.ReleaseMouseCapture();
    }

    private void Viewport_LostCapture(object sender, MouseEventArgs e) => _drag = DragMode.None;

    private bool CanPan()
    {
        if (_pics is null) return false;
        double s = Scale();
        return _pics.Width * s > Stage.ActualWidth || _pics.Height * s > Stage.ActualHeight;
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control))
        {
            StepZoom(e.Delta > 0 ? 1 : -1, e.GetPosition(Stage));
        }
        else if (mods.HasFlag(ModifierKeys.Shift))
        {
            _offsetX += e.Delta / 2.0;
            Layout();
        }
        else
        {
            _offsetY += e.Delta / 2.0;
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

    private void Viewport_KeyDown(object sender, KeyEventArgs e)
    {
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is TextBox) return;
        switch (e.Key)
        {
            case Key.D0 or Key.NumPad0: ZoomFit.IsChecked = true; break;
            case Key.D1 or Key.NumPad1: Zoom100.IsChecked = true; break;
            case Key.D2 or Key.NumPad2: Zoom200.IsChecked = true; break;
            case Key.D4 or Key.NumPad4: Zoom400.IsChecked = true; break;
            case Key.OemPlus or Key.Add: StepZoom(1, Centre); break;
            case Key.OemMinus or Key.Subtract: StepZoom(-1, Centre); break;
            case Key.Escape: Close(); break;
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
