using System.Windows;
using CabiDock.Desktop;
using Xunit;

namespace CabiDock.Tests;

public sealed class DesktopClipPlanTests
{
    private const string DocumentPath = @"C:\Users\Example\Desktop\notes.txt";
    private const string OtherPath = @"C:\Users\Public\Desktop\notes.txt";

    [Fact]
    public void SuppressesOnlyRepresentedPathsAndPreservesSystemAndUnmanagedIcons()
    {
        DesktopIconSnapshot[] shell =
        [
            FileIcon("notes", DocumentPath),
            FileIcon("other", @"C:\Users\Example\Desktop\other.txt"),
            SystemIcon("Recycle Bin")
        ];
        DesktopAccessibleIcon[] accessible =
        [
            Icon("Recycle Bin", 0, 0), Icon("other", 100, 0), Icon("notes", 200, 0)
        ];

        Assert.True(DesktopClipPlan.TryCreate(shell, [DocumentPath], accessible, out var plan, out var reason), reason);
        Assert.Equal(new Rect(200, 0, 80, 80), Assert.Single(plan!.ManagedScreenBounds));
        Assert.Equal(1, plan.ManagedItemCount);
        Assert.Equal(new[] { new Rect(0, 0, 80, 80), new Rect(100, 0, 80, 80) }, plan.PreservedScreenBounds);
    }

    [Fact]
    public void FilePathsUseWindowsCaseInsensitiveNormalizedIdentity()
    {
        Assert.True(DesktopClipPlan.TryCreate([FileIcon("notes", DocumentPath)],
            [@"c:\users\example\desktop\folder\..\NOTES.TXT"], [Icon("notes", -100, -200)], out var plan, out var reason), reason);
        Assert.Equal(new Rect(-100, -200, 80, 80), Assert.Single(plan!.ManagedScreenBounds));
    }

    [Fact]
    public void DuplicateNamesAreSafeWhenAllPathsAreManaged()
    {
        Assert.True(DesktopClipPlan.TryCreate([FileIcon("notes", DocumentPath), FileIcon("notes", OtherPath)],
            [DocumentPath, OtherPath], [Icon("notes", 0, 0), Icon("notes", 100, 0)], out var plan, out var reason), reason);
        Assert.Equal(2, plan!.ManagedItemCount);
    }

    [Fact]
    public void DuplicatePreservedNamesDoNotCreateSuppression()
    {
        Assert.True(DesktopClipPlan.TryCreate([FileIcon("notes", DocumentPath), FileIcon("notes", OtherPath)],
            [], [Icon("notes", 0, 0), Icon("notes", 100, 0)], out var plan, out var reason), reason);
        Assert.Empty(plan!.ManagedScreenBounds);
        Assert.Equal(2, plan.PreservedScreenBounds.Count);
    }

    [Fact]
    public void DuplicateNamesWithMixedTreatmentFailClosed()
    {
        AssertRejected([FileIcon("notes", DocumentPath), FileIcon("notes", OtherPath)],
            [DocumentPath], [Icon("notes", 0, 0), Icon("notes", 100, 0)]);
        AssertRejected([FileIcon("notes", DocumentPath), SystemIcon("notes")],
            [DocumentPath], [Icon("notes", 0, 0), Icon("notes", 100, 0)]);
        AssertRejected([FileIcon("notes", DocumentPath), SystemIcon("notes")],
            [], [Icon("notes", 0, 0), Icon("notes", 100, 0)]);
    }

    [Fact]
    public void RejectsSnapshotCountAndPerNameCountChanges()
    {
        AssertRejected([FileIcon("notes", DocumentPath)], [DocumentPath], []);
        AssertRejected([FileIcon("notes", DocumentPath), SystemIcon("Recycle Bin")], [DocumentPath],
            [Icon("notes", 0, 0), Icon("notes", 100, 0)]);
        AssertRejected([FileIcon("notes", DocumentPath)], [DocumentPath], [Icon("renamed", 0, 0)]);
        AssertRejected([FileIcon("notes", DocumentPath)], [DocumentPath], [Icon("Notes", 0, 0)]);
    }

    [Fact]
    public void RejectsRepresentedFileMissingFromShellSnapshot()
    {
        AssertRejected([SystemIcon("Recycle Bin")], [DocumentPath], [Icon("Recycle Bin", 0, 0)]);
        AssertRejected([FileIcon("notes", DocumentPath)], [DocumentPath, OtherPath], [Icon("notes", 0, 0)]);
        AssertRejected([FileIcon("notes", null)], [DocumentPath], [Icon("notes", 0, 0)]);
    }

    [Fact]
    public void UnmappedFilesystemPathStaysNative()
    {
        Assert.True(DesktopClipPlan.TryCreate([FileIcon("unknown", null)], [],
            [Icon("unknown", 0, 0)], out var plan, out var reason), reason);
        Assert.Empty(plan!.ManagedScreenBounds);
    }

    [Fact]
    public void RejectsInvalidOrRelativeRepresentedPaths()
    {
        foreach (var path in new[] { "", "notes.txt", @"C:\bad" + '\0' + ".txt" })
            AssertRejected([FileIcon("notes", DocumentPath)], [path], [Icon("notes", 0, 0)]);
    }

    [Fact]
    public void RejectsInvalidBoundsEvenForPreservedIcons()
    {
        Rect[] invalid =
        [
            Rect.Empty, new Rect(0, 0, 0, 20), new Rect(0, 0, 20, 0),
            new Rect(double.NaN, 0, 20, 20), new Rect(0, 0, double.PositiveInfinity, 20),
            new Rect(int.MaxValue, 0, 20, 20)
        ];
        foreach (var bounds in invalid)
        {
            AssertRejected([FileIcon("notes", DocumentPath)], [DocumentPath], [new("notes", bounds)]);
            AssertRejected([SystemIcon("Recycle Bin")], [], [new("Recycle Bin", bounds)]);
        }
    }

    [Fact]
    public void RoundsSuppressionBoundsOutwardWithoutLosingNegativeCoordinates()
    {
        Assert.True(DesktopClipPlan.TryCreate([FileIcon("notes", DocumentPath)], [DocumentPath],
            [new("notes", new Rect(-99.3, 1.2, 80.5, 79.6))], out var plan, out var reason), reason);
        Assert.Equal(new Rect(-100, 1, 82, 80), Assert.Single(plan!.ManagedScreenBounds));
    }

    [Fact]
    public void PreservedBoundsUseTheSameOutwardPixelRounding()
    {
        Assert.True(DesktopClipPlan.TryCreate([SystemIcon("Recycle Bin")], [],
            [new("Recycle Bin", new Rect(-99.3, 1.2, 80.5, 79.6))], out var plan, out var reason), reason);
        Assert.Equal(new Rect(-100, 1, 82, 80), Assert.Single(plan!.PreservedScreenBounds));
        Assert.Empty(plan.ManagedScreenBounds);
    }

    [Fact]
    public void RejectsOverlapWithSystemOrUnmanagedIconsIncludingPixelRounding()
    {
        foreach (var preserved in new[] { SystemIcon("other"), FileIcon("other", OtherPath) })
        {
            AssertRejected([FileIcon("notes", DocumentPath), preserved], [DocumentPath],
                [Icon("notes", 0, 0), Icon("other", 79, 0)]);
            AssertRejected([FileIcon("notes", DocumentPath), preserved], [DocumentPath],
                [new("notes", new Rect(0, 0, 79.5, 80)), new("other", new Rect(79.7, 0, 80, 80))]);
        }
    }

    [Fact]
    public void EdgeTouchingRectanglesDoNotOverlap()
    {
        Assert.True(DesktopClipPlan.TryCreate([FileIcon("notes", DocumentPath), SystemIcon("Recycle Bin")],
            [DocumentPath], [Icon("notes", 0, 0), Icon("Recycle Bin", 80, 0)], out _, out var reason), reason);
    }

    [Fact]
    public void EmptyDesktopProducesEmptyPlan()
    {
        Assert.True(DesktopClipPlan.TryCreate([], [], [], out var plan, out var reason), reason);
        Assert.Empty(plan!.ManagedScreenBounds);
        Assert.Empty(plan.PreservedScreenBounds);
    }

    private static DesktopIconSnapshot FileIcon(string name, string? path) => new(name, path ?? name, path, true, 0, 0);
    private static DesktopIconSnapshot SystemIcon(string name) => new(name, "::{namespace}", null, false, 0, 0);
    private static DesktopAccessibleIcon Icon(string name, double x, double y) => new(name, new Rect(x, y, 80, 80));

    private static void AssertRejected(IReadOnlyList<DesktopIconSnapshot> shell, IEnumerable<string> represented,
        IReadOnlyList<DesktopAccessibleIcon> accessible)
    {
        Assert.False(DesktopClipPlan.TryCreate(shell, represented, accessible, out var plan, out var reason));
        Assert.Null(plan);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }
}
