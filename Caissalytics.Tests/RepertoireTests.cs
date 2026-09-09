using System.Text.Json;
using Caissalytics.Core;
using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class RepertoireTests
{
    [Fact]
    public async Task RepertoireService_AddAndGetMoves_PersistsAndRetrievesByFen()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "rep_test_" + Guid.NewGuid().ToString("N"));
        var service = new RepertoireService(tempDir);

        string startFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        // Add 1. d4 to White repertoire
        await service.AddOrUpdateMoveAsync(new RepertoireMove
        {
            Fen = startFen,
            MoveSan = "d4",
            MoveUci = "d2d4",
            Color = "white",
            Status = "alt",
            Note = "Queen Pawn Opening"
        });

        var moves = await service.GetMovesForPositionAsync(startFen, "white");
        Assert.Contains(moves, m => m.MoveSan == "d4" && m.Status == "alt" && m.Note == "Queen Pawn Opening");

        // Update note
        await service.AddOrUpdateMoveAsync(new RepertoireMove
        {
            Fen = startFen,
            MoveSan = "d4",
            MoveUci = "d2d4",
            Color = "white",
            Status = "main",
            Note = "Solid positional choice"
        });

        var updatedMoves = await service.GetMovesForPositionAsync(startFen, "white");
        var d4Move = updatedMoves.FirstOrDefault(m => m.MoveSan == "d4");
        Assert.NotNull(d4Move);
        Assert.Equal("main", d4Move.Status);
        Assert.Equal("Solid positional choice", d4Move.Note);
    }

    [Fact]
    public async Task RepertoireService_RemoveMove_DeletesEntry()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "rep_test_" + Guid.NewGuid().ToString("N"));
        var service = new RepertoireService(tempDir);

        string fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";

        await service.AddOrUpdateMoveAsync(new RepertoireMove
        {
            Fen = fen,
            MoveSan = "e5",
            Color = "black",
            Status = "alt",
            Note = "Open game"
        });

        var beforeDelete = await service.GetMovesForPositionAsync(fen, "black");
        Assert.Contains(beforeDelete, m => m.MoveSan == "e5");

        await service.RemoveMoveAsync(fen, "e5", "black");

        var afterDelete = await service.GetMovesForPositionAsync(fen, "black");
        Assert.DoesNotContain(afterDelete, m => m.MoveSan == "e5");
    }

    [Fact]
    public async Task RepertoireService_ExportPgn_GeneratesValidStudy()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "rep_test_" + Guid.NewGuid().ToString("N"));
        var service = new RepertoireService(tempDir);

        string pgn = await service.ExportRepertoireToPgnAsync("white");
        Assert.Contains("[Event \"Caissalytics Personal White Repertoire\"]", pgn);
        Assert.Contains("e4", pgn);
    }

    [Fact]
    public void LichessExplorerResponse_DeserializesCorrectly()
    {
        string json = """
        {
            "white": 12500,
            "draws": 8200,
            "black": 9100,
            "moves": [
                {
                    "san": "e4",
                    "uci": "e2e4",
                    "white": 6000,
                    "draws": 3800,
                    "black": 4200,
                    "averageRating": 2520
                },
                {
                    "san": "d4",
                    "uci": "d2d4",
                    "white": 5100,
                    "draws": 3400,
                    "black": 3700,
                    "averageRating": 2545
                }
            ],
            "topGames": [
                {
                    "id": "abc12345",
                    "winner": "white",
                    "white": { "name": "Carlsen, Magnus", "rating": 2882 },
                    "black": { "name": "Nakamura, Hikaru", "rating": 2875 },
                    "year": 2024,
                    "month": "05"
                }
            ]
        }
        """;

        var response = JsonSerializer.Deserialize<LichessExplorerResponse>(json);
        Assert.NotNull(response);
        Assert.Equal(12500, response.White);
        Assert.Equal(8200, response.Draws);
        Assert.Equal(9100, response.Black);
        Assert.Equal(2, response.Moves.Count);

        var firstMove = response.Moves[0];
        Assert.Equal("e4", firstMove.San);
        Assert.Equal(2520, firstMove.AverageRating);

        Assert.Single(response.TopGames);
        var game = response.TopGames[0];
        Assert.Equal("Carlsen, Magnus", game.White.Name);
        Assert.Equal(2882, game.White.Rating);
        Assert.Equal(2024, game.Year);
    }

    [Fact]
    public async Task RepertoireService_SaveAndGetLine_PersistsNamedLine()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "rep_test_" + Guid.NewGuid().ToString("N"));
        var service = new RepertoireService(tempDir);

        var line = new RepertoireLine
        {
            Name = "Spanish Opening, Morphy Defense",
            Color = "white",
            Description = "Classical main line",
            Moves = new List<RepertoireLineMove>
            {
                new() { Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -", MoveSan = "e4", MoveUci = "e2e4", MoveNumber = 1 },
                new() { Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq -", MoveSan = "e5", MoveUci = "e7e5", MoveNumber = 2 },
                new() { Fen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq -", MoveSan = "Nf3", MoveUci = "g1f3", MoveNumber = 3 },
                new() { Fen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq -", MoveSan = "Nc6", MoveUci = "b8c6", MoveNumber = 4 },
                new() { Fen = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq -", MoveSan = "Bb5", MoveUci = "f1b5", MoveNumber = 5 },
            }
        };

        await service.SaveLineAsync(line);

        var lines = await service.GetLinesAsync();
        Assert.Single(lines);
        Assert.Equal("Spanish Opening, Morphy Defense", lines[0].Name);
        Assert.Equal("white", lines[0].Color);
        Assert.Equal(5, lines[0].Moves.Count);
        Assert.Equal("e4", lines[0].Moves[0].MoveSan);
        Assert.Equal("Bb5", lines[0].Moves[4].MoveSan);
    }

    [Fact]
    public async Task RepertoireService_GetLinesByColor_FiltersCorrectly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "rep_test_" + Guid.NewGuid().ToString("N"));
        var service = new RepertoireService(tempDir);

        await service.SaveLineAsync(new RepertoireLine
        {
            Name = "Italian Game",
            Color = "white",
            Moves = new List<RepertoireLineMove>
            {
                new() { Fen = "start", MoveSan = "e4", MoveUci = "e2e4", MoveNumber = 1 }
            }
        });

        await service.SaveLineAsync(new RepertoireLine
        {
            Name = "Sicilian Defense",
            Color = "black",
            Moves = new List<RepertoireLineMove>
            {
                new() { Fen = "after_e4", MoveSan = "c5", MoveUci = "c7c5", MoveNumber = 1 }
            }
        });

        var whiteLines = await service.GetLinesAsync("white");
        Assert.Single(whiteLines);
        Assert.Equal("Italian Game", whiteLines[0].Name);

        var blackLines = await service.GetLinesAsync("black");
        Assert.Single(blackLines);
        Assert.Equal("Sicilian Defense", blackLines[0].Name);

        var allLines = await service.GetLinesAsync();
        Assert.Equal(2, allLines.Count);
    }

    [Fact]
    public async Task RepertoireService_DeleteLine_RemovesLine()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "rep_test_" + Guid.NewGuid().ToString("N"));
        var service = new RepertoireService(tempDir);

        var line = new RepertoireLine
        {
            Name = "Test Line",
            Color = "white",
            Moves = new List<RepertoireLineMove>
            {
                new() { Fen = "start", MoveSan = "e4", MoveUci = "e2e4", MoveNumber = 1 }
            }
        };

        await service.SaveLineAsync(line);
        var linesBefore = await service.GetLinesAsync();
        Assert.Single(linesBefore);

        await service.DeleteLineAsync(line.Id);
        var linesAfter = await service.GetLinesAsync();
        Assert.Empty(linesAfter);
    }

    [Fact]
    public async Task RepertoireService_GetLineById_ReturnsCorrectLine()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "rep_test_" + Guid.NewGuid().ToString("N"));
        var service = new RepertoireService(tempDir);

        var line = new RepertoireLine
        {
            Name = "French Defense",
            Color = "black",
            Description = "Solid defense against 1.e4",
            Moves = new List<RepertoireLineMove>
            {
                new() { Fen = "after_e4", MoveSan = "e6", MoveUci = "e7e6", MoveNumber = 1 }
            }
        };

        await service.SaveLineAsync(line);

        var retrieved = await service.GetLineByIdAsync(line.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("French Defense", retrieved.Name);
        Assert.Equal("Solid defense against 1.e4", retrieved.Description);

        var nonExistent = await service.GetLineByIdAsync("nonexistent");
        Assert.Null(nonExistent);
    }

    [Fact]
    public async Task RepertoireService_UpdateLine_ModifiesExisting()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "rep_test_" + Guid.NewGuid().ToString("N"));
        var service = new RepertoireService(tempDir);

        var line = new RepertoireLine
        {
            Name = "Original Name",
            Color = "white",
            Moves = new List<RepertoireLineMove>
            {
                new() { Fen = "start", MoveSan = "e4", MoveUci = "e2e4", MoveNumber = 1 }
            }
        };

        await service.SaveLineAsync(line);

        // Update the same line (same ID)
        line.Name = "Updated Name";
        line.Description = "Now with description";
        await service.SaveLineAsync(line);

        var lines = await service.GetLinesAsync();
        Assert.Single(lines);
        Assert.Equal("Updated Name", lines[0].Name);
        Assert.Equal("Now with description", lines[0].Description);
    }

    [Fact]
    public async Task RepertoireService_LegacyMigration_PreservesMovesAndAddsLines()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "rep_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        // Write a legacy-format file (RepertoireTree without Lines)
        var legacyTree = new RepertoireTree
        {
            WhiteMoves = new List<RepertoireMove>
            {
                new() { Fen = "start_fen", MoveSan = "d4", Color = "white", Status = "main", Note = "QGD" }
            },
            BlackMoves = new List<RepertoireMove>
            {
                new() { Fen = "after_e4", MoveSan = "c5", Color = "black", Status = "main", Note = "Sicilian" }
            }
        };

        string json = JsonSerializer.Serialize(legacyTree, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(tempDir, "repertoire_data.json"), json);

        // Load with new service — should migrate
        var service = new RepertoireService(tempDir);

        // Individual moves should still be there
        var whiteMoves = await service.GetMovesForPositionAsync("start_fen", "white");
        Assert.Contains(whiteMoves, m => m.MoveSan == "d4");

        var blackMoves = await service.GetMovesForPositionAsync("after_e4", "black");
        Assert.Contains(blackMoves, m => m.MoveSan == "c5");

        // Lines list should be empty (legacy had no lines)
        var lines = await service.GetLinesAsync();
        Assert.Empty(lines);
    }
}

