using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Caissalytics.Core;
using Microsoft.Data.Sqlite;

namespace Caissalytics.Data;

public class StreamingPgnImporter
{
    private const int BatchSize = 500;

    public async Task ImportAsync(
        string connectionString,
        TextReader reader,
        long totalBytes = 0,
        IProgress<PgnImportProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool deduplicate = false)
    {
        var stopwatch = Stopwatch.StartNew();
        int gamesParsed = 0;
        int gamesSaved = 0;
        int gamesSkipped = 0;
        long bytesProcessed = 0;

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Optimize SQLite for bulk writes
        using (var pragmaCmd = connection.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
            await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        SqliteTransaction? transaction = connection.BeginTransaction();
        var insertGameCmd = connection.CreateCommand();
        insertGameCmd.Transaction = transaction;
        insertGameCmd.CommandText = @"
            INSERT INTO games (white, black, white_elo, black_elo, result, date, event, site, round, eco, ply_count, pgn)
            VALUES ($white, $black, $white_elo, $black_elo, $result, $date, $event, $site, $round, $eco, $ply_count, $pgn);
            SELECT last_insert_rowid();";

        var pWhite = insertGameCmd.Parameters.Add("$white", SqliteType.Text);
        var pBlack = insertGameCmd.Parameters.Add("$black", SqliteType.Text);
        var pWhiteElo = insertGameCmd.Parameters.Add("$white_elo", SqliteType.Integer);
        var pBlackElo = insertGameCmd.Parameters.Add("$black_elo", SqliteType.Integer);
        var pResult = insertGameCmd.Parameters.Add("$result", SqliteType.Text);
        var pDate = insertGameCmd.Parameters.Add("$date", SqliteType.Text);
        var pEvent = insertGameCmd.Parameters.Add("$event", SqliteType.Text);
        var pSite = insertGameCmd.Parameters.Add("$site", SqliteType.Text);
        var pRound = insertGameCmd.Parameters.Add("$round", SqliteType.Text);
        var pEco = insertGameCmd.Parameters.Add("$eco", SqliteType.Text);
        var pPlyCount = insertGameCmd.Parameters.Add("$ply_count", SqliteType.Integer);
        var pPgn = insertGameCmd.Parameters.Add("$pgn", SqliteType.Text);

        var insertPosCmd = connection.CreateCommand();
        insertPosCmd.Transaction = transaction;
        insertPosCmd.CommandText = @"
            INSERT OR IGNORE INTO positions (game_id, ply, zobrist_key, next_move_san, next_move_uci, result)
            VALUES ($game_id, $ply, $zobrist_key, $next_move_san, $next_move_uci, $result);";

        var ppGameId = insertPosCmd.Parameters.Add("$game_id", SqliteType.Integer);
        var ppPly = insertPosCmd.Parameters.Add("$ply", SqliteType.Integer);
        var ppZobrist = insertPosCmd.Parameters.Add("$zobrist_key", SqliteType.Integer);
        var ppMoveSan = insertPosCmd.Parameters.Add("$next_move_san", SqliteType.Text);
        var ppMoveUci = insertPosCmd.Parameters.Add("$next_move_uci", SqliteType.Text);
        var ppResult = insertPosCmd.Parameters.Add("$result", SqliteType.Text);

        var checkExistsCmd = connection.CreateCommand();
        checkExistsCmd.Transaction = transaction;
        checkExistsCmd.CommandText = "SELECT 1 FROM games WHERE site = $site LIMIT 1;";
        var pCheckSite = checkExistsCmd.Parameters.Add("$site", SqliteType.Text);

        var headerLines = new List<string>();
        var movetext = new StringBuilder();
        bool readingMovetext = false;
        string? line;

        try
        {
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bytesProcessed += Encoding.UTF8.GetByteCount(line) + 1;

                string trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    if (readingMovetext)
                    {
                        movetext.Append(' ');
                    }
                    continue;
                }

                if (trimmed.StartsWith('['))
                {
                    if (readingMovetext)
                    {
                        // We reached the start of a new game while reading movetext
                        await ProcessSingleGameAsync();
                        headerLines.Clear();
                        movetext.Clear();
                        readingMovetext = false;
                    }

                    headerLines.Add(trimmed);
                }
                else
                {
                    readingMovetext = true;
                    movetext.Append(' ');
                    movetext.Append(trimmed);
                }
            }

            // Process any trailing game
            if (headerLines.Count > 0 || movetext.Length > 0)
            {
                await ProcessSingleGameAsync();
            }

            // Commit final batch
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
                transaction.Dispose();
                transaction = null;
            }

            progress?.Report(new PgnImportProgress
            {
                GamesParsed = gamesParsed,
                GamesSaved = gamesSaved,
                GamesSkipped = gamesSkipped,
                BytesProcessed = bytesProcessed,
                TotalBytes = Math.Max(totalBytes, bytesProcessed),
                CurrentStage = "Complete",
                Elapsed = stopwatch.Elapsed
            });
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                transaction.Dispose();
            }
            throw;
        }

        async Task ProcessSingleGameAsync()
        {
            var headers = ParseHeaders(headerLines);
            string movesStr = movetext.ToString().Trim();
            if (headers.Count == 0 && string.IsNullOrWhiteSpace(movesStr))
                return;

            gamesParsed++;

            // Extract game result
            string result = headers.GetValueOrDefault("Result", "*");
            if (result == "*" && (movesStr.EndsWith("1-0") || movesStr.EndsWith("0-1") || movesStr.EndsWith("1/2-1/2")))
            {
                if (movesStr.EndsWith("1-0")) result = "1-0";
                else if (movesStr.EndsWith("0-1")) result = "0-1";
                else if (movesStr.EndsWith("1/2-1/2")) result = "1/2-1/2";
            }

            // Normalize canonical game URL (for Lichess and Chess.com)
            string site = headers.GetValueOrDefault("Site", "");
            if (string.IsNullOrWhiteSpace(site) || site == "Chess.com" || site == "?")
            {
                if (headers.TryGetValue("Link", out var link) && !string.IsNullOrWhiteSpace(link))
                {
                    site = link;
                }
            }

            // Deduplication check: skip if game URL already exists
            if (deduplicate && !string.IsNullOrWhiteSpace(site))
            {
                pCheckSite.Value = site;
                var exists = await checkExistsCmd.ExecuteScalarAsync(cancellationToken);
                if (exists != null && Convert.ToInt64(exists) > 0)
                {
                    gamesSkipped++;
                    return;
                }
            }

            // Build full PGN text for game storage
            var rawPgn = new StringBuilder();
            foreach (var hl in headerLines) rawPgn.AppendLine(hl);
            if (rawPgn.Length > 0) rawPgn.AppendLine();
            rawPgn.AppendLine(movesStr);

            // Set game parameters
            pWhite.Value = headers.GetValueOrDefault("White", "White");
            pBlack.Value = headers.GetValueOrDefault("Black", "Black");
            pWhiteElo.Value = int.TryParse(headers.GetValueOrDefault("WhiteElo", ""), out int we) ? we : DBNull.Value;
            pBlackElo.Value = int.TryParse(headers.GetValueOrDefault("BlackElo", ""), out int be) ? be : DBNull.Value;
            pResult.Value = result;
            pDate.Value = headers.GetValueOrDefault("Date", "????.??.??");
            pEvent.Value = headers.GetValueOrDefault("Event", "");
            pSite.Value = site;
            pRound.Value = headers.GetValueOrDefault("Round", "");
            pEco.Value = headers.GetValueOrDefault("ECO", "");
            pPgn.Value = rawPgn.ToString();

            // Parse moves and extract positions
            var moveTokens = ExtractMainlineMoveTokens(movesStr);
            pPlyCount.Value = moveTokens.Count;

            long gameId = (long)(await insertGameCmd.ExecuteScalarAsync(cancellationToken) ?? 0L);
            gamesSaved++;

            // Trace positions and record Zobrist hashes
            string? startFen = headers.TryGetValue("SetUp", out var setup) && setup == "1" && headers.TryGetValue("FEN", out var fenVal)
                ? fenVal
                : null;

            BoardPosition pos;
            try
            {
                pos = FenParser.Parse(startFen ?? BoardPosition.StartFen);
            }
            catch
            {
                pos = FenParser.Parse(BoardPosition.StartFen);
            }

            int ply = 0;
            ppGameId.Value = gameId;
            ppResult.Value = result;

            foreach (var token in moveTokens)
            {
                var move = SanParser.ParseSan(pos, token);
                if (move.IsEmpty)
                {
                    break;
                }

                ppPly.Value = ply;
                ppZobrist.Value = unchecked((long)pos.ZobristKey);
                ppMoveSan.Value = token;
                ppMoveUci.Value = move.ToUci();

                await insertPosCmd.ExecuteNonQueryAsync(cancellationToken);

                pos = MoveGenerator.ApplyMove(pos, move);
                ply++;
            }

            // Commit batch if limit reached
            if (gamesSaved % BatchSize == 0)
            {
                await transaction!.CommitAsync(cancellationToken);
                transaction.Dispose();

                transaction = connection.BeginTransaction();
                insertGameCmd.Transaction = transaction;
                insertPosCmd.Transaction = transaction;
                checkExistsCmd.Transaction = transaction;

                progress?.Report(new PgnImportProgress
                {
                    GamesParsed = gamesParsed,
                    GamesSaved = gamesSaved,
                    GamesSkipped = gamesSkipped,
                    BytesProcessed = bytesProcessed,
                    TotalBytes = Math.Max(totalBytes, bytesProcessed),
                    CurrentStage = "Importing",
                    Elapsed = stopwatch.Elapsed
                });

                await Task.Yield();
            }
        }
    }

    private static Dictionary<string, string> ParseHeaders(List<string> lines)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            int firstQuote = line.IndexOf('"');
            int lastQuote = line.LastIndexOf('"');
            if (line.StartsWith('[') && firstQuote > 1 && lastQuote > firstQuote)
            {
                string key = line.Substring(1, firstQuote - 1).Trim();
                string val = line.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                dict[key] = val;
            }
        }
        return dict;
    }

    public static List<string> ExtractMainlineMoveTokens(string text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return tokens;

        int len = text.Length;
        int i = 0;
        int ravDepth = 0;

        while (i < len)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // Skip comments { ... }
            if (c == '{')
            {
                int end = text.IndexOf('}', i);
                i = end == -1 ? len : end + 1;
                continue;
            }

            // Skip headers [ ... ]
            if (c == '[')
            {
                int end = text.IndexOf(']', i);
                i = end == -1 ? len : end + 1;
                continue;
            }

            // Skip variations ( ... )
            if (c == '(')
            {
                ravDepth++;
                i++;
                continue;
            }
            if (c == ')')
            {
                if (ravDepth > 0) ravDepth--;
                i++;
                continue;
            }

            // If inside variation, ignore token
            if (ravDepth > 0)
            {
                // Consume token until next whitespace or bracket
                while (i < len && !char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != ')' && text[i] != '{' && text[i] != '[')
                {
                    i++;
                }
                continue;
            }

            // Read token
            int start = i;
            while (i < len && !char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != ')' && text[i] != '{' && text[i] != '[')
            {
                i++;
            }
            string token = text.Substring(start, i - start);

            // Skip NAGs: "$1", "$14"
            if (token.StartsWith('$'))
                continue;

            // Skip game results
            if (token == "1-0" || token == "0-1" || token == "1/2-1/2" || token == "*")
                continue;

            // Strip leading move number if glued: "1.e4" -> "e4", "1...d5" -> "d5", "23.Nf3" -> "Nf3"
            token = Regex.Replace(token, @"^\d+\.+", "");

            // Skip bare move numbers: "1.", "1...", "23"
            if (string.IsNullOrWhiteSpace(token) || token.EndsWith('.') || Regex.IsMatch(token, @"^\d+\.*$"))
                continue;

            // Clean trailing move evaluations
            token = token.TrimEnd(';', '!', '?');
            if (string.IsNullOrWhiteSpace(token))
                continue;

            tokens.Add(token);
        }

        return tokens;
    }
}
