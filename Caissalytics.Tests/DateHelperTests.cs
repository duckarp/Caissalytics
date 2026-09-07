using Caissalytics.Core;
using Xunit;

namespace Caissalytics.Tests;

public class DateHelperTests
{
    [Theory]
    [InlineData("1999.01.20", "20.01.1999")]
    [InlineData("1999-01-20", "20.01.1999")]
    [InlineData("1999/01/20", "20.01.1999")]
    [InlineData("20.01.1999", "20.01.1999")]
    [InlineData("7.9.2026", "07.09.2026")]
    [InlineData("07.09.2026", "07.09.2026")]
    [InlineData("1999.??.??", "??.??.1999")]
    [InlineData("1999.01.??", "??.01.1999")]
    [InlineData("????.??.??", "??.??.????")]
    public void Format_ConvertsToSlovakFormat(string input, string expected)
    {
        string actual = DateHelper.Format(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Format_DateTime_ReturnsSlovakFormat()
    {
        var dt = new DateTime(2026, 9, 7, 14, 30, 0, DateTimeKind.Utc);
        string actual = DateHelper.Format(dt);
        Assert.Equal("07.09.2026", actual);
    }

    [Theory]
    [InlineData("20.01.1999", "1999.01.20")]
    [InlineData("7.9.2026", "2026.09.07")]
    [InlineData("07.09.2026", "2026.09.07")]
    [InlineData("1999.01.20", "1999.01.20")]
    [InlineData("1999.??.??", "1999.??.??")]
    [InlineData("??.??.1999", "1999.??.??")]
    public void ToPgnDate_ConvertsToStandardPgnFormat(string input, string expected)
    {
        string actual = DateHelper.ToPgnDate(input);
        Assert.Equal(expected, actual);
    }
}
