using Caissalytics.Core;

namespace Caissalytics.Data;

public static class EndgameCurriculum
{
    private static readonly List<EndgamePosition> _positions = new()
    {
        // -------------------------------------------------------------------------
        // 1. KING & PAWN ENDGAMES
        // -------------------------------------------------------------------------
        new EndgamePosition
        {
            Id = "kp_key_squares_direct",
            Title = "Key Squares & Direct Opposition",
            Subtitle = "King and Pawn vs King fundamental endgame",
            Category = EndgameCategory.Pawns,
            Difficulty = EndgameDifficulty.Beginner,
            Fen = "8/8/8/4k3/8/4P3/4K3/8 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "When the pawn is on the 3rd rank, reaching the key squares (d5, e5, f5) two ranks in front of the pawn ensures promotion regardless of who has the opposition.",
            CoachingTip = "Advance your King first to seize opposition or control the key squares before pushing the pawn! 1. Kd3 or 1. Kf3 wins, but pushing the pawn immediately can compromise your advantage.",
            KeySquares = new() { "d5", "e5", "f5" },
            BenchmarkMoves = "1. Kd3 Kd5 2. e4+ Ke5 3. Ke3"
        },
        new EndgamePosition
        {
            Id = "kp_distant_opposition",
            Title = "Distant Opposition",
            Subtitle = "Stepping onto the same file with odd number of squares",
            Category = EndgameCategory.Pawns,
            Difficulty = EndgameDifficulty.Intermediate,
            Fen = "8/4k3/8/8/8/8/4K3/4P3 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "To win against an active defending king, White must maintain the odd-square distance (distant opposition) to step into close opposition on the critical files.",
            CoachingTip = "Play 1. Ke3! taking distant opposition on the e-file. If Black plays 1... Ke6, play 2. Ke4 seizing direct opposition.",
            KeySquares = new() { "e3", "e4", "d5", "f5" },
            BenchmarkMoves = "1. Ke3 Ke6 2. Ke4"
        },
        new EndgamePosition
        {
            Id = "kp_rook_pawn_draw",
            Title = "The Rook Pawn Trap (Wrong Corner)",
            Subtitle = "Why 'a' and 'h' pawns are notoriously drawish",
            Category = EndgameCategory.Pawns,
            Difficulty = EndgameDifficulty.Beginner,
            Fen = "7k/7P/8/8/8/8/8/6K1 b - - 0 1",
            PlayerColor = PieceColor.Black,
            TargetOutcome = "Draw",
            Description = "A defending King trapped in front of an 'a' or 'h' pawn guarantees a draw by stalemate as long as they never voluntarily leave the corner.",
            CoachingTip = "Hold the corner! Black simply cycles between h8 and g7/g8. White has no way to drive the King out without stalemating.",
            KeySquares = new() { "h8", "g8", "g7" },
            BenchmarkMoves = "1... Kxh7"
        },
        new EndgamePosition
        {
            Id = "kp_trebuchet",
            Title = "The Trebuchet (Mutual Zugzwang)",
            Subtitle = "Whoever moves first collapses",
            Category = EndgameCategory.Pawns,
            Difficulty = EndgameDifficulty.Intermediate,
            Fen = "8/8/8/3k4/3p4/3P4/3K4/8 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "The Trebuchet is the most famous mutual zugzwang in pawn endings. The king that defends its own pawn must yield if it is their turn to move.",
            CoachingTip = "Play 1. Ke2! Ke5 2. Ke1! Kd5 3. Kd1! - triangulate so Black is forced to play ...Ke5 when White's king lands on d2.",
            KeySquares = new() { "e2", "d2", "d3", "d4" },
            BenchmarkMoves = "1. Ke2 Ke5 2. Ke1"
        },
        new EndgamePosition
        {
            Id = "kp_triangulation",
            Title = "King Triangulation & Outflanking",
            Subtitle = "Wasting a tempo to force the defender to concede ground",
            Category = EndgameCategory.Pawns,
            Difficulty = EndgameDifficulty.Advanced,
            Fen = "8/8/8/p1k5/Pp6/1P1K4/8/8 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "White's king must reach d5 or c4, but Black currently holds the defending square d5. By marching around a triangle of squares (d2-e2-d3), White wastes a tempo.",
            CoachingTip = "Notice how Black's king only has c6 and d5 to defend, whereas White's king can step to e3/e2/d2. Triangulate to pass the turn to Black!",
            KeySquares = new() { "d2", "e2", "e3", "d3" },
            BenchmarkMoves = "1. Ke3 Kd5 2. Kd3 Kc6 3. Kc4"
        },
        new EndgamePosition
        {
            Id = "kp_square_rule",
            Title = "The Rule of the Square",
            Subtitle = "Calculating whether a king can catch a runaway pawn",
            Category = EndgameCategory.Pawns,
            Difficulty = EndgameDifficulty.Beginner,
            Fen = "8/8/8/8/1p6/8/8/k3K3 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Draw",
            Description = "Visualize a square from the pawn to its promotion square. If the defending king can step into the square on its turn, it will catch the pawn in time.",
            CoachingTip = "Count the squares to the promotion rank (b1) — it is 3 steps away. The square extends from b4 to e4 to e1. Play 1. Kd2! stepping into the square.",
            KeySquares = new() { "d2", "c2", "b2", "b1" },
            BenchmarkMoves = "1. Kd2 b3 2. Kc3 b2"
        },

        // -------------------------------------------------------------------------
        // 2. ROOK ENDGAMES
        // -------------------------------------------------------------------------
        new EndgamePosition
        {
            Id = "rook_lucena",
            Title = "The Lucena Position (Building the Bridge)",
            Subtitle = "The universal blueprint for winning rook endgames",
            Category = EndgameCategory.Rooks,
            Difficulty = EndgameDifficulty.Intermediate,
            Fen = "1K1k4/1P6/8/8/8/8/8/2r5 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "The pawn is on the 7th rank with the defending king cut off. White's objective is to build a bridge with the rook to shield the king from endless vertical checks.",
            CoachingTip = "Play 1. Ra4! (or 1. Rd4+ followed by moving the rook to the 4th rank). When the king steps out to a7 or c7, the rook on the 4th rank intercepts the vertical check with Rc4/Ra4!",
            KeySquares = new() { "a4", "b4", "c4", "b7" },
            BenchmarkMoves = "1. Ra4 Kd7 2. Ka7 Ra1+ 3. Kb6 Rb1+ 4. Ka6 Kc7 5. Rc4+ Kd7 6. Rc5"
        },
        new EndgamePosition
        {
            Id = "rook_philidor",
            Title = "The Philidor Defense (3rd Rank Cut-off)",
            Subtitle = "The cornerstone defense in rook and pawn endings",
            Category = EndgameCategory.Rooks,
            Difficulty = EndgameDifficulty.Intermediate,
            Fen = "4k3/8/r3K3/4P3/8/8/8/7R b - - 0 1",
            PlayerColor = PieceColor.Black,
            TargetOutcome = "Draw",
            Description = "Black keeps their rook on the 6th rank (or 3rd rank if flipped) to prevent the enemy king from advancing. Once the enemy pawn pushes forward, the defending rook drops to the rear to check endlessly.",
            CoachingTip = "Watch out for checkmate threats like Rh8#! Play 1... Kf8 (or 1... Kd8) to give the king an escape square, and prepare ...Ra1 to check from behind once the pawn pushes to e6.",
            KeySquares = new() { "f8", "a1", "e6" },
            BenchmarkMoves = "1... Kf8 2. Rh8+ Kg7 3. Re8 Ra1"
        },
        new EndgamePosition
        {
            Id = "rook_vancura",
            Title = "The Vancura Defense",
            Subtitle = "Active side-checking defense against a rook pawn",
            Category = EndgameCategory.Rooks,
            Difficulty = EndgameDifficulty.Advanced,
            Fen = "7k/R7/8/P7/8/8/6r1/K7 b - - 0 1",
            PlayerColor = PieceColor.Black,
            TargetOutcome = "Draw",
            Description = "When the opponent has an 'a' or 'h' passed pawn and your king is trapped on the back rank, position your rook on the 6th rank (f6/g6) to deliver relentless horizontal flank checks whenever the enemy king steps out.",
            CoachingTip = "Play 1... Rg6! patrolling the 6th rank. If White's king leaves the pawn to shelter, you immediately check from the side (Rf6+). If the pawn pushes, you attack it from behind.",
            KeySquares = new() { "g6", "f6", "a6" },
            BenchmarkMoves = "1... Rg6 2. a6 Kg8 3. Ra8+ Kf7"
        },
        new EndgamePosition
        {
            Id = "rook_short_side",
            Title = "Short-Side Defense",
            Subtitle = "King on the short side, rook on the long side",
            Category = EndgameCategory.Rooks,
            Difficulty = EndgameDifficulty.Advanced,
            Fen = "5k2/R7/4P3/8/8/8/8/1r4K1 b - - 0 1",
            PlayerColor = PieceColor.Black,
            TargetOutcome = "Draw",
            Description = "When a center or bishop pawn advances to the 6th rank, the defending king must stay on the short side (e.g. g8/f8 for an e-pawn) so the defending rook has maximum distance (long side: a1/b1) to deliver checks.",
            CoachingTip = "Deliver checks from the long side! 1... Rb6 or keep checking with 1... Re1+. The white king cannot approach the rook without abandoning the e6 pawn.",
            KeySquares = new() { "b1", "b6", "e1", "f8" },
            BenchmarkMoves = "1... Re1+ 2. Kf2 Rxe6"
        },

        // -------------------------------------------------------------------------
        // 3. QUEEN ENDGAMES
        // -------------------------------------------------------------------------
        new EndgamePosition
        {
            Id = "queen_vs_pawn_center",
            Title = "Queen vs 7th-Rank Center Pawn",
            Subtitle = "The systematic zigzag maneuver to bring the king",
            Category = EndgameCategory.Queens,
            Difficulty = EndgameDifficulty.Intermediate,
            Fen = "8/8/8/8/8/4K3/3p4/3k3Q w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "Unlike rook and bishop pawns on the 7th rank, center pawns cannot stalemate. The winning technique is to give repeated checks, force the defending king onto the promotion square in front of its pawn, and gain a tempo to march your king.",
            CoachingTip = "Play 1. Qh5+! or 1. Qf3+ Ke1 2. Qe2# (or pinning 1. Qd5+ Ke1 2. Qa5). Force Black's king onto d1, then march White's king with Kd3!",
            KeySquares = new() { "d1", "d3", "e2", "d2" },
            BenchmarkMoves = "1. Qf3+ Kc2 2. Qe4+ Kc1 3. Qc4+ Kd1 4. Kd3"
        },
        new EndgamePosition
        {
            Id = "queen_vs_pawn_c_draw",
            Title = "Queen vs Bishop Pawn on 7th Rank",
            Subtitle = "The stalemate fortress defense",
            Category = EndgameCategory.Queens,
            Difficulty = EndgameDifficulty.Advanced,
            Fen = "8/8/8/8/8/4K3/2p5/1k5Q w - - 0 1",
            PlayerColor = PieceColor.Black,
            TargetOutcome = "Draw",
            Description = "A bishop pawn (c or f) on the 7th rank supported by its king draws against a queen if the attacking king is too far away. The defending king dives into the corner (a1), creating an unbreakable stalemate trap.",
            CoachingTip = "If White plays 1. Qb7+, play 1... Ka1! or Ka2. Never block your pawn voluntarily unless forced; dive towards the corner where capturing or pinning produces stalemate!",
            KeySquares = new() { "a1", "c1", "c2" },
            BenchmarkMoves = "1... Ka1 2. Qa8+ Kb2 3. Qb7+ Ka1"
        },
        new EndgamePosition
        {
            Id = "queen_vs_rook_philidor",
            Title = "Queen vs Rook (Philidor Technique)",
            Subtitle = "Driving the defender into zugzwang to win the rook",
            Category = EndgameCategory.Queens,
            Difficulty = EndgameDifficulty.Master,
            Fen = "8/8/8/8/8/1k6/1r6/K1Q5 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "Queen vs Rook is a forced win, but requires precise triangulation and geometric fork threats to dislodge the defending rook without allowing a perpetual or stalemate tactic.",
            CoachingTip = "Look for cross-board queen checks that simultaneously attack the enemy king and threaten an unprotected rook on the 2nd rank.",
            KeySquares = new() { "d1", "e4", "b2" },
            BenchmarkMoves = "1. Qd1+ Ka3 2. Qd6+ Ka4 3. Qa6+ Kb4 4. Qb6+"
        },

        // -------------------------------------------------------------------------
        // 4. MINOR PIECE ENDGAMES
        // -------------------------------------------------------------------------
        new EndgamePosition
        {
            Id = "minor_bn_mate",
            Title = "Bishop and Knight Checkmate",
            Subtitle = "The legendary W-maneuver to force checkmate in the correct corner",
            Category = EndgameCategory.MinorPieces,
            Difficulty = EndgameDifficulty.Master,
            Fen = "k7/8/1KB5/8/5N2/8/8/8 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "Checkmate can only be forced in a corner of the same color as the bishop. When the enemy king flees toward the wrong corner, the knight's W-maneuver (e.g. f7-d6-e4-c5) systematically drives it out.",
            CoachingTip = "The bishop controls light squares, the knight seals the dark escape squares. Deliver 1. Nd5! or 1. Ne6! preventing the king from escaping toward the center.",
            KeySquares = new() { "c7", "d6", "e4", "c5", "a8" },
            BenchmarkMoves = "1. Ne6 Kb8 2. Bd7 Ka8 3. Nc7+ Kb8 4. Na6+ Ka8 5. Bc6#"
        },
        new EndgamePosition
        {
            Id = "minor_wrong_bishop",
            Title = "Wrong Bishop & Rook Pawn",
            Subtitle = "An unbreakable drawing fortress",
            Category = EndgameCategory.MinorPieces,
            Difficulty = EndgameDifficulty.Beginner,
            Fen = "7k/7P/8/8/8/8/k1K5/2B5 b - - 0 1",
            PlayerColor = PieceColor.Black,
            TargetOutcome = "Draw",
            Description = "Even with an extra bishop and passed pawn, if the bishop cannot control the promotion square (a light bishop with an h-pawn / h8 is dark), the defender cannot be dislodged from the corner.",
            CoachingTip = "Stay on h8! Since the bishop is on c1 (light squares), it can never deliver check on h8. Black has an impenetrable stalemate draw.",
            KeySquares = new() { "h8", "g8" },
            BenchmarkMoves = "1... Kxh7"
        },
        new EndgamePosition
        {
            Id = "minor_opposite_bishops",
            Title = "Opposite-Colored Bishops Fortress",
            Subtitle = "Constructing a blockade on the opposite color complex",
            Category = EndgameCategory.MinorPieces,
            Difficulty = EndgameDifficulty.Intermediate,
            Fen = "8/4k3/8/4P3/8/4B3/8/4K1b1 b - - 0 1",
            PlayerColor = PieceColor.Black,
            TargetOutcome = "Draw",
            Description = "Opposite-colored bishops are the most drawish endings in chess. By establishing a blockade on the dark squares, the attacking light-squared bishop cannot assist in the breakthrough.",
            CoachingTip = "Trade the bishops if possible, or blockade the passed pawn with the king and bishop on the opposite color!",
            KeySquares = new() { "e6", "f7", "g1" },
            BenchmarkMoves = "1... Bxe3 2. Ke2 Bd4 3. Kd3 Bxe5"
        },

        // -------------------------------------------------------------------------
        // 5. PRACTICAL TOURNAMENT ENDGAMES
        // -------------------------------------------------------------------------
        new EndgamePosition
        {
            Id = "practical_tarrasch",
            Title = "Tarrasch's Rule in Practice",
            Subtitle = "Rooks belong behind passed pawns — both yours and your opponent's",
            Category = EndgameCategory.Practical,
            Difficulty = EndgameDifficulty.Intermediate,
            Fen = "8/8/8/3P4/8/3k4/8/1R2K2r w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "Siegbert Tarrasch formulated that the rook should be placed behind passed pawns: behind your own to protect it as it advances, or behind your opponent's to attack it as it flees.",
            CoachingTip = "Keep the rook active behind the pawn! Avoid passive frontal or side blocks when rear placement gives infinite scope.",
            KeySquares = new() { "b1", "b8", "d5", "d8" },
            BenchmarkMoves = "1. Kf2 Rxb1 2. d6"
        },
        new EndgamePosition
        {
            Id = "practical_capablanca",
            Title = "Capablanca's Active King Maneuver",
            Subtitle = "The king is a mighty offensive piece in the endgame",
            Category = EndgameCategory.Practical,
            Difficulty = EndgameDifficulty.Advanced,
            Fen = "8/8/8/8/3k4/8/4K3/4R3 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "Jose Raul Capablanca emphasized that in the endgame, the king transforms from a vulnerable liability into an aggressive fighting piece. Centralize and dominate the key files.",
            CoachingTip = "Bring your king forward! Active piece coordination triumphs over material parity.",
            KeySquares = new() { "e3", "d3", "e4" },
            BenchmarkMoves = "1. Kd2 Kc4 2. Re4+ Kd5 3. Kd3"
        }
    };

    public static IReadOnlyList<EndgamePosition> AllPositions => _positions;

    public static EndgamePosition? GetById(string id)
    {
        return _positions.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<EndgamePosition> GetByCategory(EndgameCategory category)
    {
        return _positions.Where(p => p.Category == category).ToList();
    }
}
