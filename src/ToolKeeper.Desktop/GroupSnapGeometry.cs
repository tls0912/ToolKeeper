using System.Windows;

namespace CabiDock;

/// <summary>Move-only magnetic alignment in logical (DIP) coordinates.</summary>
internal static class GroupSnapGeometry
{
    internal const double GroupGap = 8;

    internal static Rect SnapMove(Rect proposed, Rect workArea, IReadOnlyList<Rect> otherGroups, double threshold = 12)
    {
        ArgumentNullException.ThrowIfNull(otherGroups);
        if (!double.IsFinite(threshold) || threshold < 0) throw new ArgumentOutOfRangeException(nameof(threshold));
        if (!IsFinite(proposed) || !IsFinite(workArea)) return proposed;

        // A caller normally constrains the size first. Preserve an oversized group's size
        // and anchor it to the top/left, matching GroupWindow's existing clamp behavior.
        var maximumX = Math.Max(workArea.Left, workArea.Right - proposed.Width);
        var maximumY = Math.Max(workArea.Top, workArea.Bottom - proposed.Height);
        var bounded = new Rect(Math.Clamp(proposed.Left, workArea.Left, maximumX),
            Math.Clamp(proposed.Top, workArea.Top, maximumY), proposed.Width, proposed.Height);
        var targets = otherGroups.Where(rect => IsFinite(rect) && rect.Width > 0 && rect.Height > 0).ToArray();
        var xs = new List<Candidate> { new(bounded.Left, false) };
        var ys = new List<Candidate> { new(bounded.Top, false) };
        Add(xs, workArea.Left, bounded.Left, workArea.Left, maximumX, threshold);
        Add(xs, workArea.Right - bounded.Width, bounded.Left, workArea.Left, maximumX, threshold);
        Add(ys, workArea.Top, bounded.Top, workArea.Top, maximumY, threshold);
        Add(ys, workArea.Bottom - bounded.Height, bounded.Top, workArea.Top, maximumY, threshold);

        foreach (var target in targets)
        {
            // Matching an edge on the other side of the screen is not magnetic proximity.
            if (IntervalsNear(bounded.Top, bounded.Bottom, target.Top, target.Bottom, threshold + GroupGap))
            {
                Add(xs, target.Left - GroupGap - bounded.Width, bounded.Left, workArea.Left, maximumX, threshold);
                Add(xs, target.Right + GroupGap, bounded.Left, workArea.Left, maximumX, threshold);
                Add(xs, target.Left, bounded.Left, workArea.Left, maximumX, threshold);
                Add(xs, target.Right - bounded.Width, bounded.Left, workArea.Left, maximumX, threshold);
            }
            if (IntervalsNear(bounded.Left, bounded.Right, target.Left, target.Right, threshold + GroupGap))
            {
                Add(ys, target.Top - GroupGap - bounded.Height, bounded.Top, workArea.Top, maximumY, threshold);
                Add(ys, target.Bottom + GroupGap, bounded.Top, workArea.Top, maximumY, threshold);
                Add(ys, target.Top, bounded.Top, workArea.Top, maximumY, threshold);
                Add(ys, target.Bottom - bounded.Height, bounded.Top, workArea.Top, maximumY, threshold);
            }
        }

        // Independently nearest X/Y edges normally win. Evaluate their combinations so
        // choosing two different targets cannot pull a group inside another group.
        var best = bounded;
        var bestAxes = -1;
        var bestDistance = double.PositiveInfinity;
        foreach (var x in xs.OrderBy(candidate => candidate.Position))
        foreach (var y in ys.OrderBy(candidate => candidate.Position))
        {
            var candidate = new Rect(x.Position, y.Position, bounded.Width, bounded.Height);
            if (targets.Any(target => Overlaps(candidate, target))) continue;
            var axes = (x.Snapped ? 1 : 0) + (y.Snapped ? 1 : 0);
            var distance = Math.Pow(x.Position - bounded.Left, 2) + Math.Pow(y.Position - bounded.Top, 2);
            if (axes < bestAxes || axes == bestAxes && distance >= bestDistance) continue;
            best = candidate;
            bestAxes = axes;
            bestDistance = distance;
        }
        // Existing overlap is not an instruction to rearrange windows or teleport away.
        return best;
    }

    private static void Add(List<Candidate> candidates, double position, double current,
        double minimum, double maximum, double threshold)
    {
        if (!double.IsFinite(position) || position < minimum || position > maximum || Math.Abs(position - current) > threshold)
            return;
        if (!candidates.Any(candidate => candidate.Snapped && candidate.Position == position))
            candidates.Add(new(position, true));
    }

    private static bool IntervalsNear(double firstStart, double firstEnd, double secondStart, double secondEnd, double distance) =>
        firstStart <= secondEnd + distance && secondStart <= firstEnd + distance;

    private static bool Overlaps(Rect first, Rect second) => first.Left < second.Right && first.Right > second.Left
        && first.Top < second.Bottom && first.Bottom > second.Top;

    private static bool IsFinite(Rect rect) => !rect.IsEmpty && double.IsFinite(rect.Left) && double.IsFinite(rect.Top)
        && double.IsFinite(rect.Width) && double.IsFinite(rect.Height) && double.IsFinite(rect.Right) && double.IsFinite(rect.Bottom);

    private readonly record struct Candidate(double Position, bool Snapped);
}
