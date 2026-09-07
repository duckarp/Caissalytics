using Caissalytics.Core;

namespace Caissalytics.Engine;

public class GameAnalysisService : IGameAnalysisService
{
    private readonly IEngineService _engineService;

    public GameAnalysisService(IEngineService engineService)
    {
        _engineService = engineService;
    }

    public async Task<GameAnalysisReport> AnalyzeGameAsync(
        GameTree tree,
        GameAnalysisOptions options,
        IProgress<GameAnalysisProgress>? progress = null,
        CancellationToken ct = default)
    {
        var engine = await _engineService.GetActiveEngineAsync();
        if (engine == null || !engine.IsInstalled || string.IsNullOrEmpty(engine.ExecutablePath))
        {
            throw new InvalidOperationException("Stockfish engine is not installed or available. Please install it from the Dashboard.");
        }

        // Collect mainline nodes
        var mainlineNodes = new List<MoveNode>();
        var curr = tree.Root;
        while (curr.Children.Count > 0)
        {
            var next = curr.Children[0];
            mainlineNodes.Add(next);
            curr = next;
        }

        if (mainlineNodes.Count == 0)
        {
            return new GameAnalysisReport();
        }

        // Collect all positions: root (pos 0), followed by position after each mainline move
        var positions = new List<BoardPosition> { tree.Root.Position };
        positions.AddRange(mainlineNodes.Select(n => n.Position));

        using var client = new UciProcessClient();
        bool started = await client.StartEngineAsync(engine.ExecutablePath);
        if (!started)
        {
            throw new InvalidOperationException("Failed to start engine process for game analysis.");
        }

        var evals = new EngineEvaluationLine?[positions.Count];
        var plies = new List<PlyAnalysis>();
        int totalPlies = mainlineNodes.Count;

        for (int i = 0; i < positions.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            string fen = FenParser.ToFen(positions[i]);
            var eval = await client.EvaluatePositionAsync(fen, options.MoveTimeMs, options.MaxDepth, ct);
            evals[i] = eval;

            if (i > 0)
            {
                int plyIndex = i - 1;
                var node = mainlineNodes[plyIndex];
                var evalBefore = evals[i - 1];
                var evalAfter = eval;

                double winBefore = evalBefore?.WhiteWinPercentage ?? 50.0;
                double winAfter = evalAfter?.WhiteWinPercentage ?? 50.0;

                bool isWhite = plyIndex % 2 == 0;
                double playerWinBefore = isWhite ? winBefore : 100.0 - winBefore;
                double playerWinAfter = isWhite ? winAfter : 100.0 - winAfter;
                double winLoss = Math.Max(0.0, playerWinBefore - playerWinAfter);

                string bestMoveSan = "";
                var bestLineSans = new List<string>();

                if (evalBefore != null && evalBefore.PvMoves.Count > 0)
                {
                    var tempPos = positions[i - 1];
                    foreach (var uci in evalBefore.PvMoves.Take(5))
                    {
                        var m = ParseUciMove(uci, tempPos);
                        if (m.IsEmpty) break;
                        string san = SanParser.ToSan(tempPos, m);
                        bestLineSans.Add(san);
                        if (string.IsNullOrEmpty(bestMoveSan))
                        {
                            bestMoveSan = san;
                        }
                        tempPos = MoveGenerator.ApplyMove(tempPos, m);
                    }
                }

                bool isEngineBest = !string.IsNullOrEmpty(evalBefore?.BestMove) &&
                                    string.Equals(node.Move.ToUci(), evalBefore.BestMove, StringComparison.OrdinalIgnoreCase);

                MoveClassification classification;
                string? nagGlyph = null;
                int? nagNumber = null;

                if (isEngineBest || winLoss < 1.0)
                {
                    classification = MoveClassification.Best;
                }
                else if (winLoss < 3.5)
                {
                    classification = MoveClassification.Excellent;
                }
                else if (winLoss < 7.0)
                {
                    classification = MoveClassification.Good;
                }
                else if (winLoss < 15.0)
                {
                    classification = MoveClassification.Inaccuracy;
                    nagGlyph = "?!";
                    nagNumber = 6;
                }
                else if (winLoss < 25.0)
                {
                    classification = MoveClassification.Mistake;
                    nagGlyph = "?";
                    nagNumber = 2;
                }
                else
                {
                    classification = MoveClassification.Blunder;
                    nagGlyph = "??";
                    nagNumber = 4;
                }

                var ply = new PlyAnalysis
                {
                    Ply = plyIndex,
                    MoveSan = node.San,
                    Move = node.Move,
                    FenBefore = FenParser.ToFen(positions[i - 1]),
                    FenAfter = FenParser.ToFen(positions[i]),
                    CentipawnsBefore = evalBefore?.Centipawns,
                    MateBefore = evalBefore?.MateInMoves,
                    CentipawnsAfter = evalAfter?.Centipawns,
                    MateAfter = evalAfter?.MateInMoves,
                    WinRateBefore = winBefore,
                    WinRateAfter = winAfter,
                    WinRateLoss = winLoss,
                    Classification = classification,
                    NagGlyph = nagGlyph,
                    NagNumber = nagNumber,
                    BestMoveSan = bestMoveSan,
                    BestLineMoves = bestLineSans,
                    FormattedScoreAfter = evalAfter?.FormattedScore ?? "0.00"
                };

                plies.Add(ply);

                progress?.Report(new GameAnalysisProgress
                {
                    CurrentPly = i,
                    TotalPlies = totalPlies,
                    CurrentMoveSan = node.San,
                    LatestPly = ply
                });
            }
        }

        var whitePlies = plies.Where(p => p.IsWhiteMove).ToList();
        var blackPlies = plies.Where(p => !p.IsWhiteMove).ToList();

        var report = new GameAnalysisReport
        {
            Plies = plies,
            WhiteAccuracy = CalculateAccuracy(whitePlies),
            BlackAccuracy = CalculateAccuracy(blackPlies),
            WhiteAcpl = CalculateAcpl(whitePlies),
            BlackAcpl = CalculateAcpl(blackPlies),

            WhiteBestCount = whitePlies.Count(p => p.Classification == MoveClassification.Best),
            WhiteExcellentCount = whitePlies.Count(p => p.Classification == MoveClassification.Excellent),
            WhiteGoodCount = whitePlies.Count(p => p.Classification == MoveClassification.Good),
            WhiteInaccuracyCount = whitePlies.Count(p => p.Classification == MoveClassification.Inaccuracy),
            WhiteMistakeCount = whitePlies.Count(p => p.Classification == MoveClassification.Mistake),
            WhiteBlunderCount = whitePlies.Count(p => p.Classification == MoveClassification.Blunder),

            BlackBestCount = blackPlies.Count(p => p.Classification == MoveClassification.Best),
            BlackExcellentCount = blackPlies.Count(p => p.Classification == MoveClassification.Excellent),
            BlackGoodCount = blackPlies.Count(p => p.Classification == MoveClassification.Good),
            BlackInaccuracyCount = blackPlies.Count(p => p.Classification == MoveClassification.Inaccuracy),
            BlackMistakeCount = blackPlies.Count(p => p.Classification == MoveClassification.Mistake),
            BlackBlunderCount = blackPlies.Count(p => p.Classification == MoveClassification.Blunder)
        };

        return report;
    }

    public void AnnotateGameTree(GameTree tree, GameAnalysisReport report)
    {
        var mainlineNodes = new List<MoveNode>();
        var curr = tree.Root;
        while (curr.Children.Count > 0)
        {
            var next = curr.Children[0];
            mainlineNodes.Add(next);
            curr = next;
        }

        foreach (var ply in report.Plies)
        {
            if (ply.Ply >= mainlineNodes.Count) break;
            var node = mainlineNodes[ply.Ply];

            if (ply.NagNumber.HasValue)
            {
                if (!node.Nags.Contains(ply.NagNumber.Value))
                {
                    node.Nags.Add(ply.NagNumber.Value);
                }
            }

            string evalComment = $"[{ply.FormattedScoreAfter}]";
            if (string.IsNullOrEmpty(node.Comment))
            {
                node.Comment = evalComment;
            }
            else if (!node.Comment.Contains("["))
            {
                node.Comment = $"{evalComment} {node.Comment}";
            }

            if ((ply.Classification == MoveClassification.Blunder || ply.Classification == MoveClassification.Mistake)
                && ply.BestLineMoves.Count > 0 && node.Parent != null)
            {
                string firstBestSan = ply.BestLineMoves[0];
                bool alreadyExists = node.Parent.Children.Skip(1).Any(c => c.San == firstBestSan);
                if (!alreadyExists)
                {
                    var parentPos = node.Parent.Position;
                    var firstMove = SanParser.ParseSan(parentPos, firstBestSan);
                    if (!firstMove.IsEmpty)
                    {
                        var nextPos = MoveGenerator.ApplyMove(parentPos, firstMove);
                        var varNode = new MoveNode(firstMove, firstBestSan, nextPos, node.Parent);
                        varNode.Nags.Add(1);
                        varNode.Comment = $"Best was {firstBestSan}";
                        node.Parent.Children.Add(varNode);

                        var currVar = varNode;
                        var currVarPos = nextPos;
                        for (int m = 1; m < ply.BestLineMoves.Count; m++)
                        {
                            string subSan = ply.BestLineMoves[m];
                            var subMove = SanParser.ParseSan(currVarPos, subSan);
                            if (subMove.IsEmpty) break;
                            var subNextPos = MoveGenerator.ApplyMove(currVarPos, subMove);
                            var subNode = new MoveNode(subMove, subSan, subNextPos, currVar);
                            currVar.Children.Add(subNode);
                            currVar = subNode;
                            currVarPos = subNextPos;
                        }
                    }
                }
            }
        }
    }

    public static Move ParseUciMove(string uci, BoardPosition pos)
    {
        if (string.IsNullOrWhiteSpace(uci) || uci.Length < 4)
            return Move.Empty;

        var from = SquareExtensions.Parse(uci.Substring(0, 2));
        var to = SquareExtensions.Parse(uci.Substring(2, 2));
        if (from == Square.None || to == Square.None)
            return Move.Empty;

        var legalMoves = MoveGenerator.GenerateLegalMoves(pos);
        if (uci.Length >= 5)
        {
            char promoChar = char.ToLowerInvariant(uci[4]);
            var promoType = promoChar switch
            {
                'q' => PieceType.Queen,
                'r' => PieceType.Rook,
                'b' => PieceType.Bishop,
                'n' => PieceType.Knight,
                _ => PieceType.None
            };
            return legalMoves.FirstOrDefault(m => m.From == from && m.To == to && m.Promotion == promoType);
        }

        return legalMoves.FirstOrDefault(m => m.From == from && m.To == to);
    }

    private static double CalculateAccuracy(List<PlyAnalysis> plies)
    {
        if (plies.Count == 0) return 100.0;
        double sum = 0.0;
        foreach (var p in plies)
        {
            double plyAcc = Math.Clamp(100.0 * Math.Exp(-0.025 * p.WinRateLoss), 0.0, 100.0);
            sum += plyAcc;
        }
        return Math.Round(sum / plies.Count, 1);
    }

    private static double CalculateAcpl(List<PlyAnalysis> plies)
    {
        if (plies.Count == 0) return 0.0;
        double sumCpLoss = 0.0;
        int count = 0;
        foreach (var p in plies)
        {
            if (p.CentipawnsBefore.HasValue && p.CentipawnsAfter.HasValue)
            {
                double cpBefore = p.CentipawnsBefore.Value;
                double cpAfter = p.CentipawnsAfter.Value;
                double playerBefore = p.IsWhiteMove ? cpBefore : -cpBefore;
                double playerAfter = p.IsWhiteMove ? cpAfter : -cpAfter;
                double loss = Math.Max(0.0, playerBefore - playerAfter);
                sumCpLoss += Math.Min(1000.0, loss);
                count++;
            }
        }
        return count > 0 ? Math.Round(sumCpLoss / count, 1) : 0.0;
    }
}
