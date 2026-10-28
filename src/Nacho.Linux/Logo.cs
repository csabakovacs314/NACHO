using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Nacho.Linux;

/// <summary>The NACHO chip: a rounded yellow triangle with a blue picture inside (the same drawing as the icon and the website).</summary>
public sealed class Logo : Viewbox
{
    public Logo()
    {
        var blue = new SolidColorBrush(Color.Parse("#005FB8"));
        var yellow = new SolidColorBrush(Color.Parse("#F2B33D"));
        var canvas = new Canvas { Width = 128, Height = 128 };
        canvas.Children.Add(new Polygon
        {
            Points = { new Point(64, 16), new Point(114, 106), new Point(14, 106) },
            Fill = yellow,
            Stroke = yellow,
            StrokeThickness = 16,
            StrokeJoin = PenLineJoin.Round,
        });
        var sun = new Ellipse { Width = 14, Height = 14, Fill = blue };
        Canvas.SetLeft(sun, 70);
        Canvas.SetTop(sun, 51);
        canvas.Children.Add(sun);
        canvas.Children.Add(new Polygon
        {
            Points = { new Point(33, 96), new Point(53, 67), new Point(65, 82), new Point(75, 71), new Point(96, 96) },
            Fill = blue,
        });
        Child = canvas;
    }
}
