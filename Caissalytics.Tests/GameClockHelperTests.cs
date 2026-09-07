using Caissalytics.Core;
using Xunit;

namespace Caissalytics.Tests;

public class GameClockHelperTests
{
    [Fact]
    public void HasClocks_ReturnsTrue_WhenClocksExist()
    {
        string pgn = "1. e4 {[%clk 0:10:00]} e5 {[%clk 0:09:58]} *";
        var tree = PgnHandler.ImportPgn(pgn);

        Assert.True(GameClockHelper.HasClocks(tree));
    }

    [Fact]
    public void HasClocks_ReturnsFalse_WhenNoClocks()
    {
        string pgn = "1. e4 e5 2. Nf3 Nc6 *";
        var tree = PgnHandler.ImportPgn(pgn);

        Assert.False(GameClockHelper.HasClocks(tree));
    }

    [Fact]
    public void GetClockState_TracksWhiteAndBlackClocks_AsMovesAreTraversed()
    {
        string pgn = @"[Event ""Rated Blitz""]
[White ""Magnus""]
[Black ""Hikaru""]
[Result ""*""]

1. e4 {[%clk 0:05:00]} 1... e5 {[%clk 0:04:58]} 2. Nf3 {[%clk 0:04:52]} 2... Nc6 {[%clk 0:04:45]} *";

        var tree = PgnHandler.ImportPgn(pgn);

        // Position 0: Root (before move 1)
        tree.GoToStart();
        var stateRoot = GameClockHelper.GetClockState(tree);
        Assert.True(stateRoot.HasClocks);
        Assert.True(stateRoot.IsWhiteTurn);
        Assert.Equal("0:05:00", stateRoot.WhiteClock);
        Assert.Equal("5:00", stateRoot.WhiteClockFormatted);
        Assert.Equal("0:04:58", stateRoot.BlackClock);
        Assert.Equal("4:58", stateRoot.BlackClockFormatted);
        Assert.False(stateRoot.WhiteLowTime);
        Assert.False(stateRoot.BlackLowTime);

        // Position 1: After 1. e4
        tree.GoForward();
        var state1 = GameClockHelper.GetClockState(tree);
        Assert.Equal("0:05:00", state1.WhiteClock);
        Assert.Equal("5:00", state1.WhiteClockFormatted);
        Assert.Equal("0:04:58", state1.BlackClock);
        Assert.Equal("4:58", state1.BlackClockFormatted);
        Assert.False(state1.IsWhiteTurn); // Black to move!

        // Position 2: After 1... e5
        tree.GoForward();
        var state2 = GameClockHelper.GetClockState(tree);
        Assert.Equal("0:05:00", state2.WhiteClock);
        Assert.Equal("0:04:58", state2.BlackClock);
        Assert.True(state2.IsWhiteTurn); // White to move!

        // Position 3: After 2. Nf3
        tree.GoForward();
        var state3 = GameClockHelper.GetClockState(tree);
        Assert.Equal("0:04:52", state3.WhiteClock);
        Assert.Equal("4:52", state3.WhiteClockFormatted);
        Assert.Equal("0:04:58", state3.BlackClock);
        Assert.False(state3.IsWhiteTurn); // Black to move!

        // Position 4: After 2... Nc6
        tree.GoForward();
        var state4 = GameClockHelper.GetClockState(tree);
        Assert.Equal("0:04:52", state4.WhiteClock);
        Assert.Equal("0:04:45", state4.BlackClock);
        Assert.Equal("4:45", state4.BlackClockFormatted);
        Assert.True(state4.IsWhiteTurn); // White to move!
    }

    [Theory]
    [InlineData("0:08:12.9", false)]
    [InlineData("0:01:05", false)]
    [InlineData("0:00:30.1", false)]
    [InlineData("0:00:30.0", true)]
    [InlineData("0:00:25.4", true)]
    [InlineData("0:00:04.2", true)]
    [InlineData("00:15", true)]
    [InlineData("01:15", false)]
    public void IsLowTime_IdentifiesTimePressureCorrectly(string clock, bool expectedLowTime)
    {
        Assert.Equal(expectedLowTime, GameClockHelper.IsLowTime(clock));
    }
}
