using System.Windows;
using System.Windows.Media;

namespace Nacho;

/// <summary>The corner radius of one segment in a segmented control (first and last segments are rounded).</summary>
public static class Seg
{
    public static readonly DependencyProperty CornerProperty = DependencyProperty.RegisterAttached(
        "Corner", typeof(CornerRadius), typeof(Seg), new FrameworkPropertyMetadata(new CornerRadius(0)));

    public static CornerRadius GetCorner(DependencyObject o) => (CornerRadius)o.GetValue(CornerProperty);

    public static void SetCorner(DependencyObject o, CornerRadius value) => o.SetValue(CornerProperty, value);
}

/// <summary>
/// With High Contrast on, NACHO uses the classic theme, which has none of the Fluent brushes the windows ask for.
/// This maps each of them to the matching High Contrast system colour, so every text, border and state stays visible.
/// </summary>
public static class HighContrastBrushes
{
    public static ResourceDictionary Create()
    {
        var d = new ResourceDictionary();
        void Map(Brush b, params string[] keys)
        {
            foreach (string k in keys) d[k] = b;
        }
        Map(SystemColors.WindowTextBrush, "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush",
            "CardStrokeColorDefaultBrush", "ControlStrokeColorDefaultBrush", "ControlStrongStrokeColorDefaultBrush",
            "DividerStrokeColorDefaultBrush", "SystemFillColorSuccessBrush", "SystemFillColorCautionBrush",
            "SystemFillColorCriticalBrush", "SystemFillColorAttentionBrush");
        Map(SystemColors.WindowBrush, "CardBackgroundFillColorDefaultBrush", "LayerFillColorDefaultBrush",
            "SubtleFillColorSecondaryBrush", "SubtleFillColorTertiaryBrush", "ControlFillColorDefaultBrush",
            "ControlFillColorSecondaryBrush", "SystemFillColorSuccessBackgroundBrush", "SystemFillColorCautionBackgroundBrush",
            "SystemFillColorCriticalBackgroundBrush", "SystemFillColorAttentionBackgroundBrush");
        Map(SystemColors.HighlightBrush, "AccentFillColorDefaultBrush");
        Map(SystemColors.HotTrackBrush, "AccentTextFillColorPrimaryBrush");
        Map(SystemColors.HighlightTextBrush, "TextOnAccentFillColorPrimaryBrush");
        return d;
    }
}
