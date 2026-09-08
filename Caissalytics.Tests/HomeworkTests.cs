using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Caissalytics.Components;
using Caissalytics.Core;
using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class HomeworkTests : IDisposable
{
    private readonly string _testDir;

    public HomeworkTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "caissalytics_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task HomeworkService_LoadsDefaultTemplates_WhenNoExistingFile()
    {
        var service = new HomeworkService(_testDir);
        var sheets = await service.GetSheetsAsync();

        Assert.NotNull(sheets);
        Assert.True(sheets.Count >= 2);

        var mateSheet = sheets.FirstOrDefault(s => s.Title.Contains("Checkmate", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(mateSheet);
        Assert.NotEmpty(mateSheet.Exercises);

        foreach (var ex in mateSheet.Exercises)
        {
            Assert.False(string.IsNullOrWhiteSpace(ex.Fen));
            var board = FenParser.Parse(ex.Fen);
            Assert.NotNull(board);

            // King presence
            Assert.NotEqual(Square.None, board.FindKing(PieceColor.White));
            Assert.NotEqual(Square.None, board.FindKing(PieceColor.Black));
        }
    }

    [Fact]
    public async Task HomeworkService_CreateNewSheet_And_GetSheet()
    {
        var service = new HomeworkService(_testDir);
        var created = await service.CreateNewSheetAsync("Tactics Test 101", 4, "mate_in_one");

        Assert.NotNull(created);
        Assert.Equal("Tactics Test 101", created.Title);
        Assert.Equal(4, created.Exercises.Count);

        var retrieved = await service.GetSheetAsync(created.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(created.Id, retrieved.Id);
        Assert.Equal("Tactics Test 101", retrieved.Title);
        Assert.Equal(4, retrieved.Exercises.Count);
    }

    [Fact]
    public async Task HomeworkService_SaveSheet_UpdatesExistingSheet()
    {
        var service = new HomeworkService(_testDir);
        var created = await service.CreateNewSheetAsync("Initial Title", 2);

        created.Title = "Updated Title";
        created.CoachName = "Coach Robert";
        created.ClubName = "Bratislava Chess Club";
        created.DiagramsPerRow = 3;
        created.Exercises.Add(new HomeworkExercise
        {
            Id = Guid.NewGuid().ToString("N"),
            Order = 3,
            Title = "Exercise 3",
            Fen = "8/8/8/8/8/8/4k3/4K3 w - - 0 1",
            ToMove = "white",
            Prompt = "White to move",
            Solution = "Kf1"
        });

        await service.SaveSheetAsync(created);

        var reloaded = await service.GetSheetAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Updated Title", reloaded.Title);
        Assert.Equal("Coach Robert", reloaded.CoachName);
        Assert.Equal("Bratislava Chess Club", reloaded.ClubName);
        Assert.Equal(3, reloaded.DiagramsPerRow);
        Assert.Equal(3, reloaded.Exercises.Count);
    }

    [Fact]
    public async Task HomeworkService_DuplicateSheet_CreatesUniqueCopy()
    {
        var service = new HomeworkService(_testDir);
        var original = await service.CreateNewSheetAsync("Original Sheet", 3);

        var copy = await service.DuplicateSheetAsync(original.Id);
        Assert.NotNull(copy);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Contains("Copy", copy.Title);
        Assert.Equal(original.Exercises.Count, copy.Exercises.Count);

        // Verify exercise IDs are also unique
        Assert.NotEqual(original.Exercises[0].Id, copy.Exercises[0].Id);
    }

    [Fact]
    public async Task HomeworkService_DeleteSheet_RemovesSheetSuccessfully()
    {
        var service = new HomeworkService(_testDir);
        var sheet = await service.CreateNewSheetAsync("To Delete", 2);

        await service.DeleteSheetAsync(sheet.Id);

        var retrieved = await service.GetSheetAsync(sheet.Id);
        Assert.Null(retrieved);
    }

    [Fact]
    public void HomeworkTab_Initialization_And_Properties()
    {
        var tab = new HomeworkTab();
        Assert.NotEqual(Guid.Empty, tab.Id);
        Assert.False(string.IsNullOrWhiteSpace(tab.Title));
        Assert.Equal("📝", tab.Icon);
    }

    [Theory]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", true)] // Normal valid
    [InlineData("8/8/8/8/8/8/8/8 w - - 0 1", false)] // No kings
    [InlineData("4k3/8/8/8/8/8/8/8 w - - 0 1", false)] // No white king
    [InlineData("8/8/8/8/8/8/8/4K3 w - - 0 1", false)] // No black king
    [InlineData("4k3/8/8/8/8/8/4K3/4K3 w - - 0 1", false)] // Two white kings
    [InlineData("4k3/4k3/8/8/8/8/8/4K3 w - - 0 1", false)] // Two black kings
    [InlineData("P3k3/8/8/8/8/8/8/4K3 w - - 0 1", false)] // Pawn on 8th rank
    [InlineData("4k3/8/8/8/8/8/8/P3K3 w - - 0 1", false)] // Pawn on 1st rank
    [InlineData("8/8/8/3kK3/8/8/8/8 w - - 0 1", false)] // Kings adjacent horizontally
    [InlineData("8/8/8/4k3/4K3/8/8/8 w - - 0 1", false)] // Kings adjacent vertically
    [InlineData("8/8/8/3k4/4K3/8/8/8 w - - 0 1", false)] // Kings adjacent diagonally
    public void BoardValidationRules_DetectsInvalidPositions(string fen, bool expectedValid)
    {
        bool isValid = ValidateBoardPosition(fen, out string error);
        Assert.Equal(expectedValid, isValid);
        if (!expectedValid)
        {
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    private static bool ValidateBoardPosition(string fen, out string error)
    {
        error = string.Empty;
        BoardPosition pos;
        try
        {
            pos = FenParser.Parse(fen);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        int whiteKings = 0;
        int blackKings = 0;
        int whiteKingF = -1, whiteKingR = -1;
        int blackKingF = -1, blackKingR = -1;
        bool pawnOnBackRank = false;

        for (int f = 0; f < 8; f++)
        {
            for (int r = 0; r < 8; r++)
            {
                var p = pos[f, r];
                if (p.Type == PieceType.King)
                {
                    if (p.Color == PieceColor.White)
                    {
                        whiteKings++;
                        whiteKingF = f;
                        whiteKingR = r;
                    }
                    else
                    {
                        blackKings++;
                        blackKingF = f;
                        blackKingR = r;
                    }
                }
                else if (p.Type == PieceType.Pawn && (r == 0 || r == 7))
                {
                    pawnOnBackRank = true;
                }
            }
        }

        if (whiteKings != 1)
        {
            error = whiteKings == 0 ? "Missing White King" : "Multiple White Kings";
            return false;
        }

        if (blackKings != 1)
        {
            error = blackKings == 0 ? "Missing Black King" : "Multiple Black Kings";
            return false;
        }

        if (pawnOnBackRank)
        {
            error = "Pawns on back rank";
            return false;
        }

        if (Math.Abs(whiteKingF - blackKingF) <= 1 && Math.Abs(whiteKingR - blackKingR) <= 1)
        {
            error = "Adjacent Kings";
            return false;
        }

        return true;
    }
}
