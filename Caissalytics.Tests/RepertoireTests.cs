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
}
