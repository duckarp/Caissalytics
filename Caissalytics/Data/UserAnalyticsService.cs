using System.Globalization;

namespace Caissalytics.Data;

public class UserAnalyticsService : IUserAnalyticsService
{
    private readonly IDatabaseService _databaseService;
    private readonly IUserProfileService _profileService;

    public event Action? OnAnalyticsRefreshed;

    public UserAnalyticsService(
        IDatabaseService databaseService,
        IUserProfileService profileService)
    {
        _databaseService = databaseService;
        _profileService = profileService;

        _databaseService.OnDatabaseModified += _ => NotifyRefreshed();
        _profileService.OnProfileChanged += NotifyRefreshed;
    }

    private void NotifyRefreshed()
    {
        OnAnalyticsRefreshed?.Invoke();
    }

    public async Task<PersonalAnalyticsReport> GenerateAnalyticsReportAsync(
        string? databaseScope = null,
        UserProfile? profile = null,
        AnalyticsFilterOptions? filter = null)
    {
        profile ??= await _profileService.GetProfileAsync();
        filter ??= new AnalyticsFilterOptions();

        // 1. Determine Scope
        string scope = databaseScope ?? "";
        if (string.IsNullOrWhiteSpace(scope))
        {
            var dbs = await _databaseService.GetDatabasesAsync();
            var onlineDb = dbs.FirstOrDefault(d => string.Equals(d.Name, "My online games", StringComparison.OrdinalIgnoreCase));
            if (onlineDb != null && onlineDb.GameCount > 0)
            {
                scope = onlineDb.Name;
            }
            else
            {
                var activeDb = await _databaseService.GetActiveDatabaseAsync();
                scope = activeDb.Name;
            }
        }

        var report = new PersonalAnalyticsReport
        {
            Profile = profile,
            DatabaseScope = scope
        };

        // 2. Fetch Games
        var allHeaders = await _databaseService.GetAllGameHeadersAsync(scope);
        if (allHeaders.Count == 0)
        {
            return report;
        }

        // 3. Filter to games where user played
        var userGames = new List<(GameHeader Game, bool IsWhite, string Result, int? UserRating, int? OpponentRating, string OpponentName, DateTime? ParsedDate)>();

        DateTime? timeFilterCutoff = filter.TimeFilter switch
        {
            "30d" => DateTime.UtcNow.AddDays(-30),
            "90d" => DateTime.UtcNow.AddDays(-90),
            "year" => DateTime.UtcNow.AddDays(-365),
            _ => null
        };

        foreach (var g in allHeaders)
        {
            bool isWhite = profile.MatchesPlayer(g.White);
            bool isBlack = !isWhite && profile.MatchesPlayer(g.Black);

            if (!isWhite && !isBlack)
            {
                continue;
            }

            // Color filter
            if (filter.ColorFilter == "white" && !isWhite) continue;
            if (filter.ColorFilter == "black" && !isBlack) continue;

            // Platform filter
            if (filter.PlatformFilter == "lichess" && g.Platform != "lichess") continue;
            if (filter.PlatformFilter == "chesscom" && g.Platform != "chesscom") continue;
            if (filter.PlatformFilter == "otb" && (g.Platform == "lichess" || g.Platform == "chesscom")) continue;

            // Date parsing
            DateTime? parsedDate = ParseGameDate(g.Date);
            if (timeFilterCutoff.HasValue)
            {
                if (!parsedDate.HasValue || parsedDate.Value < timeFilterCutoff.Value)
                {
                    continue;
                }
            }

            // Determine outcome from user's perspective
            string outcome = "draw";
            if (g.Result == "1-0")
            {
                outcome = isWhite ? "win" : "loss";
            }
            else if (g.Result == "0-1")
            {
                outcome = isBlack ? "win" : "loss";
            }
            else if (g.Result == "1/2-1/2")
            {
                outcome = "draw";
            }
            else
            {
                outcome = "unknown";
            }

            int? userRating = isWhite ? g.WhiteElo : g.BlackElo;
            int? opponentRating = isWhite ? g.BlackElo : g.WhiteElo;
            string opponentName = isWhite ? g.Black : g.White;

            userGames.Add((g, isWhite, outcome, userRating, opponentRating, opponentName, parsedDate));
        }

        if (userGames.Count == 0)
        {
            return report;
        }

        // 4. Calculate Hero Metrics & Color Performance
        var userRatings = new List<int>();
        var opponentRatings = new List<int>();

        foreach (var item in userGames)
        {
            if (item.Result == "unknown") continue;

            report.TotalGames++;
            if (item.Result == "win") report.Wins++;
            else if (item.Result == "draw") report.Draws++;
            else if (item.Result == "loss") report.Losses++;

            if (item.IsWhite)
            {
                report.WhiteStats.TotalGames++;
                if (item.Result == "win") report.WhiteStats.Wins++;
                else if (item.Result == "draw") report.WhiteStats.Draws++;
                else if (item.Result == "loss") report.WhiteStats.Losses++;
                if (item.UserRating.HasValue && item.UserRating > 0) userRatings.Add(item.UserRating.Value);
                if (item.OpponentRating.HasValue && item.OpponentRating > 0) opponentRatings.Add(item.OpponentRating.Value);
            }
            else
            {
                report.BlackStats.TotalGames++;
                if (item.Result == "win") report.BlackStats.Wins++;
                else if (item.Result == "draw") report.BlackStats.Draws++;
                else if (item.Result == "loss") report.BlackStats.Losses++;
                if (item.UserRating.HasValue && item.UserRating > 0) userRatings.Add(item.UserRating.Value);
                if (item.OpponentRating.HasValue && item.OpponentRating > 0) opponentRatings.Add(item.OpponentRating.Value);
            }

            // Platform counts
            if (item.Game.Platform == "lichess") report.PlatformLichessGames++;
            else if (item.Game.Platform == "chesscom") report.PlatformChessComGames++;
            else report.PlatformOtherGames++;
        }

        if (userRatings.Count > 0)
        {
            report.AverageUserRating = (int)Math.Round(userRatings.Average());
        }
        if (opponentRatings.Count > 0)
        {
            report.AverageOpponentRating = (int)Math.Round(opponentRatings.Average());
        }

        // 5. Streaks (chronological evaluation)
        // Sort oldest -> newest for streaks and ratings
        var chronologicalGames = userGames
            .Where(u => u.Result != "unknown")
            .OrderBy(u => u.ParsedDate ?? DateTime.MinValue)
            .ThenBy(u => u.Game.Id)
            .ToList();

        int currentStreakCount = 0;
        string currentStreakType = "";
        int bestStreak = 0;
        int runningStreak = 0;

        foreach (var item in chronologicalGames)
        {
            if (item.Result == "win")
            {
                runningStreak++;
                if (runningStreak > bestStreak) bestStreak = runningStreak;
            }
            else
            {
                runningStreak = 0;
            }
        }
        report.BestWinStreak = bestStreak;

        // Current streak from newest backwards
        for (int i = chronologicalGames.Count - 1; i >= 0; i--)
        {
            var res = chronologicalGames[i].Result;
            if (i == chronologicalGames.Count - 1)
            {
                currentStreakType = res;
                currentStreakCount = 1;
            }
            else if (res == currentStreakType)
            {
                currentStreakCount++;
            }
            else
            {
                break;
            }
        }

        report.CurrentStreak = currentStreakType switch
        {
            "win" => $"+{currentStreakCount} Wins",
            "loss" => $"-{currentStreakCount} Losses",
            "draw" => $"{currentStreakCount} Draws",
            _ => "0"
        };

        // 6. Rating History Timeline
        int? peak = null;
        int? low = null;
        int? latestRating = null;

        foreach (var item in chronologicalGames)
        {
            if (item.UserRating.HasValue && item.UserRating.Value > 0)
            {
                int r = item.UserRating.Value;
                latestRating = r;
                if (!peak.HasValue || r > peak.Value) peak = r;
                if (!low.HasValue || r < low.Value) low = r;

                string openingName = OpeningCatalog.ResolveOpeningName(item.Game.Eco, item.Game.Pgn);

                report.RatingHistory.Add(new RatingHistoryPoint
                {
                    GameId = item.Game.Id,
                    Date = item.ParsedDate,
                    DateStr = item.Game.Date,
                    Rating = r,
                    OpponentRating = item.OpponentRating,
                    OpponentName = item.OpponentName,
                    UserColor = item.IsWhite ? "white" : "black",
                    Result = item.Result,
                    Platform = item.Game.Platform,
                    Eco = item.Game.Eco,
                    OpeningName = openingName
                });
            }
        }

        report.CurrentRating = latestRating;
        report.PeakRating = peak;
        report.LowestRating = low;

        // Group by platform for independent rating timelines
        var platformGroups = report.RatingHistory.GroupBy(p => p.Platform);
        foreach (var pGroup in platformGroups)
        {
            string pId = string.IsNullOrWhiteSpace(pGroup.Key) ? "otb" : pGroup.Key;
            string pName = pId switch
            {
                "lichess" => "Lichess",
                "chesscom" => "Chess.com",
                _ => "OTB / Local"
            };

            var pts = pGroup.ToList();
            if (pts.Count > 0)
            {
                report.PlatformRatings.Add(new PlatformRatingOverview
                {
                    PlatformId = pId,
                    PlatformName = pName,
                    CurrentRating = pts.Last().Rating,
                    PeakRating = pts.Max(p => p.Rating),
                    LowestRating = pts.Min(p => p.Rating),
                    Points = pts
                });
            }
        }
        report.PlatformRatings = report.PlatformRatings.OrderByDescending(p => p.Points.Count).ToList();

        // 7. Opening Repertoire Matrix
        var openingGroups = userGames
            .Where(u => u.Result != "unknown")
            .GroupBy(u =>
            {
                string opening = OpeningCatalog.ResolveOpeningName(u.Game.Eco, u.Game.Pgn);
                string eco = string.IsNullOrWhiteSpace(u.Game.Eco) ? "???" : u.Game.Eco.Trim().ToUpperInvariant();
                return (Eco: eco, Name: opening);
            });

        foreach (var group in openingGroups)
        {
            int total = group.Count();
            int wins = group.Count(g => g.Result == "win");
            int draws = group.Count(g => g.Result == "draw");
            int losses = group.Count(g => g.Result == "loss");

            bool hasWhite = group.Any(g => g.IsWhite);
            bool hasBlack = group.Any(g => !g.IsWhite);
            string color = (hasWhite && hasBlack) ? "Both" : hasWhite ? "White" : "Black";

            report.TopOpenings.Add(new OpeningPerformanceStat
            {
                Eco = group.Key.Eco,
                OpeningName = group.Key.Name,
                TotalGames = total,
                Wins = wins,
                Draws = draws,
                Losses = losses,
                Color = color
            });
        }

        report.TopOpenings = report.TopOpenings
            .OrderByDescending(o => o.TotalGames)
            .ThenByDescending(o => o.WinRate)
            .ToList();

        // 8. Recent Games (Newest first)
        var recentList = userGames
            .OrderByDescending(u => u.ParsedDate ?? DateTime.MinValue)
            .ThenByDescending(u => u.Game.Id)
            .Take(25);

        foreach (var item in recentList)
        {
            string openingName = OpeningCatalog.ResolveOpeningName(item.Game.Eco, item.Game.Pgn);

            report.RecentGames.Add(new PersonalGameSummary
            {
                GameId = item.Game.Id,
                Date = item.Game.Date,
                UserColor = item.IsWhite ? "white" : "black",
                UserRating = item.UserRating,
                OpponentRating = item.OpponentRating,
                OpponentName = item.OpponentName,
                Result = item.Result,
                Eco = item.Game.Eco,
                OpeningName = openingName,
                Platform = item.Game.Platform,
                PlyCount = item.Game.PlyCount,
                Header = item.Game
            });
        }

        return report;
    }

    private static DateTime? ParseGameDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;

        string clean = dateStr.Trim().Replace("?", "1").Replace("/", ".");
        if (DateTime.TryParseExact(clean, "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt;
        }

        if (DateTime.TryParse(clean, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtFallback))
        {
            return dtFallback;
        }

        return null;
    }
}
