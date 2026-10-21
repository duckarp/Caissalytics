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
    public void PgnHandler_ExportPgn_IncludesMoveNumberAfterVariationOnWhiteMove()
    {
        string pgn = "1. e4 e5 2. Nf3 (2. f4 d5) 2... Nc6 *";
        var tree = PgnHandler.ImportPgn(pgn);
        string exported = PgnHandler.ExportPgn(tree);
        Assert.Contains("2... Nc6", exported);
    }

    [Fact]
    public void PgnHandler_ExportPgn_IncludesMoveNumberAfterCommentBeforeBlackMove()
    {
        string pgn = "1. e4 {King's pawn opening} 1... e5 *";
        var tree = PgnHandler.ImportPgn(pgn);
        string exported = PgnHandler.ExportPgn(tree);
        Assert.Contains("1... e5", exported);
    }

    [Fact]
    public void PgnHandler_ExportPgn_NestedVariations_FormatsMoveNumbersCorrectly()
    {
        string pgn = "1. e4 e5 (1... c5 2. Nf3 d6 (2... e6 3. d4)) 2. Nf3 *";
        var tree = PgnHandler.ImportPgn(pgn);
        string exported = PgnHandler.ExportPgn(tree);
        Assert.Contains("1... c5", exported);
        Assert.Contains("2... e6", exported);
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

    [Fact]
    public void PgnHandler_ChessComClockWithDecimalSeconds_ExtractsClocksAndStripsMachineTags()
    {
        string chessComPgn = "1. e4 {[%clk 0:08:12.9]} e5 {[%clk 0:00:05.4][%emt 0:00:01.2] Time scramble!} 2. Nf3 {[%clk 0:08:10.0][%csl Gc4][%cal Ge2e4]} *";
        var tree = PgnHandler.ImportPgn(chessComPgn);

        var e4 = tree.Root.Children[0];
        Assert.Equal("e4", e4.San);
        Assert.Equal("0:08:12.9", e4.Clock);
        Assert.Equal("8:12", e4.FormattedClock);
        Assert.Null(e4.Comment);

        var e5 = e4.Children[0];
        Assert.Equal("e5", e5.San);
        Assert.Equal("0:00:05.4", e5.Clock);
        Assert.Equal("0:05.4", e5.FormattedClock);
        Assert.Equal("Time scramble!", e5.Comment);

        var nf3 = e5.Children[0];
        Assert.Equal("Nf3", nf3.San);
        Assert.Equal("0:08:10.0", nf3.Clock);
        Assert.Equal("8:10", nf3.FormattedClock);
        Assert.Null(nf3.Comment);
    }

    [Fact]
    public void PgnHandler_GluedMoveNumbers_ImportsAllMoves()
    {
        // Tournament PGNs often glue the move number to the move ("1.e4" rather than "1. e4").
        // Before the fix the display/import path failed to parse these tokens, so stored games
        // opened with 0–2 moves even though the PGN was complete.
        // Italian Game: a fully legal sequence where both sides actually castle, so the
        // castling SAN ("O-O") is exercised too.
        string pgn = "1.e4 e5 2.Nf3 Nc6 3.Bc4 Bc5 4.c3 Nf6 5.d3 d6 6.O-O O-O";
        var tree = PgnHandler.ImportPgn(pgn);

        var sans = new List<string>();
        var node = tree.Root;
        while (node.Children.Count > 0)
        {
            node = node.Children[0];
            sans.Add(node.San);
        }

        Assert.Equal(
            new[] { "e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "c3", "Nf6", "d3", "d6", "O-O", "O-O" },
            sans);
    }

    [Fact]
    public void ParseSan_RedundantDisambiguation_Accepted()
    {
        // After 1.d4 Nf6 2.c4 e6 3.Nc3 Bb4 4.e3 c5 only the g1 knight can reach
        // e2, so the minimal SAN is "Ne2", but PGNs commonly write "Nge2"/"N1e2".
        string fen = "rnbqk2r/pp1p1ppp/4pn2/2p5/1bPP4/2N1P3/PP3PPP/R1BQKBNR w KQkq c6 0 5";
        var pos = FenParser.Parse(fen);

        var minimal = SanParser.ParseSan(pos, "Ne2");
        Assert.False(minimal.IsEmpty);

        Assert.Equal(minimal, SanParser.ParseSan(pos, "Nge2"));
        Assert.Equal(minimal, SanParser.ParseSan(pos, "N1e2"));

        // A disambiguation pointing at the other (b1) knight must not match.
        Assert.True(SanParser.ParseSan(pos, "Nbe2").IsEmpty);
    }

    [Fact]
    public void ParseSan_PawnAndBishopCaptureOnSameSquare_AreDistinguishedByCase()
    {
        // Both the b5 pawn and the e4 bishop can capture on c6.
        var pos = FenParser.Parse("4k3/8/2n5/1P6/4B3/8/8/4K3 w - - 0 1");

        var pawnCapture = SanParser.ParseSan(pos, "bxc6");
        var bishopCapture = SanParser.ParseSan(pos, "Bxc6");

        Assert.Equal(Square.B5, pawnCapture.From);
        Assert.Equal(Square.E4, bishopCapture.From);
    }

    [Fact]
    public void ParseSan_LowercaseInput_AcceptedWhenUnambiguous()
    {
        var pos = FenParser.Parse("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");

        var move = SanParser.ParseSan(pos, "nf3");

        Assert.Equal(Square.G1, move.From);
        Assert.Equal(Square.F3, move.To);
    }

    [Fact]
    public void GameTree_VariationNavigation_NextAndPreviousVariation_Work()
    {
        // 1. e4 e5 (1... c5)
        var tree = new GameTree();
        var e4 = tree.AddMoveSan("e4")!;
        var e5 = tree.AddMoveSan("e5")!;

        // Go back to e4 and add variation c5
        tree.GoBack();
        var c5 = tree.AddMoveSan("c5")!;

        // CurrentNode is now c5 (index 1 of e4)
        Assert.Equal(c5, tree.CurrentNode);

        // Next variation should cycle back to e5 (index 0)
        bool nextOk = tree.GoToNextVariation();
        Assert.True(nextOk);
        Assert.Equal(e5, tree.CurrentNode);

        // Next variation again should cycle to c5 (index 1)
        nextOk = tree.GoToNextVariation();
        Assert.True(nextOk);
        Assert.Equal(c5, tree.CurrentNode);

        // Previous variation should cycle to e5
        bool prevOk = tree.GoToPreviousVariation();
        Assert.True(prevOk);
        Assert.Equal(e5, tree.CurrentNode);
    }

    [Fact]
    public void Move_DefaultAndEmpty_AreConsistent()
    {
        var def = default(Move);
        var empty = Move.Empty;
        var sameSq = new Move(Square.A1, Square.A1);
        var valid = new Move(Square.E2, Square.E4);

        Assert.True(def.IsEmpty);
        Assert.True(empty.IsEmpty);
        Assert.True(sameSq.IsEmpty);
        Assert.False(valid.IsEmpty);

        Assert.Equal(empty, def);
        Assert.Equal(def, empty);
        Assert.Equal(sameSq, def);
        Assert.NotEqual(valid, def);

        Assert.Equal("0000", def.ToUci());
        Assert.Equal("0000", empty.ToUci());
        Assert.Equal("0000", sameSq.ToUci());
        Assert.Equal("e2e4", valid.ToUci());
    }

    [Fact]
    public void SanParser_EmptyMove_ReturnsEmptyStringNotRa1()
    {
        var startPos = FenParser.Parse(BoardPosition.StartFen);

        Assert.Equal("", SanParser.ToSan(startPos, default(Move)));
        Assert.Equal("", SanParser.ToSan(startPos, Move.Empty));
        Assert.Equal("", SanParser.ToSan(startPos, new Move(Square.A1, Square.A1)));
    }
}
