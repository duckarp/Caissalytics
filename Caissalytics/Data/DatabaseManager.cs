using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Caissalytics.Core;
using Microsoft.Data.Sqlite;

namespace Caissalytics.Data;

public class DatabaseManager : IDatabaseService
{
    private class DatabaseSettings
    {
        public string ActiveDatabase { get; set; } = "ClassicalMasters";
        public string ReferenceDatabase { get; set; } = "ClassicalMasters";
    }

    private readonly string _storageDir;
    private readonly string _configFilePath;
    private string _activeDatabaseName = "ClassicalMasters";
    private string _referenceDatabaseName = "ClassicalMasters";
    private readonly object _settingsLock = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly StreamingPgnImporter _importer = new();

    public event Action? OnActiveDatabaseChanged;
    public event Action? OnReferenceDatabaseChanged;
    public event Action<string>? OnDatabaseModified;

    public DatabaseManager()
    {
        _storageDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Caissalytics", "databases");

        Directory.CreateDirectory(_storageDir);
        _configFilePath = Path.Combine(_storageDir, "database_settings.json");

        // Windows legacy migration if user had .local/share/Caissalytics/databases
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string legacyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share", "Caissalytics", "databases");
            if (Directory.Exists(legacyDir))
            {
                try
                {
                    foreach (var file in Directory.GetFiles(legacyDir, "*.db"))
                    {
                        string dest = Path.Combine(_storageDir, Path.GetFileName(file));
                        if (!File.Exists(dest))
                        {
                            File.Copy(file, dest, overwrite: false);
                        }
                    }
                }
                catch { }
            }
        }

        LoadSettings();
        _ = EnsureDefaultDatabaseAsync();
    }

    public DatabaseManager(string customStorageDir)
    {
        _storageDir = customStorageDir;
        Directory.CreateDirectory(_storageDir);
        _configFilePath = Path.Combine(_storageDir, "database_settings.json");
        LoadSettings();
    }

    private void LoadSettings()
    {
        lock (_settingsLock)
        {
            if (File.Exists(_configFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_configFilePath);
                    var settings = JsonSerializer.Deserialize<DatabaseSettings>(json);
                    if (settings != null)
                    {
                        if (!string.IsNullOrWhiteSpace(settings.ActiveDatabase))
                            _activeDatabaseName = SanitizeDatabaseName(settings.ActiveDatabase);
                        if (!string.IsNullOrWhiteSpace(settings.ReferenceDatabase))
                            _referenceDatabaseName = SanitizeDatabaseName(settings.ReferenceDatabase);
                    }
                }
                catch { }
            }
        }
    }

    private void SaveSettings()
    {
        lock (_settingsLock)
        {
            try
            {
                var settings = new DatabaseSettings
                {
                    ActiveDatabase = _activeDatabaseName,
                    ReferenceDatabase = _referenceDatabaseName
                };
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
            }
            catch { }
        }
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
                    IsActive = string.Equals(name, _activeDatabaseName, StringComparison.OrdinalIgnoreCase),
                    IsReference = string.Equals(name, _referenceDatabaseName, StringComparison.OrdinalIgnoreCase)
                });
            }

            return list.OrderByDescending(d => d.IsReference).ThenByDescending(d => d.IsActive).ThenBy(d => d.Name).ToList();
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
            SaveSettings();
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
            SaveSettings();
            OnActiveDatabaseChanged?.Invoke();
        }
        return Task.CompletedTask;
    }

    public Task<string> GetReferenceDatabaseAsync()
    {
        lock (_settingsLock)
        {
            return Task.FromResult(_referenceDatabaseName);
        }
    }

    public Task SetReferenceDatabaseAsync(string name)
    {
        string safeName = SanitizeDatabaseName(name);
        if (!string.Equals(_referenceDatabaseName, safeName, StringComparison.OrdinalIgnoreCase))
        {
            _referenceDatabaseName = safeName;
            SaveSettings();
            OnReferenceDatabaseChanged?.Invoke();
            OnActiveDatabaseChanged?.Invoke();
        }
        return Task.CompletedTask;
    }

    public Task<List<MasterCatalogItem>> GetMasterCatalogAsync()
    {
        var existingDbs = Directory.GetFiles(_storageDir, "*.db")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var catalog = new List<MasterCatalogItem>
        {
            new MasterCatalogItem
            {
                Id = "world-champions",
                Title = "World Champions & Legends",
                Description = "Immortal masterpieces from Steinitz, Lasker, Capablanca, Alekhine, Fischer, Kasparov, Kramnik, Anand, and Carlsen.",
                Tag = "Recommended",
                DatabaseName = "WorldChampions",
                EstimatedGameCount = 14,
                Era = "1851 – 2023",
                IsInstalled = existingDbs.Contains("WorldChampions")
            },
            new MasterCatalogItem
            {
                Id = "grandmaster-classics",
                Title = "Classical Grandmaster Masterpieces",
                Description = "Legendary games covering King's Indian, Sicilian Dragon, Najdorf, Ruy Lopez, French, and Queen's Gambit.",
                Tag = "Classical",
                DatabaseName = "GrandmasterClassics",
                EstimatedGameCount = 4,
                Era = "1958 – 1999",
                IsInstalled = existingDbs.Contains("GrandmasterClassics")
            },
            new MasterCatalogItem
            {
                Id = "candidates-matches",
                Title = "FIDE Candidates & Title Clashes",
                Description = "Critical clashes from modern FIDE Candidates tournaments (Madrid 2022, Toronto 2024).",
                Tag = "Modern",
                DatabaseName = "CandidatesMatches",
                EstimatedGameCount = 2,
                Era = "2022 – 2024",
                IsInstalled = existingDbs.Contains("CandidatesMatches")
            }
        };

        return Task.FromResult(catalog);
    }

    public async Task InstallMasterDatabaseAsync(
        string catalogId,
        IProgress<(int current, int total, string status)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var catalog = await GetMasterCatalogAsync();
        var item = catalog.FirstOrDefault(c => string.Equals(c.Id, catalogId, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            throw new ArgumentException($"Unknown catalog item '{catalogId}'");
        }

        progress?.Report((0, 100, $"Preparing database '{item.DatabaseName}'..."));

        string pgnText = item.Id switch
        {
            "world-champions" => CuratedMasterGames.GetWorldChampionsPgn(),
            "grandmaster-classics" => CuratedMasterGames.GetGrandmasterClassicsPgn(),
            "candidates-matches" => CuratedMasterGames.GetCandidatesMatchesPgn(),
            _ => CuratedMasterGames.GetWorldChampionsPgn()
        };

        await CreateDatabaseAsync(item.DatabaseName);

        progress?.Report((30, 100, $"Indexing {item.Title} games into opening tree..."));

        var importProgress = new Progress<PgnImportProgress>(p =>
        {
            int mapped = 30 + (int)(p.PercentComplete * 0.65);
            progress?.Report((mapped, 100, $"Indexed {p.GamesSaved} games..."));
        });

        await ImportPgnTextAsync(item.DatabaseName, pgnText, importProgress, cancellationToken, deduplicate: true);

        // Automatically set as Reference Database if user installs the recommended collection or has default
        if (string.Equals(item.Id, "world-champions", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_referenceDatabaseName, "ClassicalMasters", StringComparison.OrdinalIgnoreCase))
        {
            await SetReferenceDatabaseAsync(item.DatabaseName);
        }

        progress?.Report((100, 100, $"Master Database '{item.DatabaseName}' successfully installed!"));
        OnDatabaseModified?.Invoke(item.DatabaseName);
        OnActiveDatabaseChanged?.Invoke();
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
            SaveSettings();
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
            IsActive = true,
            IsReference = string.Equals(safeName, _referenceDatabaseName, StringComparison.OrdinalIgnoreCase)
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

            if (string.Equals(safeName, _referenceDatabaseName, StringComparison.OrdinalIgnoreCase))
            {
                _referenceDatabaseName = _activeDatabaseName;
                OnReferenceDatabaseChanged?.Invoke();
            }

            SaveSettings();
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

    public async Task<List<GameHeader>> GetAllGameHeadersAsync(string? databaseName = null)
    {
        var games = new List<GameHeader>();

        if (string.Equals(databaseName, "ALL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(databaseName, "All Databases", StringComparison.OrdinalIgnoreCase))
        {
            var files = Directory.GetFiles(_storageDir, "*.db");
            foreach (var file in files)
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={file};Mode=ReadOnly");
                    await conn.OpenAsync();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        SELECT id, white, black, white_elo, black_elo, result, date, event, site, round, eco, ply_count, pgn
                        FROM games
                        ORDER BY date DESC, id DESC;";
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        games.Add(ReadGameHeader(reader));
                    }
                }
                catch { }
            }
            return games.OrderByDescending(g => g.Date).ThenByDescending(g => g.Id).ToList();
        }

        string dbName = string.IsNullOrWhiteSpace(databaseName) ? _activeDatabaseName : databaseName;
        string path = GetDbPath(dbName);
        if (!File.Exists(path)) return games;

        using (var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, white, black, white_elo, black_elo, result, date, event, site, round, eco, ply_count, pgn
                FROM games
                ORDER BY date DESC, id DESC;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                games.Add(ReadGameHeader(reader));
            }
        }

        return games;
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
        string pgn = CuratedMasterGames.GetWorldChampionsPgn();
        using var reader = new StringReader(pgn);
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
