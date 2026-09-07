using System.Net;
using System.Text.RegularExpressions;
using Caissalytics.Core;

namespace Caissalytics.Data;

public class OpponentDossierService : IOpponentDossierService
{
    private readonly IDatabaseService _databaseService;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly Regex FirstMovesRegex = new(
        @"1\.\s*([a-hA-KNRQB1-8\+#\-O]+)(?:\s+([a-hA-KNRQB1-8\+#\-O]+))?(?:\s+2\.\s*([a-hA-KNRQB1-8\+#\-O]+)(?:\s+([a-hA-KNRQB1-8\+#\-O]+))?)?",
        RegexOptions.Compiled);

    public OpponentDossierService(IDatabaseService databaseService, IHttpClientFactory httpClientFactory)
    {
        _databaseService = databaseService;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<OpponentScoutingReport?> GenerateDossierAsync(
        string playerName,
        string? databaseName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerName)) return null;

        var targetDb = databaseName;
        if (string.IsNullOrWhiteSpace(targetDb))
        {
            var activeDb = await _databaseService.GetActiveDatabaseAsync();
            targetDb = activeDb.Name;
        }

        // Search games involving this player
        var filter = new GameFilter
        {
            Player = playerName.Trim(),
            PageNumber = 1,
            PageSize = 2000
        };

        var (games, totalCount) = await _databaseService.SearchGamesAsync(targetDb, filter);

        if (games.Count == 0)
        {
            // If empty in target DB, try reference database if distinct
            var refDb = await _databaseService.GetReferenceDatabaseAsync();
            if (!string.IsNullOrWhiteSpace(refDb) && !string.Equals(refDb, targetDb, StringComparison.OrdinalIgnoreCase))
            {
                var (refGames, refCount) = await _databaseService.SearchGamesAsync(refDb, filter);
                if (refGames.Count > 0)
                {
                    games = refGames;
                    targetDb = refDb;
                }
            }
        }

        if (games.Count == 0) return null;

        string cleanTarget = playerName.Trim();

        var report = new OpponentScoutingReport
        {
            PlayerName = cleanTarget,
            DatabaseSource = targetDb ?? "Default Database",
            TotalGames = games.Count
        };

        var whiteGames = new List<GameHeader>();
        var blackGames = new List<GameHeader>();

        var whiteElos = new List<int>();
        var blackElos = new List<int>();
        var dates = new List<string>();

        int shortWins = 0, shortDraws = 0, shortTotal = 0;
        int medWins = 0, medDraws = 0, medTotal = 0;
        int longWins = 0, longDraws = 0, longTotal = 0;

        foreach (var game in games)
        {
            bool isWhite = game.White.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase);
            bool isBlack = game.Black.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(game.Date) && game.Date != "????.??.??")
            {
                dates.Add(game.Date);
            }

            int fullMoves = game.PlyCount > 0 ? game.PlyCount / 2 : 35;

            if (isWhite)
            {
                whiteGames.Add(game);
                if (game.WhiteElo.HasValue && game.WhiteElo.Value > 0) whiteElos.Add(game.WhiteElo.Value);

                if (game.Result == "1-0")
                {
                    report.WhiteWins++;
                    report.TotalWins++;
                    RecordLengthStat(fullMoves, isWin: true, isDraw: false, ref shortWins, ref shortDraws, ref shortTotal, ref medWins, ref medDraws, ref medTotal, ref longWins, ref longDraws, ref longTotal);
                }
                else if (game.Result == "1/2-1/2")
                {
                    report.WhiteDraws++;
                    report.TotalDraws++;
                    RecordLengthStat(fullMoves, isWin: false, isDraw: true, ref shortWins, ref shortDraws, ref shortTotal, ref medWins, ref medDraws, ref medTotal, ref longWins, ref longDraws, ref longTotal);
                }
                else if (game.Result == "0-1")
                {
                    report.WhiteLosses++;
                    report.TotalLosses++;
                    RecordLengthStat(fullMoves, isWin: false, isDraw: false, ref shortWins, ref shortDraws, ref shortTotal, ref medWins, ref medDraws, ref medTotal, ref longWins, ref longDraws, ref longTotal);
                }
            }
            else if (isBlack)
            {
                blackGames.Add(game);
                if (game.BlackElo.HasValue && game.BlackElo.Value > 0) blackElos.Add(game.BlackElo.Value);

                if (game.Result == "0-1")
                {
                    report.BlackWins++;
                    report.TotalWins++;
                    RecordLengthStat(fullMoves, isWin: true, isDraw: false, ref shortWins, ref shortDraws, ref shortTotal, ref medWins, ref medDraws, ref medTotal, ref longWins, ref longDraws, ref longTotal);
                }
                else if (game.Result == "1/2-1/2")
                {
                    report.BlackDraws++;
                    report.TotalDraws++;
                    RecordLengthStat(fullMoves, isWin: false, isDraw: true, ref shortWins, ref shortDraws, ref shortTotal, ref medWins, ref medDraws, ref medTotal, ref longWins, ref longDraws, ref longTotal);
                }
                else if (game.Result == "1-0")
                {
                    report.BlackLosses++;
                    report.TotalLosses++;
                    RecordLengthStat(fullMoves, isWin: false, isDraw: false, ref shortWins, ref shortDraws, ref shortTotal, ref medWins, ref medDraws, ref medTotal, ref longWins, ref longDraws, ref longTotal);
                }
            }
        }

        report.WhiteGamesCount = whiteGames.Count;
        report.BlackGamesCount = blackGames.Count;

        // Elo calculations
        var allElos = whiteElos.Concat(blackElos).ToList();
        if (allElos.Count > 0)
        {
            report.PeakElo = allElos.Max();
            report.AvgElo = (int)allElos.Average();
            report.CurrentElo = allElos.Last();
        }

        if (dates.Count > 0)
        {
            dates.Sort();
            report.EarliestDate = DateHelper.Format(dates.First());
            report.LatestDate = DateHelper.Format(dates.Last());
        }

        // Game Length Tendencies
        report.LengthTendencies = new GameLengthTendencies
        {
            ShortGamesCount = shortTotal,
            ShortGamesWins = shortWins,
            ShortGamesDraws = shortDraws,
            MediumGamesCount = medTotal,
            MediumGamesWins = medWins,
            MediumGamesDraws = medDraws,
            LongGamesCount = longTotal,
            LongGamesWins = longWins,
            LongGamesDraws = longDraws
        };

        // Determine playing style
        ClassifyPlayingStyle(report);

        // White Repertoire analysis
        report.WhiteRepertoire = AnalyzeWhiteRepertoire(whiteGames);

        // Black Repertoire analysis
        report.BlackRepertoire = AnalyzeBlackRepertoire(blackGames);

        // Detect specific tactical & strategic vulnerabilities
        report.Vulnerabilities = DetectVulnerabilities(report);

        // Recent 30 games
        report.RecentGames = games.Take(30).ToList();

        return report;
    }

    private static void RecordLengthStat(
        int fullMoves,
        bool isWin,
        bool isDraw,
        ref int shortWins, ref int shortDraws, ref int shortTotal,
        ref int medWins, ref int medDraws, ref int medTotal,
        ref int longWins, ref int longDraws, ref int longTotal)
    {
        if (fullMoves < 30)
        {
            shortTotal++;
            if (isWin) shortWins++;
            if (isDraw) shortDraws++;
        }
        else if (fullMoves <= 50)
        {
            medTotal++;
            if (isWin) medWins++;
            if (isDraw) medDraws++;
        }
        else
        {
            longTotal++;
            if (isWin) longWins++;
            if (isDraw) longDraws++;
        }
    }

    private static void ClassifyPlayingStyle(OpponentScoutingReport report)
    {
        int total = report.TotalGames;
        if (total == 0) return;

        double shortPct = (double)report.LengthTendencies.ShortGamesCount / total;
        double longPct = (double)report.LengthTendencies.LongGamesCount / total;

        if (shortPct >= 0.40)
        {
            report.PlayingStyle = "Tactical & Direct";
            report.StyleDescription = "Fierce, concrete playing style. A large percentage of games conclude within 30 moves through early tactical assaults or sharp miniature decisions.";
        }
        else if (longPct >= 0.35)
        {
            report.PlayingStyle = "Endgame Grinder";
            report.StyleDescription = "Patient, technical player with high stamina. Thrives in complex, prolonged maneuvers and technical piece endgames stretching past move 50.";
        }
        else if (report.LengthTendencies.ShortGamesScore > report.LengthTendencies.LongGamesScore + 15)
        {
            report.PlayingStyle = "Dynamic Attacker";
            report.StyleDescription = "Dominant in active, sharp middlegames, but their scoring rate declines noticeably in lengthy, quiet endgames.";
        }
        else
        {
            report.PlayingStyle = "Solid & Classical";
            report.StyleDescription = "Balanced, structured classical player. Shows consistent tactical awareness in the middlegame and dependable endgame fundamentals.";
        }
    }

    private static List<OpponentRepertoireBranch> AnalyzeWhiteRepertoire(List<GameHeader> games)
    {
        var branches = new Dictionary<string, List<GameHeader>>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in games)
        {
            string move1 = ExtractFirstMoveWhite(g.Pgn);
            if (!branches.TryGetValue(move1, out var list))
            {
                list = new List<GameHeader>();
                branches[move1] = list;
            }
            list.Add(g);
        }

        var result = new List<OpponentRepertoireBranch>();
        int total = games.Count;

        foreach (var (move, gameList) in branches.OrderByDescending(kvp => kvp.Value.Count))
        {
            int wins = gameList.Count(g => g.Result == "1-0");
            int draws = gameList.Count(g => g.Result == "1/2-1/2");
            int losses = gameList.Count(g => g.Result == "0-1");

            double freq = total > 0 ? Math.Round(gameList.Count * 100.0 / total, 1) : 0;
            string cat = freq >= 45 ? "Primary Weapon" : (freq >= 15 ? "Secondary Line" : "Occasional Choice");

            var branch = new OpponentRepertoireBranch
            {
                Move = move,
                Category = cat,
                GameCount = gameList.Count,
                FrequencyPct = freq,
                Wins = wins,
                Draws = draws,
                Losses = losses,
                KeyLines = ExtractTopLines(gameList, isOpponentWhite: true)
            };

            result.Add(branch);
        }

        return result;
    }

    private static List<OpponentRepertoireBranch> AnalyzeBlackRepertoire(List<GameHeader> games)
    {
        var branches = new Dictionary<string, List<GameHeader>>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in games)
        {
            string firstExchange = ExtractFirstExchange(g.Pgn);
            if (!branches.TryGetValue(firstExchange, out var list))
            {
                list = new List<GameHeader>();
                branches[firstExchange] = list;
            }
            list.Add(g);
        }

        var result = new List<OpponentRepertoireBranch>();
        int total = games.Count;

        foreach (var (move, gameList) in branches.OrderByDescending(kvp => kvp.Value.Count))
        {
            int wins = gameList.Count(g => g.Result == "0-1");
            int draws = gameList.Count(g => g.Result == "1/2-1/2");
            int losses = gameList.Count(g => g.Result == "1-0");

            double freq = total > 0 ? Math.Round(gameList.Count * 100.0 / total, 1) : 0;
            string cat = freq >= 40 ? "Main Defense" : (freq >= 15 ? "Regular Defense" : "Occasional Sideline");

            var branch = new OpponentRepertoireBranch
            {
                Move = move,
                Category = cat,
                GameCount = gameList.Count,
                FrequencyPct = freq,
                Wins = wins,
                Draws = draws,
                Losses = losses,
                KeyLines = ExtractTopLines(gameList, isOpponentWhite: false)
            };

            result.Add(branch);
        }

        return result;
    }

    private static List<OpponentOpeningLine> ExtractTopLines(List<GameHeader> games, bool isOpponentWhite)
    {
        var grouped = games
            .GroupBy(g => !string.IsNullOrEmpty(g.Eco) ? g.Eco : "Unclassified")
            .OrderByDescending(grp => grp.Count())
            .Take(5);

        var lines = new List<OpponentOpeningLine>();
        foreach (var grp in grouped)
        {
            string eco = grp.Key;
            var sampleGame = grp.First();
            string openingName = OpeningCatalog.ResolveOpeningName(eco, sampleGame.Pgn);

            int wins = isOpponentWhite ? grp.Count(g => g.Result == "1-0") : grp.Count(g => g.Result == "0-1");
            int draws = grp.Count(g => g.Result == "1/2-1/2");
            int losses = isOpponentWhite ? grp.Count(g => g.Result == "0-1") : grp.Count(g => g.Result == "1-0");

            lines.Add(new OpponentOpeningLine
            {
                Eco = eco,
                Name = openingName,
                MoveSequence = ExtractFirstTwoMoves(sampleGame.Pgn),
                GameCount = grp.Count(),
                Wins = wins,
                Draws = draws,
                Losses = losses,
                SampleGameId = sampleGame.Id
            });
        }

        return lines;
    }

    private static List<OpponentVulnerability> DetectVulnerabilities(OpponentScoutingReport report)
    {
        var list = new List<OpponentVulnerability>();

        // 1. White Repertoire Weaknesses
        foreach (var branch in report.WhiteRepertoire.Where(b => b.GameCount >= 2))
        {
            if (branch.ScorePct <= 35)
            {
                list.Add(new OpponentVulnerability
                {
                    Title = $"Low Score with {branch.Move}",
                    Severity = branch.ScorePct <= 25 ? "High" : "Moderate",
                    Description = $"As White with {branch.Move}, scores only {branch.ScorePct}% across {branch.GameCount} games ({branch.Wins}W, {branch.Draws}D, {branch.Losses}L).",
                    Recommendation = $"Prepare aggressive, principled defenses against {branch.Move} to capitalize on their historical struggle."
                });
            }
        }

        // 2. Black Repertoire Weaknesses
        foreach (var branch in report.BlackRepertoire.Where(b => b.GameCount >= 2))
        {
            if (branch.ScorePct <= 35)
            {
                list.Add(new OpponentVulnerability
                {
                    Title = $"Struggles Defending {branch.Move}",
                    Severity = branch.ScorePct <= 25 ? "High" : "Moderate",
                    Description = $"As Black against {branch.Move}, achieves only a {branch.ScorePct}% score across {branch.GameCount} games ({branch.Losses} losses).",
                    Recommendation = $"Guide the opening into {branch.Move} structures to put immediate pressure on their defensive preparation."
                });
            }
        }

        // 3. Time / Length Weaknesses
        var l = report.LengthTendencies;
        if (l.ShortGamesCount >= 3 && l.ShortGamesScore <= 35)
        {
            list.Add(new OpponentVulnerability
            {
                Title = "Tactical Vulnerability in Early Phase",
                Severity = "High",
                Description = $"In games concluding in under 30 moves, their score drops to {l.ShortGamesScore}%.",
                Recommendation = "Aim for concrete, tactical variations with active piece play to challenge their tactical accuracy."
            });
        }
        else if (l.LongGamesCount >= 3 && l.LongGamesScore <= 35)
        {
            list.Add(new OpponentVulnerability
            {
                Title = "Technical Endgame Fatigue",
                Severity = "Moderate",
                Description = $"In long technical games past move 50, their score falls to {l.LongGamesScore}%.",
                Recommendation = "Patiently simplify into advantageous endgames and test their technical endurance."
            });
        }

        // 4. Color Asymmetry
        if (report.WhiteGamesCount >= 3 && report.BlackGamesCount >= 3)
        {
            if (report.WhiteScore >= 60 && report.BlackScore <= 38)
            {
                list.Add(new OpponentVulnerability
                {
                    Title = "Noticeable Color Disparity",
                    Severity = "Noticeable",
                    Description = $"Significantly stronger as White ({report.WhiteScore}%) than as Black ({report.BlackScore}%).",
                    Recommendation = "When playing White, exert sustained pressure; when playing Black, steer toward solid, drawish structures."
                });
            }
        }

        return list;
    }

    private static string ExtractFirstMoveWhite(string pgn)
    {
        if (string.IsNullOrWhiteSpace(pgn)) return "1. e4";
        var m = FirstMovesRegex.Match(pgn);
        if (m.Success && m.Groups[1].Success)
        {
            return $"1. {m.Groups[1].Value}";
        }
        return "1. e4";
    }

    private static string ExtractFirstExchange(string pgn)
    {
        if (string.IsNullOrWhiteSpace(pgn)) return "vs 1. e4: 1... e5";
        var m = FirstMovesRegex.Match(pgn);
        if (m.Success)
        {
            string w1 = m.Groups[1].Value;
            string b1 = m.Groups[2].Success ? m.Groups[2].Value : "";
            if (!string.IsNullOrEmpty(b1))
            {
                return $"vs 1. {w1}: 1... {b1}";
            }
            return $"vs 1. {w1}";
        }
        return "vs 1. e4: 1... e5";
    }

    private static string ExtractFirstTwoMoves(string pgn)
    {
        if (string.IsNullOrWhiteSpace(pgn)) return "";
        var m = FirstMovesRegex.Match(pgn);
        if (m.Success)
        {
            var parts = new List<string>();
            if (m.Groups[1].Success) parts.Add($"1. {m.Groups[1].Value}");
            if (m.Groups[2].Success) parts.Add(m.Groups[2].Value);
            if (m.Groups[3].Success) parts.Add($"2. {m.Groups[3].Value}");
            if (m.Groups[4].Success) parts.Add(m.Groups[4].Value);
            return string.Join(" ", parts);
        }
        return "";
    }

    public async Task<List<string>> SearchKnownPlayersAsync(string query, string? databaseName = null, int limit = 15)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<string>();

        var filter = new GameFilter
        {
            Player = query.Trim(),
            PageNumber = 1,
            PageSize = 40
        };

        var (games, _) = await _databaseService.SearchGamesAsync(databaseName, filter);

        var clean = query.Trim();
        var players = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in games)
        {
            if (g.White.Contains(clean, StringComparison.OrdinalIgnoreCase))
            {
                players.Add(g.White);
            }
            if (g.Black.Contains(clean, StringComparison.OrdinalIgnoreCase))
            {
                players.Add(g.Black);
            }
            if (players.Count >= limit) break;
        }

        return players.OrderBy(p => p).Take(limit).ToList();
    }

    public async Task<OnlineSyncResult> FetchOnlineOpponentGamesAsync(
        string platform,
        string username,
        int maxGames = 50,
        CancellationToken cancellationToken = default)
    {
        var result = new OnlineSyncResult();
        if (string.IsNullOrWhiteSpace(username))
        {
            result.Errors.Add("Username cannot be empty.");
            return result;
        }

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        string pgnText = "";

        if (string.Equals(platform, "Lichess", StringComparison.OrdinalIgnoreCase))
        {
            string url = $"https://lichess.org/api/games/user/{Uri.EscapeDataString(username.Trim())}?max={maxGames}&clocks=false&evals=false&opening=true";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/x-chess-pgn");
            var response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                result.Errors.Add($"Lichess player '{username}' was not found.");
                return result;
            }
            if (!response.IsSuccessStatusCode)
            {
                result.Errors.Add($"Lichess API returned HTTP {(int)response.StatusCode}.");
                return result;
            }

            pgnText = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        else
        {
            // Chess.com
            string archivesUrl = $"https://api.chess.com/pub/player/{Uri.EscapeDataString(username.Trim().ToLowerInvariant())}/games/archives";
            using var req = new HttpRequestMessage(HttpMethod.Get, archivesUrl);
            req.Headers.UserAgent.ParseAdd("Caissalytics-Desktop/1.0");
            var resp = await client.SendAsync(req, cancellationToken);

            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                result.Errors.Add($"Chess.com player '{username}' was not found.");
                return result;
            }

            string json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("archives", out var archives) && archives.GetArrayLength() > 0)
            {
                string lastArchive = archives[archives.GetArrayLength() - 1].GetString()!;
                string pgnUrl = $"{lastArchive}/pgn";
                using var pgnReq = new HttpRequestMessage(HttpMethod.Get, pgnUrl);
                pgnReq.Headers.UserAgent.ParseAdd("Caissalytics-Desktop/1.0");
                var pgnResp = await client.SendAsync(pgnReq, cancellationToken);
                if (pgnResp.IsSuccessStatusCode)
                {
                    pgnText = await pgnResp.Content.ReadAsStringAsync(cancellationToken);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(pgnText))
        {
            var activeDb = await _databaseService.GetActiveDatabaseAsync();
            var progress = new Progress<PgnImportProgress>(p =>
            {
                result.TotalImported = p.GamesSaved;
                result.TotalSkipped = p.GamesSkipped;
            });

            await _databaseService.ImportPgnTextAsync(activeDb.Name, pgnText, progress, cancellationToken, deduplicate: true);
        }

        return result;
    }
}
