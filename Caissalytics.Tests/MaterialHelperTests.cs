using Caissalytics.Core;
using Xunit;

namespace Caissalytics.Tests;

public class MaterialHelperTests
{
    [Fact]
    public void GetPieceValue_ReturnsStandardValues()
    {
        Assert.Equal(1, MaterialHelper.GetPieceValue(PieceType.Pawn));
        Assert.Equal(3, MaterialHelper.GetPieceValue(PieceType.Knight));
        Assert.Equal(3, MaterialHelper.GetPieceValue(PieceType.Bishop));
        Assert.Equal(5, MaterialHelper.GetPieceValue(PieceType.Rook));
        Assert.Equal(9, MaterialHelper.GetPieceValue(PieceType.Queen));
        Assert.Equal(0, MaterialHelper.GetPieceValue(PieceType.King));
        Assert.Equal(0, MaterialHelper.GetPieceValue(PieceType.None));
    }

    [Fact]
    public void GetMaterialState_NullPosition_ReturnsDefaultEmpty()
    {
        var state = MaterialHelper.GetMaterialState(null);

        Assert.NotNull(state);
        Assert.Empty(state.WhiteCaptured);
        Assert.Empty(state.BlackCaptured);
        Assert.Equal(0, state.WhiteScore);
        Assert.Equal(0, state.BlackScore);
        Assert.Equal(0, state.WhiteAdvantage);
        Assert.Equal(0, state.BlackAdvantage);
        Assert.False(state.HasCaptures);
    }

    [Fact]
    public void GetMaterialState_StartPosition_HasNoCapturesAndEqualScore()
    {
        var pos = FenParser.Parse(BoardPosition.StartFen);
        var state = MaterialHelper.GetMaterialState(pos);

        Assert.Empty(state.WhiteCaptured);
        Assert.Empty(state.BlackCaptured);
        Assert.Equal(39, state.WhiteScore);
        Assert.Equal(39, state.BlackScore);
        Assert.Equal(0, state.WhiteAdvantage);
        Assert.Equal(0, state.BlackAdvantage);
        Assert.False(state.HasCaptures);
    }

    [Fact]
    public void GetMaterialState_SingleCapture_CalculatesAdvantageAndPiece()
    {
        // Scandinavian: 1. e4 d5 2. exd5 (White captured black d5 pawn)
        string fen = "rnbqkbnr/ppp1pppp/8/3P4/8/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2";
        var pos = FenParser.Parse(fen);
        var state = MaterialHelper.GetMaterialState(pos);

        Assert.True(state.HasCaptures);
        Assert.Single(state.WhiteCaptured);
        Assert.Equal(PieceType.Pawn, state.WhiteCaptured[0].Type);
        Assert.Equal(PieceColor.Black, state.WhiteCaptured[0].Color);

        Assert.Empty(state.BlackCaptured);
        Assert.Equal(39, state.WhiteScore);
        Assert.Equal(38, state.BlackScore);
        Assert.Equal(1, state.WhiteAdvantage);
        Assert.Equal(0, state.BlackAdvantage);
    }

    [Fact]
    public void GetMaterialState_EqualTrade_ShowsCapturesWithNoAdvantage()
    {
        // Both sides have lost 1 knight (White missing g1 knight, Black missing g8 knight)
        // White: 8P, 1N, 2B, 2R, 1Q (score: 36)
        // Black: 8P, 1N, 2B, 2R, 1Q (score: 36)
        string fen = "rnbqkb1r/pppppppp/8/8/8/8/PPPPPPPP/RNBQKB1R w KQkq - 0 1";
        var pos = FenParser.Parse(fen);
        var state = MaterialHelper.GetMaterialState(pos);

        Assert.True(state.HasCaptures);
        Assert.Single(state.WhiteCaptured);
        Assert.Equal(PieceType.Knight, state.WhiteCaptured[0].Type);
        Assert.Equal(PieceColor.Black, state.WhiteCaptured[0].Color);

        Assert.Single(state.BlackCaptured);
        Assert.Equal(PieceType.Knight, state.BlackCaptured[0].Type);
        Assert.Equal(PieceColor.White, state.BlackCaptured[0].Color);

        Assert.Equal(36, state.WhiteScore);
        Assert.Equal(36, state.BlackScore);
        Assert.Equal(0, state.WhiteAdvantage);
        Assert.Equal(0, state.BlackAdvantage);
    }

    [Fact]
    public void GetMaterialState_PawnPromotion_OffsetsMissingPawnWithoutDoubleCounting()
    {
        // White promoted a pawn to a second queen (2 Queens, 7 Pawns on board)
        // Black has normal starting army
        // White score: 7*1 + 2*3 + 2*3 + 2*5 + 2*9 = 7 + 6 + 6 + 10 + 18 = 47
        // Black score: 39
        // Advantage: White +8
        // BlackCaptured: 0 pawns (since pawn was promoted, not captured)
        string fen = "rnbqkbnr/pppppppp/8/8/4Q3/8/PPPPPPP1/RNBQKBNR b KQkq - 0 1";
        var pos = FenParser.Parse(fen);
        var state = MaterialHelper.GetMaterialState(pos);

        Assert.Empty(state.BlackCaptured);
        Assert.Empty(state.WhiteCaptured);
        Assert.Equal(47, state.WhiteScore);
        Assert.Equal(39, state.BlackScore);
        Assert.Equal(8, state.WhiteAdvantage);
        Assert.Equal(0, state.BlackAdvantage);
    }

    [Fact]
    public void GetMaterialState_BlackAdvantage_Works()
    {
        // Black has captured White's Queen
        // White: 8P, 2N, 2B, 2R, 0Q (score: 30)
        // Black: 8P, 2N, 2B, 2R, 1Q (score: 39)
        string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNB1KBNR w KQkq - 0 1";
        var pos = FenParser.Parse(fen);
        var state = MaterialHelper.GetMaterialState(pos);

        Assert.True(state.HasCaptures);
        Assert.Empty(state.WhiteCaptured);
        Assert.Single(state.BlackCaptured);
        Assert.Equal(PieceType.Queen, state.BlackCaptured[0].Type);
        Assert.Equal(PieceColor.White, state.BlackCaptured[0].Color);

        Assert.Equal(30, state.WhiteScore);
        Assert.Equal(39, state.BlackScore);
        Assert.Equal(0, state.WhiteAdvantage);
        Assert.Equal(9, state.BlackAdvantage);
    }

    [Fact]
    public void GetMaterialState_MultipleCaptures_SortedByAscendingValue()
    {
        // Black is missing 2 pawns, 1 knight, 1 bishop, 1 rook, 1 queen
        // Black remaining: 6 pawns, 1 knight, 1 bishop, 1 rook, 0 queens, 1 king
        // FEN: 1 rook, 1 bishop, 1 knight on back rank; 6 pawns on 7th rank; king on e8
        string fen = "r1b1k1n1/ppp1pp1p/8/8/8/8/PPPPPPPP/RNBQKBNR w KQq - 0 1";
        var pos = FenParser.Parse(fen);
        var state = MaterialHelper.GetMaterialState(pos);

        Assert.Equal(6, state.WhiteCaptured.Count);
        // Ordering should be: Pawn, Pawn, Knight, Bishop, Rook, Queen
        Assert.Equal(PieceType.Pawn, state.WhiteCaptured[0].Type);
        Assert.Equal(PieceType.Pawn, state.WhiteCaptured[1].Type);
        Assert.Equal(PieceType.Knight, state.WhiteCaptured[2].Type);
        Assert.Equal(PieceType.Bishop, state.WhiteCaptured[3].Type);
        Assert.Equal(PieceType.Rook, state.WhiteCaptured[4].Type);
        Assert.Equal(PieceType.Queen, state.WhiteCaptured[5].Type);

        // All captured pieces by White should be Black pieces
        Assert.All(state.WhiteCaptured, p => Assert.Equal(PieceColor.Black, p.Color));
    }
}
