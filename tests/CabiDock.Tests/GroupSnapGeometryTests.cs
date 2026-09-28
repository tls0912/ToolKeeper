using System.Windows;
using Xunit;

namespace CabiDock.Tests;

public sealed class GroupSnapGeometryTests
{
    private static readonly Rect WorkArea = new(0, 0, 1000, 900);

    [Theory]
    [InlineData(7, 300, 0, 300)]
    [InlineData(856, 300, 862, 300)]
    [InlineData(400, 9, 400, 0)]
    [InlineData(400, 731, 400, 740)]
    [InlineData(7, 9, 0, 0)]
    public void SnapsToEachWorkAreaEdgeWithoutChangingSize(double x, double y, double expectedX, double expectedY)
    {
        var result = GroupSnapGeometry.SnapMove(new Rect(x, y, 138, 160), WorkArea, []);
        Assert.Equal(new Rect(expectedX, expectedY, 138, 160), result);
    }

    [Theory]
    [InlineData(400, 200, 138, 160, 260, 220, 138, 160, 254, 220)]
    [InlineData(200, 200, 138, 160, 345, 220, 138, 160, 346, 220)]
    [InlineData(400, 400, 138, 160, 420, 238, 138, 160, 420, 232)]
    [InlineData(400, 200, 138, 160, 420, 374, 138, 160, 420, 368)]
    [InlineData(400, 200, 138, 160, 405, 371, 138, 160, 400, 368)]
    [InlineData(400, 200, 138, 160, 431, 371, 100, 160, 438, 368)]
    [InlineData(200, 200, 138, 160, 350, 207, 138, 100, 346, 200)]
    [InlineData(200, 200, 138, 160, 350, 253, 138, 100, 346, 260)]
    public void NearbyGroupsSupportAdjacentEdgesAndSameSideAlignment(double targetX, double targetY, double targetWidth,
        double targetHeight, double x, double y, double width, double height, double expectedX, double expectedY)
    {
        var target = new Rect(targetX, targetY, targetWidth, targetHeight);
        var result = GroupSnapGeometry.SnapMove(new Rect(x, y, width, height), WorkArea, [target]);
        Assert.Equal(new Rect(expectedX, expectedY, width, height), result);
        Assert.False(Overlaps(result, target));
    }

    [Fact]
    public void DistantGroupsDoNotAlignAcrossTheScreenAndThereIsNoSnapGrid()
    {
        var proposed = new Rect(205, 700, 138, 160);
        Assert.Equal(proposed, GroupSnapGeometry.SnapMove(proposed, new Rect(0, 0, 2000, 2000), [new Rect(200, 0, 138, 160)]));
        proposed = new Rect(357, 257, 138, 160);
        Assert.Equal(proposed, GroupSnapGeometry.SnapMove(proposed, WorkArea, []));
    }

    [Fact]
    public void ChoosesNearestCandidateRegardlessOfTargetEnumerationOrder()
    {
        Rect[] targets = [new(200, 220, 183, 120), new(554, 220, 100, 120)];
        var proposed = new Rect(400, 200, 138, 160);
        var expected = new Rect(408, 200, 138, 160);
        Assert.Equal(expected, GroupSnapGeometry.SnapMove(proposed, WorkArea, targets));
        Assert.Equal(expected, GroupSnapGeometry.SnapMove(proposed, WorkArea, targets.Reverse().ToArray()));
        Assert.Equal(new Rect(200, 220, 183, 120), targets[0]);
        Assert.Equal(new Rect(554, 220, 100, 120), targets[1]);
    }

    [Fact]
    public void IndependentNearestAxesCannotMoveInsideAnotherGroup()
    {
        Rect[] targets = [new(200, 290, 100, 100), new(320, 195, 100, 100)];
        // X=200 and Y=195 are independently nearest, but together overlap the first group.
        var result = GroupSnapGeometry.SnapMove(new Rect(205, 190, 100, 100), WorkArea, targets);
        Assert.Equal(new Rect(200, 182, 100, 100), result);
        Assert.All(targets, target => Assert.False(Overlaps(result, target)));
    }

    [Fact]
    public void ThresholdIsInclusiveAndCanBeDisabledWithoutMovingOrResizingOtherGroups()
    {
        Assert.Equal(new Rect(0, 300, 138, 160), GroupSnapGeometry.SnapMove(new Rect(12, 300, 138, 160), WorkArea, []));
        Assert.Equal(new Rect(12.01, 300, 138, 160), GroupSnapGeometry.SnapMove(new Rect(12.01, 300, 138, 160), WorkArea, []));
        Assert.Equal(new Rect(5, 300, 138, 160), GroupSnapGeometry.SnapMove(new Rect(5, 300, 138, 160), WorkArea, [], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroupSnapGeometry.SnapMove(new Rect(5, 300, 138, 160), WorkArea, [], -1));
    }

    [Fact]
    public void NegativeWorkAreaOriginAndOutOfBoundsMovementRemainConstrained()
    {
        var area = new Rect(-1000, -200, 800, 600);
        Assert.Equal(new Rect(-1000, -200, 138, 160),
            GroupSnapGeometry.SnapMove(new Rect(-1006, -195, 138, 160), area, []));
        Assert.Equal(new Rect(-338, 240, 138, 160),
            GroupSnapGeometry.SnapMove(new Rect(100, 700, 138, 160), area, []));
        Assert.Equal(new Rect(-1000, -200, 1200, 800),
            GroupSnapGeometry.SnapMove(new Rect(-800, -100, 1200, 800), area, []));
    }

    [Fact]
    public void ExpandedToolsAndCollapsedCardsUseTheirPresentedDimensions()
    {
        var expandedTool = new Rect(24, 24, 460, 400);
        var collapsedCard = new Rect(497, 100, 138, 160);
        Assert.Equal(new Rect(492, 100, 138, 160), GroupSnapGeometry.SnapMove(collapsedCard, WorkArea, [expandedTool]));
        var before = new Rect(492, 100, 138, 160);
        Assert.Equal(new Rect(24, 24, 460, 400), GroupSnapGeometry.SnapMove(new Rect(29, 24, 460, 400), WorkArea, [before]));
    }

    [Fact]
    public void NoViableNearbyEdgeDoesNotRearrangeAnAlreadyOverlappingGroup()
    {
        var proposed = new Rect(250, 250, 100, 100);
        Assert.Equal(proposed, GroupSnapGeometry.SnapMove(proposed, WorkArea, [new Rect(200, 200, 300, 300)]));
    }

    private static bool Overlaps(Rect first, Rect second) => first.Left < second.Right && first.Right > second.Left
        && first.Top < second.Bottom && first.Bottom > second.Top;
}
