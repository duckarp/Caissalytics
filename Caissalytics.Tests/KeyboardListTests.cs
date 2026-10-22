using Caissalytics.Components.Common;
using Xunit;

namespace Caissalytics.Tests;

public class KeyboardListTests
{
    [Theory]
    [InlineData("down", -1, 0)]      // nothing active yet: first row
    [InlineData("down", 3, 4)]
    [InlineData("down", 9, 9)]       // stays on the last row
    [InlineData("up", -1, 0)]
    [InlineData("up", 4, 3)]
    [InlineData("up", 0, 0)]
    [InlineData("first", 7, 0)]
    [InlineData("last", 2, 9)]
    [InlineData("pageDown", -1, 4)]  // a page (5) from "before the first row"
    [InlineData("pageDown", 2, 7)]
    [InlineData("pageDown", 7, 9)]
    [InlineData("pageUp", 7, 2)]
    [InlineData("pageUp", 3, 0)]
    [InlineData("down", 25, 9)]      // stale index after the list shrank
    public void NextIndex_StaysWithinTheList(string key, int current, int expected)
    {
        Assert.Equal(expected, KeyboardList.NextIndex(key, current, count: 10, pageStep: 5));
    }

    [Theory]
    [InlineData("down")]
    [InlineData("last")]
    public void NextIndex_EmptyList_HasNoActiveRow(string key)
    {
        Assert.Equal(-1, KeyboardList.NextIndex(key, current: 0, count: 0, pageStep: 10));
    }

    private static KeyboardList.SelectionChange Range(int anchor, int active, int[] baseSelection, int[] currentlySelected) =>
        KeyboardList.RangeChange(anchor, active, count: 10, baseSelection.ToHashSet(), i => currentlySelected.Contains(i));

    [Fact]
    public void RangeChange_SelectsRowsFromAnchorToActive()
    {
        var change = Range(anchor: 2, active: 5, baseSelection: [], currentlySelected: []);

        Assert.Equal(new[] { 2, 3, 4, 5 }, change.Selected);
        Assert.Empty(change.Deselected);
    }

    [Fact]
    public void RangeChange_ShrinkingGivesRowsBackTheirEarlierState()
    {
        // Range 2..5 is selected (row 4 was already selected before the range started); shrink to 2..3
        var change = Range(anchor: 2, active: 3, baseSelection: [4], currentlySelected: [2, 3, 4, 5]);

        Assert.Empty(change.Selected);
        Assert.Equal(new[] { 5 }, change.Deselected); // row 4 stays: it was selected before
    }

    [Fact]
    public void RangeChange_WorksUpwardsAndKeepsOtherSelections()
    {
        var change = Range(anchor: 6, active: 4, baseSelection: [0, 9], currentlySelected: [0, 9]);

        Assert.Equal(new[] { 4, 5, 6 }, change.Selected);
        Assert.Empty(change.Deselected);
    }
}
