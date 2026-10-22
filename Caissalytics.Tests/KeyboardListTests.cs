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
}
