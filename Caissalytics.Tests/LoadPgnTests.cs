using Caissalytics.Core;
using Caissalytics.Data;
using Caissalytics.Components;
using Xunit;

namespace Caissalytics.Tests;

public class LoadPgnTests
{
    private const string SamplePgn = @"[Event ""World Championship 34th""]
[Site ""London""]
[Date ""1993.09.07""]
[Round ""1""]
[White ""Kasparov, Garry""]
[Black ""Short, Nigel D""]
[Result ""1-0""]
[WhiteElo ""2815""]
[BlackElo ""2665""]

1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 6. Re1 b5 7. Bb3 d6 8. c3 O-O 9. h3 Nb8 10. d4 Nbd7 1-0";

    [Fact]
    public void LoadPgn_ValidPgn_PopulatesTreeAndHeaders()
    {
        var tree = PgnHandler.ImportPgn(SamplePgn);

        Assert.NotNull(tree);
        Assert.Equal("Kasparov, Garry", tree.Headers["White"]);
        Assert.Equal("Short, Nigel D", tree.Headers["Black"]);
        Assert.Equal("1-0", tree.Headers["Result"]);
        Assert.Equal("London", tree.Headers["Site"]);

        // Verify root has first move (1. e4)
        Assert.True(tree.CanGoForward);
        Assert.True(tree.CurrentNode.IsRoot);
        Assert.NotEmpty(tree.Root.Children);
        Assert.Equal("e4", tree.Root.Children[0].San);

        // Advance through moves
        int moveCount = 0;
        while (tree.CanGoForward)
        {
            tree.GoForward();
            moveCount++;
        }

        Assert.Equal(20, moveCount); // 10 full moves = 20 half-moves
        Assert.False(tree.CanGoForward);
        Assert.True(tree.CanGoBack);
    }

    [Fact]
    public void LoadPgn_WithVariationsAndComments_Works()
    {
        string pgnWithVariations = @"[White ""Player 1""]
[Black ""Player 2""]

1. e4 e5 (1... c5 2. Nf3 {Sicilian Defense}) 2. Nf3 Nc6 *";

        var tree = PgnHandler.ImportPgn(pgnWithVariations);

        Assert.NotNull(tree);
        Assert.True(tree.CanGoForward);
        var e4Node = tree.Root.Children[0];
        Assert.Equal("e4", e4Node.San);

        // e4 should have e5 as main move and c5 as variation
        Assert.Equal(2, e4Node.Children.Count);
        Assert.Equal("e5", e4Node.Children[0].San);
        Assert.Equal("c5", e4Node.Children[1].San);
        Assert.Contains("Sicilian Defense", e4Node.Children[1].Children[0].Comment);
    }

    [Fact]
    public void LoadPgn_AnalysisTabReplacement_UpdatesTreeWithoutDb()
    {
        // 1. Create a fresh empty analysis tab
        var tab = new AnalysisTab("Initial Tab");
        Assert.Equal("Initial Tab", tab.Title);
        Assert.Null(tab.DatabaseGameId);
        Assert.Empty(tab.Tree.Root.Children);

        // 2. Load PGN into tree
        var importedTree = PgnHandler.ImportPgn(SamplePgn);
        tab.Tree = importedTree;
        tab.DatabaseGameId = null;

        string white = tab.Tree.Headers.GetValueOrDefault("White", "");
        string black = tab.Tree.Headers.GetValueOrDefault("Black", "");
        tab.SetTitle($"{white} vs {black}");

        // 3. Verify tab state
        Assert.Equal("Kasparov, Garry vs Short, Nigel D", tab.Title);
        Assert.Null(tab.DatabaseGameId);
        Assert.NotEmpty(tab.Tree.Root.Children);
        Assert.Equal("e4", tab.Tree.Root.Children[0].San);

        // 4. Verify position change event fires on imported tree
        bool positionChangedFired = false;
        tab.Tree.PositionChanged += () => positionChangedFired = true;

        tab.Tree.GoForward();
        Assert.True(positionChangedFired);
        Assert.Equal("e4", tab.Tree.CurrentNode.San);
    }

    [Fact]
    public void LoadPgn_CustomStartFen_ParsedCorrectly()
    {
        string fenPgn = @"[Event ""Tactics""]
[FEN ""r1bqkb1r/pppp1ppp/2n5/4p3/4n3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 0 4""]

4. Qe2 d5 5. d3 *";

        var tree = PgnHandler.ImportPgn(fenPgn);

        Assert.NotNull(tree);
        Assert.True(tree.CanGoForward);
        Assert.Equal(PieceColor.White, tree.CurrentNode.Position.ActiveColor);
        Assert.Equal("Qe2", tree.Root.Children[0].San);
    }

    [Fact]
    public void Eco_ClassifiesFromBoardMoves()
    {
        var tree = new GameTree();
        tree.AddMoveSan("e4");
        tree.AddMoveSan("c5");
        tree.AddMoveSan("Nf3");
        tree.AddMoveSan("d6");
        tree.AddMoveSan("d4");
        tree.AddMoveSan("cxd4");
        tree.AddMoveSan("Nxd4");
        tree.AddMoveSan("Nf6");
        tree.AddMoveSan("Nc3");
        tree.AddMoveSan("a6");

        var mainline = new List<string>();
        var cur = tree.Root;
        while (cur.Children.Count > 0)
        {
            cur = cur.Children[0];
            mainline.Add(cur.San);
        }

        string? eco = EcoClassifier.Classify(mainline);
        Assert.Equal("B90", eco);
    }

    [Fact]
    public void GameTree_NewTree_HasNoDefaultRound()
    {
        var tree = new GameTree();
        Assert.False(tree.Headers.ContainsKey("Round"));
    }

    [Fact]
    public void LoadPgn_ComplexSmyslovPgn_ParsesCorrectly()
    {
        string pgn = """
        [Event "?"]
        [Site "?"]
        [Date "2024.08.19"]
        [Round "?"]
        [White "Ruy Lopez"]
        [Black "Smyslov/Fianchetto (3...g6)"]
        [Result "*"]
        [ECO "C70"]
        [Annotator "ChessGeek"]
        [PlyCount "19"]
        [SourceVersionDate "2024.08.19"]

        {[%evp 0,79,25,16,23,18,16,24,61,23,19,19,19,24,24,24,28,24,20,19,19,22,19,21,
        28,26,28,20,39,23,53,51,53,46,48,52,58,56,56,36,35,35,95,94,111,111,111,72,85,
        72,83,64,119,132,130,106,105,108,104,103,132,112,143,184,152,140,155,155,155,
        147,149,149,210,210,230,235,235,225,252,244,769,236]} 1. e4 e5 2. Nf3 Nc6 3.
        Bb5 g6 {We now focus on the Smyslov/Fianchetto Defense. In general, as you
        will see, often White obtains a strong center. This is in large part due to
        our next move, and the plan with it: c3 aiming for d4!} 4. c3 {There are 4
        essential common tries for Black in this position: (1) a6: The idea for
        Black is to quickly ask our Bishop a question. What do we do? We put the
        bishop on a4, which, as it turns out, does not change much. This will likely
        lead to the same positions as in options (3) and (4), with little difference
        for White. We can apply the same notions as in (3) and (4). (2) d6: The idea
        for Black is to defend e5, with later moves like Bg7 and Bd7 - which
        indirectly defends e5 by freeing up the c6 knight. Black does not want to play
        exd4 and give us the center in this line. What do we do? We eventually clarify
        the situation by playing for d5!, and transform the pawn structure when it is
        advantageous for us. (3) Nge7 & playing for a quick d5: The idea for Black
        is to quickly challenge our center, but it is unclear if this combination of a
        quick center challenge goes well with the g6 pawn. What do we do? We trade on
        e5, forcing the knight to come out. Then, at the right moment, we can take the
        c6 knight, which is now undefended by the other knight, and claim some long
        term strategic advantage. (4) Perhaps the main move, at least in theory:
        Black plays for Bg7 & Nge7, and tries to 0-0 quickly. The idea for Black is to
        simply develop in the most harmonious way and get the King safe. What do we
        do? We do not allow Black's king to sit safely on the Kingside, instead, we
        push for the aggressive yet sound f4!} a6 {Option 1 & the most popular move} (
        4... d6 {Option 2: Black plays d6, and later Bd7 & Bg7 to keep the tension, so
        as to not surrender us the entire center like in the a6 lines. But, we make
        natural moves, until they either give us the center, or they are ready to
        castle at which point we play d5! and transform the position into a Queenside
        battle.} 5. d4 Bd7 6. O-O Bg7 7. Re1 Nge7 8. d5 Nb8 9. Bxd7+ Nxd7 10. Be3 O-O
        11. c4 f5 (11... h6 12. Nfd2 c5 13. dxc6 bxc6 14. Nb3 Nf6 15. Nc3 Qc7 16. Qf3
        Kh7 17. Red1 Rfd8 18. Rac1 a5 19. Na4 c5 20. Nc3 Nc6 21. Nd5 Nxd5 22. cxd5 Na7
        23. Nxc5 $2 {The wrong decision. White would have enjoyed a slight advantage
        after a4!} (23. a4 {[%cal Ge3d2,Gd2a5]} Rdb8 24. Nd2 Rxb2 25. Nc4 Rb3 26. Qe2 {
        [%cal Ge3d2,Rb3f3,Gd2a5]}) 23... dxc5 24. Rxc5 Qb7 25. Rxa5 Nb5 26. Rxa8 Rxa8
        27. a3 Nd4 28. Bxd4 exd4 29. b4 Rc8 30. Qf4 Qb5 31. h4 Qe2 32. Rd2 Qe1+ 33. Kh2
        Re8 34. g3 Rxe4 35. Qf3 Qxd2 36. Qxe4 Qxf2+ 37. Kh1 Qf1+ 38. Kh2 d3 {0-1 (38)
        Baghdasaryan,V (2388)-Bryzgalin,K (2485) Warsaw 2012}) 12. Ng5 f4 13. Bd2 Rf6
        14. Ne6 Rxe6 15. dxe6 Nf8 16. b4 Nxe6 17. Bc3 Qe8 18. Nd2 Qf7 19. Nf3 g5 20. h3
        h5 21. c5 g4 22. hxg4 hxg4 23. Nh2) (4... Bg7 {We will look at Bg7 through
        Nge7.} 5. d4 exd4 6. cxd4 Nge7) (4... Nge7 5. d4 exd4 6. cxd4 Bg7 {Option 4!} (
        6... d5 {Option 3!} 7. exd5 {Again, our idea is to castle, and at some point
        take on c6 to play against the doubled pawns.} Nxd5 8. O-O Bg7 9. Bxc6+ bxc6
        10. Bg5 Qd6 11. Re1+ Be6 12. Nbd2 O-O 13. Ne4 Qb4 14. Qc1 Rfe8 15. Bd2 Qb6 16.
        Nc5 Bf5 17. Ne5 Rad8 18. h3 Nf6 19. Nb3 Rd5 20. Nxc6 Rxe1+ 21. Bxe1 Bf8 22. Ne5
        Be6 23. Nc4 Qb7 24. Ne3 Rh5 25. Nc5 Bxc5 26. dxc5 Bxh3 27. c6 Qc8 28. f3 Bxg2
        29. Nxg2 Qh3 30. Bc3 Nd5 31. Bd4 f6 32. Qe1 Kf7 33. Rd1 Rg5 34. Qe2 Kg7 35. Bc5
        Re5 36. Qf2 Rg5 37. Rd4 Qe6 38. Re4 Qxc6 39. Qd4 a5 40. a3 h5 41. Kh2 Kh7 42.
        Qc4 Qb7 43. Re7+ Kh8 44. Bd4 Rf5 45. Rd7 Qb8 46. Qc6 {1-0 (46) Naiditsch,A
        (2721)-Nakamura,H (2763) Douglas 2018}) 7. d5 Ne5 8. Nxe5 Bxe5 9. O-O O-O (9...
        c6 10. f4) (9... a6 10. f4 (10. Ba4 {Or, we can even play this move
        transposing to line (1)!.})) 10. Nc3 a6 11. Ba4 b5 12. Bb3 d6 13. Bh6 Re8 14.
        f4 Bg7 15. Bxg7 Kxg7 16. Qd4+ f6 17. h3 Bb7 18. Rad1 Nc8 19. Rde1 Qe7 20. a4
        Nb6 21. Rf2 bxa4 22. Nxa4 Rab8 23. Nxb6 cxb6 24. Rfe2 b5 25. e5 Kf7 26. Re3
        fxe5 27. fxe5 Rbc8 28. Qf4+ Kg8 29. exd6 Qf8 30. Qxf8+ Rxf8 31. Re7 Ba8 32. d7
        Rcd8 33. d6+ Kh8 34. Re8 Bc6 35. Rxd8 Rxd8 36. Re8+ Rxe8 37. dxe8=Q+ Bxe8 38.
        Be6 Kg7 39. d7 Bxd7 40. Bxd7 Kf6 41. Kf2 Ke5 42. Bc8 a5 43. Bd7 b4 44. b3 Ke4
        45. Kg3 Ke5 46. Kg4 Kf6 47. h4 Ke7 48. Bb5 Kf6 49. Kf4 Ke6 50. Ke4 Kd6 51. Kd4
        Ke6 52. g4 Kd6 53. h5 g5 54. h6 Ke6 55. Bd3 Kf6 56. Bxh7 Kf7 57. Bd3 Kg8 58.
        Kc4 Kh8 59. Kb5 {1-0 (59) Gal,H (2074)-Furedi,E (1989) Budapest 2013}) 5. Ba4
        Nge7 6. d4 exd4 7. cxd4 d5 (7... Bg7 8. d5 Ne5 9. Nxe5 Bxe5 10. O-O {And again,
        another pretty complete transposition.}) 8. exd5 Nxd5 9. O-O Bg7 10. Bxc6+ {
        again we can opt for the same structural advantage as in option (3), the
        position fully transposes, aside from the a6 pawn.} *
        """;

        var tree = PgnHandler.ImportPgn(pgn);
        Assert.NotNull(tree);
        Assert.Equal("Ruy Lopez", tree.Headers["White"]);
        Assert.Equal("Smyslov/Fianchetto (3...g6)", tree.Headers["Black"]);
        Assert.Equal("C70", tree.Headers["ECO"]);
        Assert.Equal("*", tree.Headers["Result"]);

        // Walk mainline
        var moves = new List<string>();
        var cur = tree.Root;
        while (cur.Children.Count > 0)
        {
            cur = cur.Children[0];
            moves.Add(cur.San);
        }

        // Mainline should be: e4, e5, Nf3, Nc6, Bb5, g6, c3, a6, Ba4, Nge7, d4, exd4, cxd4, d5, exd5, Nxd5, O-O, Bg7, Bxc6+
        Assert.Equal(19, moves.Count);
        Assert.Equal(new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "g6", "c3", "a6", "Ba4", "Nge7", "d4", "exd4", "cxd4", "d5", "exd5", "Nxd5", "O-O", "Bg7", "Bxc6+" }, moves);

        // Find move 4. c3 (which is index 6 from Root)
        var c3Node = tree.Root.Children[0] // e4
            .Children[0] // e5
            .Children[0] // Nf3
            .Children[0] // Nc6
            .Children[0] // Bb5
            .Children[0] // g6
            .Children[0]; // c3

        Assert.Equal("c3", c3Node.San);
        Assert.NotNull(c3Node.Comment);
        Assert.Contains("There are 4 essential common tries", c3Node.Comment);

        // 4. c3 must have 4 alternative continuations: a6 (mainline), d6 (option 2), Bg7, Nge7
        Assert.Equal(4, c3Node.Children.Count);
        var c3ChildSans = c3Node.Children.Select(c => c.San).ToList();
        Assert.Equal(new[] { "a6", "d6", "Bg7", "Nge7" }, c3ChildSans);

        // Verify Option 1: a6 has comment
        var a6Node = c3Node.Children[0];
        Assert.Equal("a6", a6Node.San);
        Assert.Contains("Option 1 & the most popular move", a6Node.Comment);

        // Verify Option 2: 4... d6 has variations inside it
        var d6Node = c3Node.Children[1];
        Assert.Equal("d6", d6Node.San);
        Assert.Contains("Option 2: Black plays d6", d6Node.Comment);

        // Verify subvariation with NAG: navigate down to 23. Nxc5 in 11... h6 branch
        // 4... d6 -> 5. d4 -> 6. O-O -> 7. Re1 -> 8. d5 -> 9. Bxd7+ -> 10. Be3 -> 11. c4 -> 11... f5
        var c4Node = d6Node.Children[0] // 5. d4
            .Children[0] // 5... Bd7
            .Children[0] // 6. O-O
            .Children[0] // 6... Bg7
            .Children[0] // 7. Re1
            .Children[0] // 7... Nge7
            .Children[0] // 8. d5
            .Children[0] // 8... Nb8
            .Children[0] // 9. Bxd7+
            .Children[0] // 9... Nxd7
            .Children[0] // 10. Be3
            .Children[0] // 10... O-O
            .Children[0]; // 11. c4

        Assert.Equal("c4", c4Node.San);
        // 11. c4 has 2 continuations: 11... f5 (main variation) and 11... h6 (subvariation)
        Assert.Equal(2, c4Node.Children.Count);
        Assert.Equal("f5", c4Node.Children[0].San);
        Assert.Equal("h6", c4Node.Children[1].San);

        // Follow 11... h6 down to 23. Nxc5
        var curH6 = c4Node.Children[1]; // 11... h6
        while (curH6.Children.Count > 0 && curH6.San != "Nxc5")
        {
            curH6 = curH6.Children[0];
        }
        Assert.Equal("Nxc5", curH6.San);
        Assert.Contains(2, curH6.Nags); // NAG $2 is mistake/error
        Assert.Contains("The wrong decision", curH6.Comment);

        // 23. Nxc5 has alternative 23. a4
        Assert.Equal(2, curH6.Parent!.Children.Count);
        var alt23a4 = curH6.Parent.Children[1];
        Assert.Equal("a4", alt23a4.San);

        // Verify StreamingPgnImporter mainline extraction
        var extractedMainline = StreamingPgnImporter.ExtractMainlineMoveTokens(pgn);
        Assert.Equal(moves, extractedMainline);

        // Verify PgnHandler.ExportPgn round-trip reimport produces the same mainline
        string exported = PgnHandler.ExportPgn(tree);
        Assert.NotEmpty(exported);
        var reimported = PgnHandler.ImportPgn(exported);
        var reimportedMoves = new List<string>();
        var rCur = reimported.Root;
        while (rCur.Children.Count > 0)
        {
            rCur = rCur.Children[0];
            reimportedMoves.Add(rCur.San);
        }
        Assert.Equal(moves, reimportedMoves);
    }
}
