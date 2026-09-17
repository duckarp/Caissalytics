using System.IO;
using Caissalytics.Components;
using Caissalytics.Core;
using Caissalytics.Engine;
using Xunit;

namespace Caissalytics.Tests;

public class MaiaModelTests
{
    [Fact]
    public void MaiaModelService_Registry_Initializes9Models()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "caissalytics_test_" + Guid.NewGuid());
        try
        {
            var service = new MaiaModelService(customStorageDir: tempDir);
            var models = service.Models;

            Assert.Equal(9, models.Count);
            int[] expectedRatings = [1100, 1200, 1300, 1400, 1500, 1600, 1700, 1800, 1900];

            for (int i = 0; i < expectedRatings.Length; i++)
            {
                int r = expectedRatings[i];
                var m = models[i];
                Assert.Equal(r, m.Rating);
                Assert.Equal($"Maia {r}", m.Name);
                Assert.Equal($"maia-{r}.pb.gz", m.WeightsFileName);
                Assert.Equal($"https://github.com/CSSLab/maia-chess/releases/download/v1.0/maia-{r}.pb.gz", m.DownloadUrl);
                Assert.False(m.IsDownloaded);
            }
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
    [InlineData(800, 1100)]
    [InlineData(1050, 1100)]
    [InlineData(1140, 1100)]
    [InlineData(1160, 1200)]
    [InlineData(1490, 1500)]
    [InlineData(1550, 1500)]
    [InlineData(1870, 1900)]
    [InlineData(2500, 1900)]
    public void MaiaModelService_GetClosestModel_SelectsNearestRating(int inputElo, int expectedClosest)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "caissalytics_test_" + Guid.NewGuid());
        try
        {
            var service = new MaiaModelService(customStorageDir: tempDir);
            var closest = service.GetClosestModel(inputElo);

            Assert.NotNull(closest);
            Assert.Equal(expectedClosest, closest.Rating);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void PracticeTab_MaiaInitialization_SetsCorrectHeadersAndIcon()
    {
        var ws = new WorkspaceState();
        var tab = ws.CreatePracticeTab(
            playerColor: PieceColor.White,
            opponentElo: 1500,
            botType: PracticeBotType.Maia);

        Assert.NotNull(tab);
        Assert.Equal(PracticeBotType.Maia, tab.BotType);
        Assert.Equal("🧠", tab.Icon);
        Assert.Equal("Maia (1500)", tab.Title);
        Assert.Equal("Player", tab.Tree.Headers["White"]);
        Assert.Equal("Maia 1500", tab.Tree.Headers["Black"]);
        Assert.Equal("1500", tab.Tree.Headers["BlackElo"]);
        Assert.Equal("Practice vs Maia", tab.Tree.Headers["Event"]);
    }

    [Fact]
    public void PracticeTab_MaiaPlayAsBlack_SetsCorrectHeaders()
    {
        var ws = new WorkspaceState();
        var tab = ws.CreatePracticeTab(
            playerColor: PieceColor.Black,
            opponentElo: 1700,
            botType: PracticeBotType.Maia);

        Assert.Equal(PracticeBotType.Maia, tab.BotType);
        Assert.Equal("Maia 1700", tab.Tree.Headers["White"]);
        Assert.Equal("1700", tab.Tree.Headers["WhiteElo"]);
        Assert.Equal("Player", tab.Tree.Headers["Black"]);
        Assert.Equal("black", tab.Orientation);
    }

    [Fact]
    public void WorkspaceSerialization_PracticeTab_PreservesMaiaBotType()
    {
        var ws = new WorkspaceState();
        var tab = ws.CreatePracticeTab(playerColor: PieceColor.White, opponentElo: 1400, botType: PracticeBotType.Maia);

        string json = ws.ExportStateJson();
        Assert.Contains("\"BotType\":\"Maia\"", json);

        var wsRestored = new WorkspaceState();
        bool restored = wsRestored.RestoreStateFromJson(json);
        Assert.True(restored);

        var restoredTab = wsRestored.Tabs.OfType<PracticeTab>().FirstOrDefault();
        Assert.NotNull(restoredTab);
        Assert.Equal(PracticeBotType.Maia, restoredTab.BotType);
        Assert.Equal(1400, restoredTab.OpponentElo);
        Assert.Equal("🧠", restoredTab.Icon);
        Assert.Equal("Maia (1400)", restoredTab.Title);
    }

    [Fact]
    public void MaiaModelService_CustomLc0Path_PersistsAndLoads()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "caissalytics_test_" + Guid.NewGuid());
        try
        {
            var service1 = new MaiaModelService(customStorageDir: tempDir);
            service1.CustomLc0Path = "/opt/custom/lc0";
            service1.SaveConfig();

            var service2 = new MaiaModelService(customStorageDir: tempDir);
            Assert.Equal("/opt/custom/lc0", service2.CustomLc0Path);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
