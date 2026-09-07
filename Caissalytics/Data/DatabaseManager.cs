using System.Text;
using Caissalytics.Core;
using Microsoft.Data.Sqlite;

namespace Caissalytics.Data;

public class DatabaseManager : IDatabaseService
{
    private readonly string _storageDir;
    private string _activeDatabaseName = "ClassicalMasters";
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly StreamingPgnImporter _importer = new();

    public event Action? OnActiveDatabaseChanged;
    public event Action<string>? OnDatabaseModified;

    public DatabaseManager()
    {
        _storageDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "Caissalytics", "databases");

        Directory.CreateDirectory(_storageDir);
        _ = EnsureDefaultDatabaseAsync();
    }

    public DatabaseManager(string customStorageDir)
    {
        _storageDir = customStorageDir;
        Directory.CreateDirectory(_storageDir);
    }

    private string GetDbPath(string dbName)
    {
        string safeName = SanitizeDatabaseName(dbName);
        return Path.Combine(_storageDir, $"{safeName}.db");
    }

    private static string SanitizeDatabaseName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Default";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }
        string sanitized = sb.ToString().Trim();
        if (sanitized.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
        {
            sanitized = sanitized[..^3];
        }
        return string.IsNullOrEmpty(sanitized) ? "Default" : sanitized;
    }

    public async Task EnsureDefaultDatabaseAsync()
    {
        await _lock.WaitAsync();
        try
        {
            string defaultPath = GetDbPath(_activeDatabaseName);
            if (!File.Exists(defaultPath))
            {
                await InitializeSchemaAsync(defaultPath);
                await SeedSampleGamesAsync(defaultPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DatabaseManager] Failed initializing default DB: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<DatabaseInfo>> GetDatabasesAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var list = new List<DatabaseInfo>();
            var files = Directory.GetFiles(_storageDir, "*.db");

            foreach (var file in files)
            {
                var fi = new FileInfo(file);
                string name = Path.GetFileNameWithoutExtension(file);
                int gameCount = 0;

                try
                {
                    using var conn = new SqliteConnection($"Data Source={file};Mode=ReadOnly");
                    await conn.OpenAsync();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM games;";
                    gameCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }
                catch
                {
                    // If DB is locked or corrupted, keep count as 0
                }

                list.Add(new DatabaseInfo
                {
                    Name = name,
                    FilePath = file,
                    SizeBytes = fi.Length,
                    GameCount = gameCount,
                    CreatedAt = fi.CreationTimeUtc,
                    LastModified = fi.LastWriteTimeUtc,
                    IsActive = string.Equals(name, _activeDatabaseName, StringComparison.OrdinalIgnoreCase)
                });
            }

            return list.OrderByDescending(d => d.IsActive).ThenBy(d => d.Name).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<DatabaseInfo> GetActiveDatabaseAsync()
    {
        var dbs = await GetDatabasesAsync();
        var active = dbs.FirstOrDefault(d => d.IsActive);
        if (active != null) return active;

        if (dbs.Count > 0)
        {
            _activeDatabaseName = dbs[0].Name;
            dbs[0].IsActive = true;
            return dbs[0];
        }

        return await CreateDatabaseAsync(_activeDatabaseName);
    }

    public Task SetActiveDatabaseAsync(string name)
    {
        string safeName = SanitizeDatabaseName(name);
        if (!string.Equals(_activeDatabaseName, safeName, StringComparison.OrdinalIgnoreCase))
        {
            _activeDatabaseName = safeName;
            OnActiveDatabaseChanged?.Invoke();
        }
        return Task.CompletedTask;
    }

    public async Task<DatabaseInfo> CreateDatabaseAsync(string name)
    {
        string safeName = SanitizeDatabaseName(name);
        string path = GetDbPath(safeName);

        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(path))
            {
                await InitializeSchemaAsync(path);
            }
            _activeDatabaseName = safeName;
            OnActiveDatabaseChanged?.Invoke();
        }
        finally
        {
            _lock.Release();
        }

        var fi = new FileInfo(path);
        return new DatabaseInfo
        {
            Name = safeName,
            FilePath = path,
            SizeBytes = fi.Exists ? fi.Length : 0,
            GameCount = 0,
            CreatedAt = fi.Exists ? fi.CreationTimeUtc : DateTime.UtcNow,
            LastModified = fi.Exists ? fi.LastWriteTimeUtc : DateTime.UtcNow,
            IsActive = true
        };
    }

    public async Task<bool> DeleteDatabaseAsync(string name)
    {
        string safeName = SanitizeDatabaseName(name);
        string path = GetDbPath(safeName);

        await _lock.WaitAsync();
        try
        {
            // Clear SQLite connection pools for this file
            SqliteConnection.ClearAllPools();

            if (File.Exists(path)) File.Delete(path);
            string walPath = $"{path}-wal";
            if (File.Exists(walPath)) File.Delete(walPath);
            string shmPath = $"{path}-shm";
            if (File.Exists(shmPath)) File.Delete(shmPath);

            var remaining = Directory.GetFiles(_storageDir, "*.db");
            if (remaining.Length > 0)
            {
                _activeDatabaseName = Path.GetFileNameWithoutExtension(remaining[0]);
            }
            else
            {
                _activeDatabaseName = "Default";
                string defPath = GetDbPath(_activeDatabaseName);
                await InitializeSchemaAsync(defPath);
            }

            OnActiveDatabaseChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DatabaseManager] Error deleting DB {name}: {ex.Message}");
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PositionReferenceResult> QueryPositionAsync(string? databaseName, ulong zobristKey, int maxGames = 25)
    {
        string dbName = string.IsNullOrWhiteSpace(databaseName) ? _activeDatabaseName : databaseName;
        string path = GetDbPath(dbName);
        if (!File.Exists(path))
        {
            await InitializeSchemaAsync(path);
        }

        var result = new PositionReferenceResult
        {
            ZobristKey = zobristKey
        };

        long signedKey = unchecked((long)zobristKey);

        using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await conn.OpenAsync();

        // 1. Candidate move frequencies & win rates
        using (var moveCmd = conn.CreateCommand())
        {
            moveCmd.CommandText = @"
                SELECT 
                    next_move_san,
                    next_move_uci,
                    COUNT(*) as total_games,
                    SUM(CASE WHEN result = '1-0' THEN 1 ELSE 0 END) as white_wins,
                    SUM(CASE WHEN result = '1/2-1/2' THEN 1 ELSE 0 END) as draws,
                    SUM(CASE WHEN result = '0-1' THEN 1 ELSE 0 END) as black_wins
                FROM positions
                WHERE zobrist_key = $zobrist
                GROUP BY next_move_san
                ORDER BY total_games DESC;";

            moveCmd.Parameters.AddWithValue("$zobrist", signedKey);

            using var reader = await moveCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var stat = new PositionMoveStat
                {
                    MoveSan = reader.GetString(0),
                    MoveUci = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    TotalGames = reader.GetInt32(2),
                    WhiteWins = reader.GetInt32(3),
                    Draws = reader.GetInt32(4),
                    BlackWins = reader.GetInt32(5)
                };
                result.CandidateMoves.Add(stat);
                result.TotalPositionGames += stat.TotalGames;
            }
        }

        // 2. Top games reaching this position
        using (var gameCmd = conn.CreateCommand())
        {
            gameCmd.CommandText = @"
                SELECT g.id, g.white, g.black, g.white_elo, g.black_elo, g.result, g.date, g.event, g.site, g.round, g.eco, g.ply_count, g.pgn
                FROM positions p
                JOIN games g ON g.id = p.game_id
                WHERE p.zobrist_key = $zobrist
                ORDER BY (COALESCE(g.white_elo, 0) + COALESCE(g.black_elo, 0)) DESC, g.date DESC
                LIMIT $limit;";

            gameCmd.Parameters.AddWithValue("$zobrist", signedKey);
            gameCmd.Parameters.AddWithValue("$limit", maxGames);

            using var reader = await gameCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.TopGames.Add(ReadGameHeader(reader));
            }
        }

        return result;
    }

    public async Task<(List<GameHeader> Games, int TotalCount)> SearchGamesAsync(string? databaseName, GameFilter filter)
    {
        string dbName = string.IsNullOrWhiteSpace(databaseName) ? _activeDatabaseName : databaseName;
        string path = GetDbPath(dbName);
        if (!File.Exists(path))
        {
            await InitializeSchemaAsync(path);
        }

        var games = new List<GameHeader>();
        int totalCount = 0;

        using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await conn.OpenAsync();

        var whereClauses = new List<string> { "1=1" };
        var parameters = new List<SqliteParameter>();

        if (!string.IsNullOrWhiteSpace(filter.Player))
        {
            whereClauses.Add("(white LIKE $player OR black LIKE $player)");
            parameters.Add(new SqliteParameter("$player", $"%{filter.Player.Trim()}%"));
        }

        if (!string.IsNullOrWhiteSpace(filter.Eco))
        {
            whereClauses.Add("eco LIKE $eco");
            parameters.Add(new SqliteParameter("$eco", $"{filter.Eco.Trim()}%"));
        }

        if (filter.MinElo.HasValue)
        {
            whereClauses.Add("(white_elo >= $minElo OR black_elo >= $minElo)");
            parameters.Add(new SqliteParameter("$minElo", filter.MinElo.Value));
        }

        if (filter.MaxElo.HasValue)
        {
            whereClauses.Add("(white_elo <= $maxElo OR black_elo <= $maxElo)");
            parameters.Add(new SqliteParameter("$maxElo", filter.MaxElo.Value));
        }

        if (!string.IsNullOrWhiteSpace(filter.Result) && filter.Result != "all")
        {
            whereClauses.Add("result = $result");
            parameters.Add(new SqliteParameter("$result", filter.Result));
        }

        if (!string.IsNullOrWhiteSpace(filter.Event))
        {
            whereClauses.Add("event LIKE $event");
            parameters.Add(new SqliteParameter("$event", $"%{filter.Event.Trim()}%"));
        }

        if (filter.YearFrom.HasValue)
        {
            whereClauses.Add("substr(date, 1, 4) >= $yearFrom");
            parameters.Add(new SqliteParameter("$yearFrom", filter.YearFrom.Value.ToString()));
        }

        if (filter.YearTo.HasValue)
        {
            whereClauses.Add("substr(date, 1, 4) <= $yearTo");
            parameters.Add(new SqliteParameter("$yearTo", filter.YearTo.Value.ToString()));
        }

        string whereSql = string.Join(" AND ", whereClauses);

        // Count total matching
        using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = $"SELECT COUNT(*) FROM games WHERE {whereSql};";
            foreach (var p in parameters) countCmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
            totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        // Fetch page
        int offset = Math.Max(0, (filter.PageNumber - 1) * filter.PageSize);
        using (var queryCmd = conn.CreateCommand())
        {
            queryCmd.CommandText = $@"
                SELECT id, white, black, white_elo, black_elo, result, date, event, site, round, eco, ply_count, pgn
                FROM games
                WHERE {whereSql}
                ORDER BY date DESC, id DESC
                LIMIT $limit OFFSET $offset;";

            foreach (var p in parameters) queryCmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
            queryCmd.Parameters.AddWithValue("$limit", filter.PageSize);
            queryCmd.Parameters.AddWithValue("$offset", offset);

            using var reader = await queryCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                games.Add(ReadGameHeader(reader));
            }
        }

        return (games, totalCount);
    }

    public async Task<GameHeader?> GetGameByIdAsync(string? databaseName, long gameId)
    {
        string dbName = string.IsNullOrWhiteSpace(databaseName) ? _activeDatabaseName : databaseName;
        string path = GetDbPath(dbName);
        if (!File.Exists(path)) return null;

        using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, white, black, white_elo, black_elo, result, date, event, site, round, eco, ply_count, pgn
            FROM games
            WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", gameId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadGameHeader(reader);
        }
        return null;
    }

    public async Task<long> SaveGameAsync(string databaseName, GameHeader game)
    {
        string dbName = string.IsNullOrWhiteSpace(databaseName) ? _activeDatabaseName : databaseName;
        string path = GetDbPath(dbName);
        await InitializeSchemaAsync(path);

        using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        long gameId;
        if (game.Id > 0)
        {
            gameId = game.Id;
            using var delPosCmd = conn.CreateCommand();
            delPosCmd.Transaction = tx;
            delPosCmd.CommandText = "DELETE FROM positions WHERE game_id = $id;";
            delPosCmd.Parameters.AddWithValue("$id", gameId);
            await delPosCmd.ExecuteNonQueryAsync();

            using var updateGameCmd = conn.CreateCommand();
            updateGameCmd.Transaction = tx;
            updateGameCmd.CommandText = @"
                UPDATE games 
                SET white = $white, black = $black, white_elo = $white_elo, black_elo = $black_elo,
                    result = $result, date = $date, event = $event, site = $site, round = $round,
                    eco = $eco, ply_count = $ply_count, pgn = $pgn
                WHERE id = $id;";
            updateGameCmd.Parameters.AddWithValue("$id", gameId);
            updateGameCmd.Parameters.AddWithValue("$white", game.White);
            updateGameCmd.Parameters.AddWithValue("$black", game.Black);
            updateGameCmd.Parameters.AddWithValue("$white_elo", (object?)game.WhiteElo ?? DBNull.Value);
            updateGameCmd.Parameters.AddWithValue("$black_elo", (object?)game.BlackElo ?? DBNull.Value);
            updateGameCmd.Parameters.AddWithValue("$result", game.Result);
            updateGameCmd.Parameters.AddWithValue("$date", game.Date);
            updateGameCmd.Parameters.AddWithValue("$event", game.Event);
            updateGameCmd.Parameters.AddWithValue("$site", game.Site);
            updateGameCmd.Parameters.AddWithValue("$round", game.Round);
            updateGameCmd.Parameters.AddWithValue("$eco", game.Eco);
            updateGameCmd.Parameters.AddWithValue("$ply_count", game.PlyCount);
            updateGameCmd.Parameters.AddWithValue("$pgn", game.Pgn);
            await updateGameCmd.ExecuteNonQueryAsync();
        }
        else
        {
            using var insertGameCmd = conn.CreateCommand();
            insertGameCmd.Transaction = tx;
            insertGameCmd.CommandText = @"
                INSERT INTO games (white, black, white_elo, black_elo, result, date, event, site, round, eco, ply_count, pgn)
                VALUES ($white, $black, $white_elo, $black_elo, $result, $date, $event, $site, $round, $eco, $ply_count, $pgn);
                SELECT last_insert_rowid();";
            insertGameCmd.Parameters.AddWithValue("$white", game.White);
            insertGameCmd.Parameters.AddWithValue("$black", game.Black);
            insertGameCmd.Parameters.AddWithValue("$white_elo", (object?)game.WhiteElo ?? DBNull.Value);
            insertGameCmd.Parameters.AddWithValue("$black_elo", (object?)game.BlackElo ?? DBNull.Value);
            insertGameCmd.Parameters.AddWithValue("$result", game.Result);
            insertGameCmd.Parameters.AddWithValue("$date", game.Date);
            insertGameCmd.Parameters.AddWithValue("$event", game.Event);
            insertGameCmd.Parameters.AddWithValue("$site", game.Site);
            insertGameCmd.Parameters.AddWithValue("$round", game.Round);
            insertGameCmd.Parameters.AddWithValue("$eco", game.Eco);
            insertGameCmd.Parameters.AddWithValue("$ply_count", game.PlyCount);
            insertGameCmd.Parameters.AddWithValue("$pgn", game.Pgn);
            gameId = (long)(await insertGameCmd.ExecuteScalarAsync() ?? 0L);
        }

        // Trace positions and index into positions table
        var moveTokens = StreamingPgnImporter.ExtractMainlineMoveTokens(game.Pgn);
        var pos = FenParser.Parse(BoardPosition.StartFen);
        int ply = 0;

        using var insertPosCmd = conn.CreateCommand();
        insertPosCmd.Transaction = tx;
        insertPosCmd.CommandText = @"
            INSERT OR IGNORE INTO positions (game_id, ply, zobrist_key, next_move_san, next_move_uci, result)
            VALUES ($game_id, $ply, $zobrist_key, $next_move_san, $next_move_uci, $result);";
        insertPosCmd.Parameters.AddWithValue("$game_id", gameId);
        var pPly = insertPosCmd.Parameters.Add("$ply", SqliteType.Integer);
        var pZobrist = insertPosCmd.Parameters.Add("$zobrist_key", SqliteType.Integer);
        var pSan = insertPosCmd.Parameters.Add("$next_move_san", SqliteType.Text);
        var pUci = insertPosCmd.Parameters.Add("$next_move_uci", SqliteType.Text);
        insertPosCmd.Parameters.AddWithValue("$result", game.Result);

        foreach (var tok in moveTokens)
        {
            var move = SanParser.ParseSan(pos, tok);
            if (move.IsEmpty) break;

            pPly.Value = ply;
            pZobrist.Value = unchecked((long)pos.ZobristKey);
            pSan.Value = tok;
            pUci.Value = move.ToUci();
            await insertPosCmd.ExecuteNonQueryAsync();

            pos = MoveGenerator.ApplyMove(pos, move);
            ply++;
        }

        // Update exact ply count
        using var updatePlyCmd = conn.CreateCommand();
        updatePlyCmd.Transaction = tx;
        updatePlyCmd.CommandText = "UPDATE games SET ply_count = $ply WHERE id = $id;";
        updatePlyCmd.Parameters.AddWithValue("$ply", ply);
        updatePlyCmd.Parameters.AddWithValue("$id", gameId);
        await updatePlyCmd.ExecuteNonQueryAsync();

        await tx.CommitAsync();
        OnDatabaseModified?.Invoke(dbName);
        OnActiveDatabaseChanged?.Invoke();
        return gameId;
    }

    public async Task<bool> DeleteGameAsync(string databaseName, long gameId)
    {
        string dbName = string.IsNullOrWhiteSpace(databaseName) ? _activeDatabaseName : databaseName;
        string path = GetDbPath(dbName);
        if (!File.Exists(path)) return false;

        using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA foreign_keys = ON;
            DELETE FROM positions WHERE game_id = $id;
            DELETE FROM games WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", gameId);
        int rows = await cmd.ExecuteNonQueryAsync();

        OnDatabaseModified?.Invoke(dbName);
        OnActiveDatabaseChanged?.Invoke();
        return rows > 0;
    }

    public async Task ImportPgnStreamAsync(
        string databaseName,
        Stream stream,
        IProgress<PgnImportProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool deduplicate = false)
    {
        string path = GetDbPath(databaseName);
        await InitializeSchemaAsync(path);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        long totalBytes = stream.CanSeek ? stream.Length : 0;
        await _importer.ImportAsync($"Data Source={path}", reader, totalBytes, progress, cancellationToken, deduplicate);
        OnDatabaseModified?.Invoke(databaseName);
        OnActiveDatabaseChanged?.Invoke();
    }

    public async Task ImportPgnTextAsync(
        string databaseName,
        string pgnText,
        IProgress<PgnImportProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool deduplicate = false)
    {
        string path = GetDbPath(databaseName);
        await InitializeSchemaAsync(path);

        using var reader = new StringReader(pgnText);
        long totalBytes = Encoding.UTF8.GetByteCount(pgnText);
        await _importer.ImportAsync($"Data Source={path}", reader, totalBytes, progress, cancellationToken, deduplicate);
        OnDatabaseModified?.Invoke(databaseName);
        OnActiveDatabaseChanged?.Invoke();
    }

    private static async Task InitializeSchemaAsync(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS games (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                white TEXT NOT NULL,
                black TEXT NOT NULL,
                white_elo INTEGER,
                black_elo INTEGER,
                result TEXT NOT NULL,
                date TEXT,
                event TEXT,
                site TEXT,
                round TEXT,
                eco TEXT,
                ply_count INTEGER DEFAULT 0,
                pgn TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_games_white ON games(white);
            CREATE INDEX IF NOT EXISTS idx_games_black ON games(black);
            CREATE INDEX IF NOT EXISTS idx_games_eco ON games(eco);
            CREATE INDEX IF NOT EXISTS idx_games_date ON games(date);
            CREATE INDEX IF NOT EXISTS idx_games_site ON games(site);

            CREATE TABLE IF NOT EXISTS positions (
                game_id INTEGER NOT NULL,
                ply INTEGER NOT NULL,
                zobrist_key INTEGER NOT NULL,
                next_move_san TEXT NOT NULL,
                next_move_uci TEXT,
                result TEXT NOT NULL,
                PRIMARY KEY (game_id, ply),
                FOREIGN KEY (game_id) REFERENCES games(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_positions_zobrist ON positions(zobrist_key, next_move_san);
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedSampleGamesAsync(string dbPath)
    {
        string samplePgn = @"[Event ""London""]
[Site ""London""]
[Date ""1851.06.21""]
[Round ""?""]
[White ""Anderssen, Adolf""]
[Black ""Kieseritzky, Lionel""]
[Result ""1-0""]
[ECO ""C33""]

1. e4 e5 2. f4 exf4 3. Bc4 Qh4+ 4. Kf1 b5 5. Bxb5 Nf6 6. Nf3 Qh6 7. d3 Nh5 8. Nh4 Qg5 9. Nf5 c6 10. g4 Nf6 11. Rg1 cxb5 12. h4 Qg6 13. h5 Qg5 14. Qf3 Ng8 15. Bxf4 Qf6 16. Nc3 Bc5 17. Nd5 Qxb2 18. Bd6 Bxg1 19. e5 Qxa1+ 20. Ke2 Na6 21. Nxg7+ Kd8 22. Qf6+ Nxf6 23. Be7# 1-0

[Event ""Paris""]
[Site ""Paris""]
[Date ""1858.??.??""]
[Round ""?""]
[White ""Morphy, Paul""]
[Black ""Duke Karl / Count Isouard""]
[Result ""1-0""]
[ECO ""C41""]

1. e4 e5 2. Nf3 d6 3. d4 Bg4 4. dxe5 Bxf3 5. Qxf3 dxe5 6. Bc4 Nf6 7. Qb3 Qe7 8. Nc3 c6 9. Bg5 b5 10. Nxb5 cxb5 11. Bxb5+ Nbd7 12. O-O-O Rd8 13. Rxd7 Rxd7 14. Rd1 Qe6 15. Bxd7+ Nxd7 16. Qb8+ Nxb8 17. Rd8# 1-0

[Event ""Third Rosenwald Trophy""]
[Site ""New York""]
[Date ""1956.10.17""]
[Round ""8""]
[White ""Byrne, Donald""]
[Black ""Fischer, Robert James""]
[Result ""0-1""]
[ECO ""D92""]

1. Nf3 Nf6 2. c4 g6 3. Nc3 Bg7 4. d4 O-O 5. Bf4 d5 6. Qb3 dxc4 7. Qxc4 c6 8. e4 Nbd7 9. Rd1 Nb6 10. Qc5 Bg4 11. Bg5 Na4 12. Qa3 Nxc3 13. bxc3 Nxe4 14. Bxe7 Qb6 15. Bc4 Nxc3 16. Bc5 Rfe8+ 17. Kf1 Be6 18. Bxb6 Bxc4+ 19. Kg1 Ne2+ 20. Kf1 Nxd4+ 21. Kg1 Ne2+ 22. Kf1 Nc3+ 23. Kg1 axb6 24. Qb4 Ra4 25. Qxb6 Nxd1 26. h3 Rxa2 27. Kh2 Nxf2 28. Re1 Rxe1 29. Qd8+ Bf8 30. Nxe1 Bd5 31. Nf3 Ne4 32. Qb8 b5 33. h4 h5 34. Ne5 Kg7 35. Kg1 Bc5+ 36. Kf1 Ng3+ 37. Ke1 Bb4+ 38. Kd1 Bb3+ 39. Kc1 Ne2+ 40. Kb1 Nc3+ 41. Kc1 Rc2# 0-1

[Event ""Wijk aan Zee""]
[Site ""Wijk aan Zee""]
[Date ""1999.01.20""]
[Round ""4""]
[White ""Kasparov, Garry""]
[Black ""Topalov, Veselin""]
[Result ""1-0""]
[ECO ""B07""]

1. e4 d6 2. d4 Nf6 3. Nc3 g6 4. Be3 Bg7 5. Qd2 c6 6. f3 b5 7. Nge2 Nbd7 8. Bh6 Bxh6 9. Qxh6 Bb7 10. a3 e5 11. O-O-O Qe7 12. Kb1 a6 13. Nc1 O-O-O 14. Nb3 exd4 15. Rxd4 c5 16. Rd1 Nb6 17. g3 Kb8 18. Na5 Ba8 19. Bh3 d5 20. Qf4+ Ka7 21. Rhe1 d4 22. Nd5 Nbxd5 23. exd5 Qd6 24. Rxd4 cxd4 25. Re7+ Kb6 26. Qxd4+ Kxa5 27. b4+ Ka4 28. Qc3 Qxd5 29. Ra7 Bb7 30. Rxb7 Qc4 31. Qxf6 Kxa3 32. Qxa6+ Kxb4 33. c3+ Kxc3 34. Qa1+ Kd2 35. Qb2+ Kd1 36. Bf1 Rd2 37. Rd7 Rxd7 38. Bxc4 bxc4 39. Qxh8 Rd3 40. Qa8 c3 41. Qa4+ Ke1 42. f4 f5 43. Kc1 Rd2 44. Qa7 1-0

[Event ""ACM Chess Challenge""]
[Site ""Philadelphia""]
[Date ""1996.02.10""]
[Round ""1""]
[White ""Deep Blue""]
[Black ""Kasparov, Garry""]
[Result ""1-0""]
[ECO ""B22""]

1. e4 c5 2. c3 d5 3. exd5 Qxd5 4. d4 Nf6 5. Nf3 Bg4 6. Be2 e6 7. h3 Bh5 8. O-O Nc6 9. Be3 cxd4 10. cxd4 Bb4 11. a3 Ba5 12. Nc3 Qd6 13. Nb5 Qe7 14. Ne5 Bxe2 15. Qxe2 O-O 16. Rac1 Rac8 17. Bg5 Bb6 18. Bxf6 gxf6 19. Nc4 Rfd8 20. Nxb6 axb6 21. Rfd1 f5 22. Qe3 Qf6 23. d5 Rxd5 24. Rxd5 exd5 25. b3 Kh8 26. Qxb6 Rg8 27. Qc5 d4 28. Nd6 f4 29. Nxb7 Ne5 30. Qd5 f3 31. g3 Nd3 32. Rc7 Re8 33. Nd6 Re1+ 34. Kh2 Nxf2 35. Nxf7+ Kg7 36. Ng5+ Kh6 37. Rxh7+ 1-0";

        using var reader = new StringReader(samplePgn);
        await _importer.ImportAsync($"Data Source={dbPath}", reader);
    }

    private static GameHeader ReadGameHeader(SqliteDataReader reader)
    {
        return new GameHeader
        {
            Id = reader.GetInt64(0),
            White = reader.GetString(1),
            Black = reader.GetString(2),
            WhiteElo = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            BlackElo = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            Result = reader.GetString(5),
            Date = reader.IsDBNull(6) ? "????.??.??" : reader.GetString(6),
            Event = reader.IsDBNull(7) ? "" : reader.GetString(7),
            Site = reader.IsDBNull(8) ? "" : reader.GetString(8),
            Round = reader.IsDBNull(9) ? "" : reader.GetString(9),
            Eco = reader.IsDBNull(10) ? "" : reader.GetString(10),
            PlyCount = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
            Pgn = reader.IsDBNull(12) ? "" : reader.GetString(12)
        };
    }
}
