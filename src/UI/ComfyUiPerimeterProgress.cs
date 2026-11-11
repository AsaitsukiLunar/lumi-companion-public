// Version: 0.3.13-preview | Generated/updated/coverage: 2026-11-11 (Asia/Shanghai)
// Source: RAW | user request and screenshot; LOCAL-WORK | Codex Desktop / Lumi / Codex.
using System.Windows;
using System.Windows.Media;
using Pen = System.Windows.Media.Pen;

namespace LumiCompanion.UI;

/// <summary>Draws the reported fraction of the panel perimeter, clockwise from the upper-left corner.</summary>
internal sealed class ComfyUiPerimeterProgress : FrameworkElement
{
    private const double StrokeWidth = 3;
    private static readonly Pen Stroke = CreateStroke();
    private double _fraction;

    internal double Fraction
    {
        get => _fraction;
        set
        {
            if (_fraction == value) return;
            _fraction = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_fraction <= 0 || RenderSize.Width <= StrokeWidth || RenderSize.Height <= StrokeWidth) return;

        double inset = StrokeWidth / 2;
        var frame = new Rect(inset, inset, RenderSize.Width - StrokeWidth, RenderSize.Height - StrokeWidth);
        if (_fraction >= 1)
        {
            drawingContext.DrawRectangle(null, Stroke, frame);
            return;
        }

        System.Windows.Point[] corners = [frame.TopLeft, frame.TopRight, frame.BottomRight, frame.BottomLeft, frame.TopLeft];
        double remaining = _fraction * 2 * (frame.Width + frame.Height);
        var path = new StreamGeometry();
        using (StreamGeometryContext figure = path.Open())
        {
            figure.BeginFigure(corners[0], isFilled: false, isClosed: false);
            for (int i = 1; i < corners.Length && remaining > 0; i++)
            {
                Vector side = corners[i] - corners[i - 1];
                double length = side.Length;
                figure.LineTo(corners[i - 1] + side * (Math.Min(remaining, length) / length),
                    isStroked: true, isSmoothJoin: false);
                remaining -= length;
            }
        }
        path.Freeze();
        drawingContext.DrawGeometry(null, Stroke, path);
    }

    private static Pen CreateStroke()
    {
        var pen = new Pen(UsageControlStyle.Brush(91, 218, 153), StrokeWidth);
        pen.Freeze();
        return pen;
    }
}
