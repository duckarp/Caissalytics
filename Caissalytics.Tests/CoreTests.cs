using Caissalytics.Core;
using Xunit;

namespace Caissalytics.Tests;

public class CoreTests
{
    [Fact]
    public void StartPosition_Has20LegalMoves()
    {
        var pos = FenParser.Parse(BoardPosition.StartFen);
        var moves = MoveGenerator.GenerateLegalMoves(pos);
        Assert.Equal(20, moves.Count);

        var dests = MoveGenerator.GetLegalDestinations(pos);
        Assert.Equal(10, dests.Count); // 8 pawns + 2 knights have moves
    }

    [Fact]
    public void FenParser_Roundtrip_Works()
    {
        string original = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3";
        var pos = FenParser.Parse(original);
        string exported = FenParser.ToFen(pos);
        Assert.Equal(original, exported);
    }

    [Fact]
    public void Castling_LegalMove_Works()
    {
        // Position where White can castle Kingside
        string fen = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4";
        var pos = FenParser.Parse(fen);
        var moves = MoveGenerator.GenerateLegalMoves(pos);

        var castleMove = moves.FirstOrDefault(m => m.IsCastling && m.To == Square.G1);
        Assert.NotEqual(Move.Empty, castleMove);

        var next = MoveGenerator.ApplyMove(pos, castleMove);
        Assert.Equal(PieceType.King, next[Square.G1].Type);
        Assert.Equal(PieceType.Rook, next[Square.F1].Type);
        Assert.Equal(Piece.None, next[Square.E1]);
        Assert.Equal(Piece.None, next[Square.H1]);
        Assert.Equal(CastlingRights.None, next.Castling & CastlingRights.WhiteBoth);
    }

    [Fact]
    public void EnPassant_Execution_Works()
    {
        // 1. e4 e6 2. e5 d5 3. exd6
        string fen = "rnbqkbnr/ppp2ppp/4p3/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3";
        var pos = FenParser.Parse(fen);
        var moves = MoveGenerator.GenerateLegalMoves(pos);

        var epMove = moves.FirstOrDefault(m => m.IsEnPassant);
        Assert.NotEqual(Move.Empty, epMove);
        Assert.Equal(Square.E5, epMove.From);
        Assert.Equal(Square.D6, epMove.To);

        var next = MoveGenerator.ApplyMove(pos, epMove);
        Assert.Equal(PieceType.Pawn, next[Square.D6].Type);
        Assert.Equal(PieceColor.White, next[Square.D6].Color);
        Assert.Equal(Piece.None, next[Square.D5]); // Captured black pawn
    }

    [Fact]
    public void Checkmate_Detection_Works()
    {
        // Scholar's Mate: 1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 4. Qxf7#
        string fen = "r1bqkb1r/pppp1Qpp/2n2n2/4p3/2B1P3/8/PPPP1PPP/RNB1K1NR b KQkq - 0 4";
        var pos = FenParser.Parse(fen);

        Assert.True(MoveGenerator.IsInCheck(pos, PieceColor.Black));
        Assert.True(MoveGenerator.IsCheckmate(pos));
        Assert.Empty(MoveGenerator.GenerateLegalMoves(pos));
    }

    [Fact]
    public void Stalemate_Detection_Works()
    {
        // Stalemate: Black king on a8, White queen on c7, White king on c8
        string fen = "k7/2Q5/2K5/8/8/8/8/8 b - - 0 1";
        var pos = FenParser.Parse(fen);

        Assert.False(MoveGenerator.IsInCheck(pos, PieceColor.Black));
        Assert.True(MoveGenerator.IsStalemate(pos));
        Assert.Empty(MoveGenerator.GenerateLegalMoves(pos));
    }

    [Fact]
    public void GameTree_BranchingAndPgn_Works()
    {
        var tree = new GameTree();

        // 1. e4
        var e4 = MoveGenerator.GenerateLegalMoves(tree.CurrentNode.Position)
            .First(m => m.From == Square.E2 && m.To == Square.E4);
        tree.AddMove(e4);

        // 1... e5
        var e5 = MoveGenerator.GenerateLegalMoves(tree.CurrentNode.Position)
            .First(m => m.From == Square.E7 && m.To == Square.E5);
        tree.AddMove(e5);

        // Go back to move 1, add variation 1... c5 (Sicilian)
        tree.GoBack();
        var c5 = MoveGenerator.GenerateLegalMoves(tree.CurrentNode.Position)
            .First(m => m.From == Square.C7 && m.To == Square.C5);
        tree.AddMove(c5);

        Assert.Equal(2, tree.Root.Children[0].Children.Count);

        // Export PGN
        string pgn = PgnHandler.ExportPgn(tree);
        Assert.Contains("1. e4 e5 ( 1... c5)", pgn);

        // Import PGN back
        var importedTree = PgnHandler.ImportPgn(pgn);
        Assert.Single(importedTree.Root.Children);
        Assert.Equal(2, importedTree.Root.Children[0].Children.Count);
    }

    [Fact]
    public void PgnHandler_ClockAndCommentParsing_ExtractsClocksCleanly()
    {
        string pgnWithClocks = "1. e4 {[%clk 0:04:21]} e5 {[%clk 0:00:45] Great defensive resource!} 2. Nf3 {[%clk 0:04:15][%eval +0.25]} *";
        var tree = PgnHandler.ImportPgn(pgnWithClocks);

        var e4 = tree.Root.Children[0];
        Assert.Equal("e4", e4.San);
        Assert.Equal("0:04:21", e4.Clock);
        Assert.Equal("4:21", e4.FormattedClock);
        Assert.Null(e4.Comment);

        var e5 = e4.Children[0];
        Assert.Equal("e5", e5.San);
        Assert.Equal("0:00:45", e5.Clock);
        Assert.Equal("0:45", e5.FormattedClock);
        Assert.Equal("Great defensive resource!", e5.Comment);

        var nf3 = e5.Children[0];
        Assert.Equal("Nf3", nf3.San);
        Assert.Equal("0:04:15", nf3.Clock);
        Assert.Equal("+0.25", nf3.Eval);
        Assert.Null(nf3.Comment);
    }
}
