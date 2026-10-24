using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Nacho.Linux;

/// <summary>A 16 x 16 line icon drawn in the inherited text colour, so it follows light, dark and accent buttons.</summary>
public sealed class StrokeIcon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<StrokeIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<StrokeIcon, double>(nameof(Thickness), 1.5);

    /// <summary>The line colour; inherited from the button or text around the icon unless set.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<StrokeIcon>();

    static StrokeIcon()
    {
        AffectsRender<StrokeIcon>(DataProperty, ThicknessProperty, ForegroundProperty);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public StrokeIcon()
    {
        Width = 16;
        Height = 16;
        IsHitTestVisible = false;
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Data is null || Bounds.Width <= 0) return;
        IBrush brush = Foreground ?? Brushes.Gray;
        double scale = Math.Min(Bounds.Width, Bounds.Height) / 16.0;
        var pen = new Pen(brush, Thickness / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawGeometry(null, pen, Data);
        }
    }
}
