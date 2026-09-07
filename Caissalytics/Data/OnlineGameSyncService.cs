using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Caissalytics.Data;

public class OnlineGameSyncService : IOnlineGameSyncService
{
    public string OnlineGamesDatabaseName => "My online games";

    private readonly IDatabaseService _databaseService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _configFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public OnlineGameSyncService(IDatabaseService databaseService, IHttpClientFactory httpClientFactory)
    {
        _databaseService = databaseService;
        _httpClientFactory = httpClientFactory;

        string configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "Caissalytics");
        Directory.CreateDirectory(configDir);
        _configFilePath = Path.Combine(configDir, "online_sync_config.json");
    }

    public OnlineGameSyncService(
        IDatabaseService databaseService,
        IHttpClientFactory httpClientFactory,
        string customConfigPath)
    {
        _databaseService = databaseService;
        _httpClientFactory = httpClientFactory;
        _configFilePath = customConfigPath;
        var dir = Path.GetDirectoryName(_configFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public async Task<OnlineSyncConfig> GetConfigAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_configFilePath))
            {
                return new OnlineSyncConfig();
            }

            string json = await File.ReadAllTextAsync(_configFilePath);
            return JsonSerializer.Deserialize<OnlineSyncConfig>(json) ?? new OnlineSyncConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OnlineGameSync] Error reading config: {ex.Message}");
            return new OnlineSyncConfig();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveConfigAsync(OnlineSyncConfig config)
    {
        await _lock.WaitAsync();
        try
        {
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_configFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OnlineGameSync] Error saving config: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<OnlineSyncResult> SyncGamesAsync(
        OnlineSyncConfig config,
        IProgress<OnlineSyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new OnlineSyncResult();

        string lichessUser = config.LichessUsername.Trim();
        string chessComUser = config.ChessComUsername.Trim();

        if (string.IsNullOrEmpty(lichessUser) && string.IsNullOrEmpty(chessComUser))
        {
            result.Errors.Add("Please specify at least one username for Lichess or Chess.com.");
            return result;
        }

        // 1. Ensure target database exists
        progress?.Report(new OnlineSyncProgress
        {
            CurrentStage = $"Checking '{OnlineGamesDatabaseName}' database...",
            PercentComplete = 5
        });

        await EnsureOnlineGamesDatabaseExistsAsync();

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Caissalytics/1.0 (chess analysis app; https://github.com/tomask/Caissalytics)");
        client.Timeout = TimeSpan.FromSeconds(60);

        bool syncBoth = !string.IsNullOrEmpty(lichessUser) && !string.IsNullOrEmpty(chessComUser);

        // 2. Sync Lichess
        if (!string.IsNullOrEmpty(lichessUser))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await SyncLichessAsync(client, lichessUser, config.MaxGamesPerPlatform, result, progress, syncBoth, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                result.Errors.Add("Sync cancelled by user.");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OnlineGameSync] Lichess error: {ex.Message}");
                result.Errors.Add($"Lichess ({lichessUser}): {ex.Message}");
            }
        }

        // 3. Sync Chess.com
        if (!string.IsNullOrEmpty(chessComUser))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await SyncChessComAsync(client, chessComUser, config.MaxGamesPerPlatform, result, progress, syncBoth, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                result.Errors.Add("Sync cancelled by user.");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OnlineGameSync] Chess.com error: {ex.Message}");
                result.Errors.Add($"Chess.com ({chessComUser}): {ex.Message}");
            }
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        // Save last sync time
        config.LastSyncUtc = DateTime.UtcNow;
        await SaveConfigAsync(config);

        progress?.Report(new OnlineSyncProgress
        {
            CurrentStage = "Sync Completed!",
            GamesImported = result.TotalImported,
            GamesSkipped = result.TotalSkipped,
            PercentComplete = 100,
            IsFinished = true
        });

        return result;
    }

    private async Task EnsureOnlineGamesDatabaseExistsAsync()
    {
        var existingDbs = await _databaseService.GetDatabasesAsync();
        bool exists = existingDbs.Any(d => string.Equals(d.Name, OnlineGamesDatabaseName, StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            await _databaseService.CreateDatabaseAsync(OnlineGamesDatabaseName);
        }
    }

    private async Task SyncLichessAsync(
        HttpClient client,
        string username,
        int maxGames,
        OnlineSyncResult result,
        IProgress<OnlineSyncProgress>? progress,
        bool syncBoth,
        CancellationToken cancellationToken)
    {
        progress?.Report(new OnlineSyncProgress
        {
            Platform = "Lichess",
            CurrentStage = $"Connecting to Lichess API for '{username}'...",
            PercentComplete = syncBoth ? 10 : 20
        });

        string url = $"https://lichess.org/api/games/user/{Uri.EscapeDataString(username)}?max={maxGames}&clocks=true&evals=true&opening=true";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/x-chess-pgn");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new Exception($"Lichess user '{username}' was not found.");
        }
        if ((int)response.StatusCode == 429)
        {
            throw new Exception("Lichess rate limit reached (HTTP 429). Please wait a minute before retrying.");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Lichess API returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        int startImported = result.TotalImported;
        int startSkipped = result.TotalSkipped;

        var importProgress = new Progress<PgnImportProgress>(p =>
        {
            progress?.Report(new OnlineSyncProgress
            {
                Platform = "Lichess",
                CurrentStage = $"Importing Lichess games ({p.GamesSaved} new, {p.GamesSkipped} duplicate skipped)...",
                GamesDownloaded = p.GamesParsed,
                GamesImported = startImported + p.GamesSaved,
                GamesSkipped = startSkipped + p.GamesSkipped,
                PercentComplete = syncBoth ? 40 : 80
            });
        });

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await _databaseService.ImportPgnStreamAsync(
            OnlineGamesDatabaseName,
            stream,
            importProgress,
            cancellationToken,
            deduplicate: true);

        // Fetch difference to account for games
        var (games, totalCount) = await _databaseService.SearchGamesAsync(OnlineGamesDatabaseName, new GameFilter { PageSize = 1 });
        int lichessAdded = Math.Max(0, totalCount - startImported);
        result.TotalLichessGames = lichessAdded;
        result.TotalImported = totalCount;
    }

    private async Task SyncChessComAsync(
        HttpClient client,
        string username,
        int maxGames,
        OnlineSyncResult result,
        IProgress<OnlineSyncProgress>? progress,
        bool syncBoth,
        CancellationToken cancellationToken)
    {
        progress?.Report(new OnlineSyncProgress
        {
            Platform = "Chess.com",
            CurrentStage = $"Connecting to Chess.com API for '{username}'...",
            PercentComplete = syncBoth ? 50 : 20
        });

        string archivesUrl = $"https://api.chess.com/pub/player/{Uri.EscapeDataString(username.ToLowerInvariant())}/games/archives";
        using var archivesResponse = await client.GetAsync(archivesUrl, cancellationToken);

        if (archivesResponse.StatusCode == HttpStatusCode.NotFound)
        {
            throw new Exception($"Chess.com player '{username}' was not found.");
        }
        if ((int)archivesResponse.StatusCode == 429)
        {
            throw new Exception("Chess.com rate limit reached (HTTP 429). Please wait before retrying.");
        }
        if (!archivesResponse.IsSuccessStatusCode)
        {
            throw new Exception($"Chess.com API returned HTTP {(int)archivesResponse.StatusCode} ({archivesResponse.ReasonPhrase}).");
        }

        string json = await archivesResponse.Content.ReadAsStringAsync(cancellationToken);
        var archivesDoc = JsonDocument.Parse(json);
        if (!archivesDoc.RootElement.TryGetProperty("archives", out var archivesArray) || archivesArray.GetArrayLength() == 0)
        {
            progress?.Report(new OnlineSyncProgress
            {
                Platform = "Chess.com",
                CurrentStage = $"No game archives found for Chess.com player '{username}'.",
                PercentComplete = 100
            });
            return;
        }

        var archiveUrls = new List<string>();
        foreach (var item in archivesArray.EnumerateArray())
        {
            string? url = item.GetString();
            if (!string.IsNullOrEmpty(url))
            {
                archiveUrls.Add(url);
            }
        }

        // Sort descending: newest months first (e.g. 2026/03, 2026/02, 2026/01...)
        archiveUrls.Reverse();

        // Determine how many months to inspect based on maxGames requested
        int monthsToInspect = maxGames switch
        {
            <= 50 => 2,
            <= 100 => 3,
            <= 250 => 6,
            _ => 12
        };

        var selectedArchives = archiveUrls.Take(monthsToInspect).ToList();
        int chessComGamesBefore = result.TotalImported;

        for (int i = 0; i < selectedArchives.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string archiveUrl = selectedArchives[i];
            string monthPgnUrl = $"{archiveUrl}/pgn";
            string monthLabel = archiveUrl.Length > 7 ? archiveUrl[^7..] : $"Month {i + 1}";

            progress?.Report(new OnlineSyncProgress
            {
                Platform = "Chess.com",
                CurrentStage = $"Downloading Chess.com archive {monthLabel} ({i + 1}/{selectedArchives.Count})...",
                PercentComplete = syncBoth ? 50 + (int)((i + 1) * 45.0 / selectedArchives.Count) : (int)((i + 1) * 90.0 / selectedArchives.Count)
            });

            // Polite delay between requests to strictly respect Chess.com rate limits
            if (i > 0)
            {
                await Task.Delay(500, cancellationToken);
            }

            using var pgnResponse = await client.GetAsync(monthPgnUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (pgnResponse.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }
            if ((int)pgnResponse.StatusCode == 429)
            {
                throw new Exception("Chess.com rate limit reached (HTTP 429). Please wait before retrying.");
            }
            if (!pgnResponse.IsSuccessStatusCode)
            {
                continue;
            }

            using var stream = await pgnResponse.Content.ReadAsStreamAsync(cancellationToken);
            int batchBefore = result.TotalImported;

            var importProgress = new Progress<PgnImportProgress>(p =>
            {
                progress?.Report(new OnlineSyncProgress
                {
                    Platform = "Chess.com",
                    CurrentStage = $"Importing {monthLabel} ({p.GamesSaved} new, {p.GamesSkipped} duplicate skipped)...",
                    GamesImported = result.TotalImported + p.GamesSaved,
                    GamesSkipped = result.TotalSkipped + p.GamesSkipped,
                    PercentComplete = syncBoth ? 50 + (int)((i + 1) * 45.0 / selectedArchives.Count) : (int)((i + 1) * 90.0 / selectedArchives.Count)
                });
            });

            await _databaseService.ImportPgnStreamAsync(
                OnlineGamesDatabaseName,
                stream,
                importProgress,
                cancellationToken,
                deduplicate: true);

            var (games, currentCount) = await _databaseService.SearchGamesAsync(OnlineGamesDatabaseName, new GameFilter { PageSize = 1 });
            result.TotalImported = currentCount;
        }

        result.TotalChessComGames = Math.Max(0, result.TotalImported - chessComGamesBefore);
    }
}
