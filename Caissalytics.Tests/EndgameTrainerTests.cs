using Caissalytics.Components;
using Caissalytics.Core;
using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class EndgameTrainerTests
{
    [Fact]
    public void Curriculum_ContainsExpectedPositions_AllValidFens()
    {
        var positions = EndgameCurriculum.AllPositions;
        Assert.NotEmpty(positions);
        Assert.True(positions.Count >= 12);

        foreach (var pos in positions)
        {
            Assert.False(string.IsNullOrWhiteSpace(pos.Id));
            Assert.False(string.IsNullOrWhiteSpace(pos.Title));
            Assert.False(string.IsNullOrWhiteSpace(pos.Description));
            Assert.False(string.IsNullOrWhiteSpace(pos.CoachingTip));

            // Verify FEN parses into valid board position
            var board = FenParser.Parse(pos.Fen);
            Assert.NotNull(board);

            // King presence check
            var whiteKing = board.FindKing(PieceColor.White);
            var blackKing = board.FindKing(PieceColor.Black);
            Assert.NotEqual(Square.None, whiteKing);
            Assert.NotEqual(Square.None, blackKing);

            // Verify piece count is <= 7
            int pieceCount = TablebaseService.CountPiecesInFen(pos.Fen);
            Assert.InRange(pieceCount, 2, 7);

            // Verify active side has legal moves
            var legalMoves = MoveGenerator.GenerateLegalMoves(board);
            Assert.NotEmpty(legalMoves);
        }
    }

    [Fact]
    public void Curriculum_CoversAllMajorEndgameCategories()
    {
        var pawnEndgames = EndgameCurriculum.GetByCategory(EndgameCategory.Pawns);
        var rookEndgames = EndgameCurriculum.GetByCategory(EndgameCategory.Rooks);
        var queenEndgames = EndgameCurriculum.GetByCategory(EndgameCategory.Queens);
        var minorEndgames = EndgameCurriculum.GetByCategory(EndgameCategory.MinorPieces);
        var practicalEndgames = EndgameCurriculum.GetByCategory(EndgameCategory.Practical);

        Assert.NotEmpty(pawnEndgames);
        Assert.NotEmpty(rookEndgames);
        Assert.NotEmpty(queenEndgames);
        Assert.NotEmpty(minorEndgames);
        Assert.NotEmpty(practicalEndgames);

        // Spot-check classic textbook positions
        var lucena = EndgameCurriculum.GetById("rook_lucena");
        Assert.NotNull(lucena);
        Assert.Equal("Win", lucena.TargetOutcome);

        var philidor = EndgameCurriculum.GetById("rook_philidor");
        Assert.NotNull(philidor);
        Assert.Equal("Draw", philidor.TargetOutcome);

        var trebuchet = EndgameCurriculum.GetById("kp_trebuchet");
        Assert.NotNull(trebuchet);
    }

    [Fact]
    public void WorkspaceState_ManagesEndgameTrainerTab_PersistsAndRestores()
    {
        var state = new WorkspaceState();

        // Create endgame tab
        var tab = state.CreateEndgameTrainerTab("rook_lucena");
        Assert.NotNull(tab);
        Assert.Equal("Endgames", tab.Title);
        Assert.Equal("🏆", tab.Icon);
        Assert.Equal("rook_lucena", tab.SelectedPositionId);
        Assert.Same(tab, state.ActiveTab);

        // Calling again reuses tab
        var tabAgain = state.CreateEndgameTrainerTab("rook_philidor");
        Assert.Same(tab, tabAgain);
        Assert.Equal("rook_philidor", tabAgain.SelectedPositionId);

        // Serialize state
        string json = state.ExportStateJson();
        Assert.Contains("endgames", json);
        Assert.Contains("rook_philidor", json);

        // Restore into new state
        var restoredState = new WorkspaceState();
        bool restored = restoredState.RestoreStateFromJson(json);
        Assert.True(restored);

        var restoredEndgameTab = restoredState.Tabs.OfType<EndgameTrainerTab>().FirstOrDefault();
        Assert.NotNull(restoredEndgameTab);
        Assert.Equal("rook_philidor", restoredEndgameTab.SelectedPositionId);
    }
}
