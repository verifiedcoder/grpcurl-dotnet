using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace GrpCurl.Net.Studio.Views.Documents;

/// <summary>
///     A minimal AvaloniaEdit background renderer that draws a wavy underline beneath validation
///     problem spans (FR-063). Spans without a known position simply aren't marked.
/// </summary>
internal sealed class SquiggleRenderer : IBackgroundRenderer
{
    private readonly List<(int Offset, int Length)> _markers = [];

    public KnownLayer Layer => KnownLayer.Selection;

    public void SetMarkers(IEnumerable<(int Offset, int Length)> markers)
    {
        _markers.Clear();
        _markers.AddRange(markers);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_markers.Count == 0 || !textView.VisualLinesValid)
        {
            return;
        }

        var pen = new Pen(Brush(), 1);

        foreach (var (offset, length) in _markers)
        {
            if (length <= 0)
            {
                continue;
            }

            var segment = new TextSegment { StartOffset = offset, Length = length };

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                DrawWavyUnderline(drawingContext, pen, rect.BottomLeft, rect.Right);
            }
        }
    }

    private static IBrush Brush()
    {
        if (Application.Current is { } app
            && app.TryGetResource("Status.Server", app.ActualThemeVariant, out var resource)
            && resource is IBrush brush)
        {
            return brush;
        }

        return Brushes.Red;
    }

    private static void DrawWavyUnderline(DrawingContext context, IPen pen, Point start, double endX)
    {
        const double step = 3.0;
        const double amplitude = 2.0;

        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, isFilled: false);

            var up = false;

            for (var x = start.X + step; x < endX; x += step)
            {
                ctx.LineTo(new Point(x, start.Y - (up ? amplitude : 0)));
                up = !up;
            }
        }

        context.DrawGeometry(null, pen, geometry);
    }
}
