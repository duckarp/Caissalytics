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
            Fen = "8/4k3/8/8/4K3/4P3/8/8 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "When the pawn is on the 3rd rank, seizing the key squares (d5, e5, f5) two ranks in front of the pawn ensures promotion. Advance the king to take opposition and breach the defensive blockade.",
            CoachingTip = "Advance your King to seize opposition or occupy key squares! 1. Ke5! seizes direct opposition, while 1. Kd5 or 1. Kf5 occupies the key squares to guarantee promotion.",
            KeySquares = new() { "d5", "e5", "f5" },
            BenchmarkMoves = "1. Ke5 Kd7 2. Kf6 Ke8 3. Ke6"
        },
        new EndgamePosition
        {
            Id = "kp_distant_opposition",
            Title = "Distant Opposition",
            Subtitle = "Stepping onto the same file with odd number of squares",
            Category = EndgameCategory.Pawns,
            Difficulty = EndgameDifficulty.Intermediate,
            Fen = "8/4k3/8/8/8/4K3/4P3/8 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "To win against an active defending king, White maintains the odd-square distance (distant opposition) to step into close opposition on the critical files.",
            CoachingTip = "White holds distant opposition on the e-file with 3 squares between kings. Advance with 1. Ke4! (or Kd4/Kf4). If Black meets you with 1... Ke6, use the pawn tempo 2. e3! to force Black to concede ground.",
            KeySquares = new() { "e4", "d5", "f5" },
            BenchmarkMoves = "1. Ke4 Ke6 2. e3 Kd6 3. Kf5"
        },
        new EndgamePosition
        {
            Id = "kp_rook_pawn_draw",
            Title = "The Rook Pawn Trap (Wrong Corner)",
            Subtitle = "Why 'a' and 'h' pawns are notoriously drawish",
            Category = EndgameCategory.Pawns,
            Difficulty = EndgameDifficulty.Beginner,
            Fen = "7k/8/5K2/7P/8/8/8/8 b - - 0 1",
            PlayerColor = PieceColor.Black,
            TargetOutcome = "Draw",
            Description = "A defending King positioned in front of an 'a' or 'h' pawn guarantees a draw by stalemate as long as they never voluntarily leave the corner file.",
            CoachingTip = "Hold the corner! Shuffle your king between h8 and h7/g8. If White pushes h6 and h7, retreat to h8. White cannot advance the king to support promotion without producing stalemate!",
            KeySquares = new() { "h8", "h7", "g8" },
            BenchmarkMoves = "1... Kh7 2. Kg5 Kg7 3. h6+ Kh7 4. Kh5 Kh8 5. Kg6 Kg8 6. h7+ Kh8"
        },
        new EndgamePosition
        {
            Id = "kp_trebuchet",
            Title = "The Trebuchet (Mutual Zugzwang)",
            Subtitle = "Holding the mutual zugzwang fortress",
            Category = EndgameCategory.Pawns,
            Difficulty = EndgameDifficulty.Intermediate,
            Fen = "8/8/8/3k4/3p4/3P4/3K4/8 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Draw",
            Description = "The Trebuchet is the most famous mutual zugzwang in pawn endings. In this standoff, neither side can rush the opposing pawn without losing their own.",
            CoachingTip = "Maintain defensive patience! Keep your king patrolling d2, e2, or c2 to protect d3 and keep Black's king locked out. A rash step to e3 or c3 would allow Black to counter-attack.",
            KeySquares = new() { "d2", "e2", "c2", "d3" },
            BenchmarkMoves = "1. Ke2 Ke6 2. Kf3 Kf5"
        },
        new EndgamePosition
        {
            Id = "kp_triangulation",
            Title = "King Triangulation & Outflanking",
            Subtitle = "Wasting a tempo to force the defender to concede ground",
            Category = EndgameCategory.Pawns,
            Difficulty = EndgameDifficulty.Advanced,
            Fen = "8/8/8/p1k5/Pp6/1P6/3K4/8 b - - 1 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "White has successfully triangulated to d2, passing the move to Black. Black's king must yield ground, allowing White to outflank and capture the queenside pawns.",
            CoachingTip = "Black's king is forced to move away from c5 or advance. When Black moves, march your king to c2 and b2 to infiltrate the weak pawns!",
            KeySquares = new() { "c2", "b2", "a5", "b4" },
            BenchmarkMoves = "1... Kd4 2. Kc2 Ke3 3. Kc1 Kd3 4. Kb2"
        },
        new EndgamePosition
        {
            Id = "kp_square_rule",
            Title = "The Rule of the Square",
            Subtitle = "Calculating whether a king can catch a runaway pawn",
            Category = EndgameCategory.Pawns,
            Difficulty = EndgameDifficulty.Beginner,
            Fen = "7k/8/8/8/1p6/8/8/4K3 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Draw",
            Description = "Visualize a square from the pawn to its promotion square. If the defending king can step into the square on its turn, it will catch the pawn in time.",
            CoachingTip = "Count the squares to the promotion rank (b1) — it is 3 steps away. The square extends from b4 to e4 to e1. Play 1. Kd2! (or Kd1/Ke2) stepping inside the square to catch and eliminate the pawn!",
            KeySquares = new() { "d2", "c2", "b2", "b1" },
            BenchmarkMoves = "1. Kd2 b3 2. Kc3 b2 3. Kxb2"
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
            Fen = "4K3/4P3/4k3/8/8/8/r7/3R4 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "The pawn is on the 7th rank with the defending king cut off. White's objective is to build a bridge with the rook on the 4th rank to shield the king from vertical checks.",
            CoachingTip = "Play 1. Re1+! to cut Black's king off further away to d6. Then play 2. Rd4! building the bridge on the 4th rank to intercept checks when White's king marches out!",
            KeySquares = new() { "e1", "d4", "f7", "e7" },
            BenchmarkMoves = "1. Re1+ Kd6 2. Rd4+ Kc6 3. Kf7 Rf2+ 4. Ke6 Re2+ 5. Re4"
        },
        new EndgamePosition
        {
            Id = "rook_philidor",
            Title = "The Philidor Defense (3rd Rank Cut-off)",
            Subtitle = "The cornerstone defense in rook and pawn endings",
            Category = EndgameCategory.Rooks,
            Difficulty = EndgameDifficulty.Intermediate,
            Fen = "4k3/R7/1r6/4K3/4P3/8/8/8 b - - 0 1",
            PlayerColor = PieceColor.Black,
            TargetOutcome = "Draw",
            Description = "Black keeps their rook on the 6th rank (Rb6/Rh6) to prevent the enemy king from advancing to e6. Once the enemy pawn pushes forward, the defending rook drops to the rear to check endlessly.",
            CoachingTip = "Hold the 6th rank! Play 1... Rh6! to keep White's king locked out. As soon as White pushes e5, immediately drop your rook to the 1st rank (...Rh1) to deliver endless checks from behind.",
            KeySquares = new() { "b6", "h6", "b1", "h1" },
            BenchmarkMoves = "1... Rh6 2. Kd5 Rg6 3. e5 Rb6"
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
            Fen = "5k2/R7/4P3/8/8/8/1r6/6K1 b - - 0 1",
            PlayerColor = PieceColor.Black,
            TargetOutcome = "Draw",
            Description = "When a center or bishop pawn advances to the 6th rank, the defending king must stay on the short side (e.g. g8/f8 for an e-pawn) so the defending rook has maximum distance (long side: a1/b1) to deliver checks.",
            CoachingTip = "Deliver checks from the long side! Play 1... Rb1+! forcing White's king out, and keep checking vertically. The white king cannot approach the rook without abandoning the e6 pawn.",
            KeySquares = new() { "b1", "b2", "f8", "e6" },
            BenchmarkMoves = "1... Rb1+ 2. Kf2 Rb2+ 3. Ke3 Rb3+"
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
            Fen = "8/8/8/8/8/4K2Q/3p4/3k4 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "Unlike rook and bishop pawns on the 7th rank, center pawns cannot stalemate. The winning technique is to give repeated checks, force the defending king onto the promotion square in front of its pawn, and gain a tempo to march your king.",
            CoachingTip = "Pin and check! Play 1. Qf1+ (or 1. Qh1+). Force Black's king onto the promotion square d1, then gain a tempo to march your king in with Kd3 to deliver checkmate!",
            KeySquares = new() { "d1", "d3", "e2", "d2" },
            BenchmarkMoves = "1. Qf1+ Kc2 2. Qd3+ Kc1 3. Qxd2+ Kb1 4. Kd3"
        },
        new EndgamePosition
        {
            Id = "queen_vs_pawn_c_draw",
            Title = "Queen vs Bishop Pawn on 7th Rank",
            Subtitle = "The stalemate fortress defense",
            Category = EndgameCategory.Queens,
            Difficulty = EndgameDifficulty.Advanced,
            Fen = "6K1/3Q4/8/8/8/8/2p5/1k6 w - - 0 1",
            PlayerColor = PieceColor.Black,
            TargetOutcome = "Draw",
            Description = "A bishop pawn (c or f) on the 7th rank supported by its king draws against a queen if the attacking king is too far away. The defending king dives into the corner (a1), creating an unbreakable stalemate trap.",
            CoachingTip = "Defend against the queen! Dive towards the corner (a1/a2) whenever White checks. Never block your pawn voluntarily unless forced, because capturing or pinning produces an immediate stalemate draw!",
            KeySquares = new() { "a1", "b1", "c1", "c2" },
            BenchmarkMoves = "1. Qb7+ Ka1 2. Qa6+ Kb1 3. Qb6+ Ka1"
        },
        new EndgamePosition
        {
            Id = "queen_vs_rook_philidor",
            Title = "Queen vs Rook (Philidor Technique)",
            Subtitle = "Driving the defender into zugzwang to win the rook",
            Category = EndgameCategory.Queens,
            Difficulty = EndgameDifficulty.Master,
            Fen = "4k3/r7/4K3/3Q4/8/8/8/8 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "Queen vs Rook is a forced win, but requires precise triangulation and geometric fork threats to dislodge the defending rook without allowing a perpetual or stalemate tactic.",
            CoachingTip = "Look for cross-board queen checks (1. Qd6! or 1. Qb5+) to force the defending rook away from the king, winning it via a double attack fork.",
            KeySquares = new() { "d6", "b5", "c6", "a7" },
            BenchmarkMoves = "1. Qd6 Ra8 2. Qc6+ Kf8 3. Qxa8+"
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
            Fen = "2k5/8/1KB5/8/5N2/8/8/8 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "Checkmate can only be forced in a corner of the same color as the bishop. When the enemy king flees toward the wrong corner, the knight's W-maneuver systematically drives it out.",
            CoachingTip = "The bishop controls light squares, the knight seals dark escape squares. Play 1. Ne6! trapping Black's king on the back rank and driving it toward the light corner.",
            KeySquares = new() { "e6", "d7", "c7", "a6", "c6" },
            BenchmarkMoves = "1. Ne6 Kb8 2. Bd7 Ka8 3. Nc7+ Kb8 4. Na6+ Ka8 5. Bc6#"
        },
        new EndgamePosition
        {
            Id = "minor_wrong_bishop",
            Title = "Wrong Bishop & Rook Pawn",
            Subtitle = "An unbreakable drawing fortress",
            Category = EndgameCategory.MinorPieces,
            Difficulty = EndgameDifficulty.Beginner,
            Fen = "7k/8/6KP/8/8/8/4B3/8 b - - 0 1",
            PlayerColor = PieceColor.Black,
            TargetOutcome = "Draw",
            Description = "Even with an extra bishop and passed pawn, if the bishop cannot control the promotion square (a light bishop with an h-pawn / h8 is dark), the defender cannot be dislodged from the corner.",
            CoachingTip = "Stay on h8! Since White has a light-squared bishop (e2) and h8 is dark, White can never deliver check on h8. Black simply shuffles between h8 and g8 for a stalemate draw.",
            KeySquares = new() { "h8", "g8" },
            BenchmarkMoves = "1... Kg8 2. Bc4+ Kh8 3. Kh5 Kh7"
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
            Description = "Opposite-colored bishops are notoriously drawish. By establishing a blockade on the opposite color complex, the defending bishop can sacrifice itself for the pawn or blockade indefinitely.",
            CoachingTip = "Trade the bishops with 1... Bxe3! followed by capturing White's passed pawn (2. Ke2 Bd4 3. Kd3 Bxe5), leaving bare kings for an immediate draw.",
            KeySquares = new() { "e3", "d4", "e5" },
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
            Fen = "r7/5k2/P7/3K4/8/8/8/R7 w - - 0 1",
            PlayerColor = PieceColor.White,
            TargetOutcome = "Win",
            Description = "Siegbert Tarrasch formulated that the rook belongs behind passed pawns: behind your own to protect it as it advances, or behind your opponent's to attack it as it flees.",
            CoachingTip = "Keep the rook active behind the passed pawn! Play 1. a7! (or 1. Kc6). With the rook defending the pawn from behind, White's king marches to b7 to assist promotion while Black's rook is tied down.",
            KeySquares = new() { "a1", "a7", "c6", "b7" },
            BenchmarkMoves = "1. a7 Ke7 2. Kc6 Kd8 3. Kb7"
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
            CoachingTip = "Bring your king forward! Use the rook to cut off the enemy king while your king marches in to deliver checkmate.",
            KeySquares = new() { "d2", "d3", "e3", "e4" },
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
