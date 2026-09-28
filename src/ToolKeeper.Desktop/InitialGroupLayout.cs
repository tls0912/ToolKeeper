using System.Windows;
using CabiDock.Models;

namespace CabiDock;

/// <summary>Places only groups without saved positions; existing user layouts are obstacles.</summary>
internal static class InitialGroupLayout
{
    private const double Gap = 16;

    public static GroupLayout Create(Rect workArea, bool expanded, IEnumerable<Rect> occupied)
    {
        var width = Math.Min(expanded ? 360 : 138, workArea.Width);
        var height = Math.Min(expanded ? 320 : 160, workArea.Height);
        var obstacles = occupied.Where(rect => !rect.IsEmpty && rect.Width > 0 && rect.Height > 0).ToArray();
        var firstX = Math.Min(workArea.Left + 24, workArea.Right - width);
        var firstY = Math.Min(workArea.Top + 24, workArea.Bottom - height);
        var xs = Grid(firstX, workArea.Right - width, 154)
            .Concat(obstacles.Select(rect => rect.Right + Gap)).Append(workArea.Right - width)
            .Where(x => x >= workArea.Left && x + width <= workArea.Right).Distinct().Order().ToArray();
        var ys = Grid(firstY, workArea.Bottom - height, 176)
            .Concat(obstacles.Select(rect => rect.Bottom + Gap)).Append(workArea.Bottom - height)
            .Where(y => y >= workArea.Top && y + height <= workArea.Bottom).Distinct().Order();
        foreach (var y in ys)
        foreach (var x in xs)
        {
            var candidate = new Rect(x, y, width, height);
            if (obstacles.All(rect => candidate.Left >= rect.Right + Gap || candidate.Right + Gap <= rect.Left
                || candidate.Top >= rect.Bottom + Gap || candidate.Bottom + Gap <= rect.Top))
                return new GroupLayout { X = x, Y = y };
        }
        // A screen too small for every card retains the existing bounded-placement behavior.
        return new GroupLayout { X = firstX, Y = firstY };
    }

    private static IEnumerable<double> Grid(double start, double end, double step)
    {
        for (var value = start; value <= end; value += step) yield return value;
    }
}
