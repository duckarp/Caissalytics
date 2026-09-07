using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class UserProfileTests
{
    [Fact]
    public void UserProfile_PropertiesAndUrls_ComputedCorrectly()
    {
        var profile = new UserProfile
        {
            FirstName = "Garry",
            LastName = "Kasparov",
            FideId = "4100018",
            LichessUsername = "KasparovG",
            ChessComUsername = "GKasparov"
        };

        Assert.Equal("Garry Kasparov", profile.FullName);
        Assert.True(profile.HasFideId);
        Assert.Equal("https://ratings.fide.com/profile/4100018", profile.FideProfileUrl);
        Assert.True(profile.HasLichess);
        Assert.Equal("https://lichess.org/@/KasparovG", profile.LichessProfileUrl);
        Assert.True(profile.HasChessCom);
        Assert.Equal("https://www.chess.com/member/GKasparov", profile.ChessComProfileUrl);
    }

    [Fact]
    public void UserProfile_EmptyProfile_ReturnsDefaults()
    {
        var profile = new UserProfile();

        Assert.Equal("Anonymous Player", profile.FullName);
        Assert.False(profile.HasFideId);
        Assert.Null(profile.FideProfileUrl);
        Assert.False(profile.HasLichess);
        Assert.Null(profile.LichessProfileUrl);
        Assert.False(profile.HasChessCom);
        Assert.Null(profile.ChessComProfileUrl);
    }

    [Fact]
    public async Task UserProfileService_SaveAndLoad_RoundtripsCleanly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"caissa_profile_test_{Guid.NewGuid():N}");
        string profilePath = Path.Combine(tempDir, "user_profile.json");

        try
        {
            var service = new UserProfileService(profilePath);

            // Initial load from empty
            var initial = await service.GetProfileAsync();
            Assert.Equal("Anonymous Player", initial.FullName);

            // Save new profile
            var updated = new UserProfile
            {
                FirstName = "Magnus",
                LastName = "Carlsen",
                FideId = "1503014",
                LichessUsername = "DrNykterstein",
                ChessComUsername = "MagnusCarlsen"
            };

            bool eventFired = false;
            service.OnProfileChanged += () => eventFired = true;

            await service.SaveProfileAsync(updated);
            Assert.True(eventFired);

            // Load back with a fresh service instance
            var service2 = new UserProfileService(profilePath);
            var loaded = await service2.GetProfileAsync();

            Assert.Equal("Magnus Carlsen", loaded.FullName);
            Assert.Equal("1503014", loaded.FideId);
            Assert.Equal("DrNykterstein", loaded.LichessUsername);
            Assert.Equal("MagnusCarlsen", loaded.ChessComUsername);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Theory]
    [InlineData("DrNykterstein", null, true)]
    [InlineData("drnykterstein", null, true)]
    [InlineData("MagnusCarlsen", null, true)]
    [InlineData("magnuscarlsen", null, true)]
    [InlineData("Magnus Carlsen", null, true)]
    [InlineData("Carlsen, Magnus", null, true)]
    [InlineData("Carlsen, M.", null, true)]
    [InlineData("GM Magnus Carlsen", null, true)]
    [InlineData("RandomOpponent", "1503014", true)]
    [InlineData("RandomOpponent", null, false)]
    [InlineData("Hikaru", null, false)]
    public void MatchesPlayer_IdentifiesUserCorrectly(string playerName, string? fideId, bool expectedMatch)
    {
        var profile = new UserProfile
        {
            FirstName = "Magnus",
            LastName = "Carlsen",
            FideId = "1503014",
            LichessUsername = "DrNykterstein",
            ChessComUsername = "MagnusCarlsen"
        };

        Assert.Equal(expectedMatch, profile.MatchesPlayer(playerName, fideId));
    }

    [Fact]
    public void MatchesPlayer_EmptyProfile_NeverMatches()
    {
        var profile = new UserProfile();

        Assert.False(profile.MatchesPlayer("Magnus Carlsen"));
        Assert.False(profile.MatchesPlayer("DrNykterstein"));
        Assert.False(profile.MatchesPlayer("Player", "1503014"));
    }
}
