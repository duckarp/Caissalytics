using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Caissalytics.Data;

public class SkppIntegrationService : ISkppIntegrationService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IUserProfileService? _userProfileService;
    private readonly string _sessionFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SkppSessionData? _cachedSession;
    private string? _lastError;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public event Action? OnConnectionStateChanged;
    public event Action<int>? OnUnreadCountChanged;

    private int _unreadMessageCount;
    public int UnreadMessageCount => _unreadMessageCount;

    private readonly Timer? _pollTimer;

    public SkppIntegrationService(IUserProfileService userProfileService)
        : this(null, null, userProfileService)
    {
    }

    public SkppIntegrationService(
        HttpClient? httpClient = null,
        string? storageDirectory = null,
        IUserProfileService? userProfileService = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _userProfileService = userProfileService;
        string configDir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Caissalytics");
        Directory.CreateDirectory(configDir);
        _sessionFilePath = Path.Combine(configDir, "skpp_session.json");

        _pollTimer = new Timer(async _ =>
        {
            try
            {
                await CheckUnreadMessagesAsync();
            }
            catch { }
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5));
    }

    public async Task<SkppConnectionState> GetConnectionStateAsync()
    {
        var session = await GetSessionAsync();
        bool hasTokens = !string.IsNullOrWhiteSpace(session.RefreshToken);
        return new SkppConnectionState
        {
            ApiUrl = session.ApiUrl ?? "",
            IsConnected = hasTokens && session.CachedUser != null,
            User = session.CachedUser,
            LastError = _lastError
        };
    }

    public async Task SaveApiUrlAsync(string apiUrl)
    {
        await _lock.WaitAsync();
        try
        {
            var session = await LoadSessionUnsafeAsync();
            session.ApiUrl = CleanUrl(apiUrl);
            await SaveSessionUnsafeAsync(session);
        }
        finally
        {
            _lock.Release();
        }

        OnConnectionStateChanged?.Invoke();
    }

    public async Task<(bool Success, string? ErrorMessage)> ConnectAsync(string apiUrl, string usernameOrEmail, string password)
    {
        string cleanApiUrl = CleanUrl(apiUrl);
        if (string.IsNullOrWhiteSpace(cleanApiUrl))
        {
            _lastError = "API Base URL must be specified.";
            return (false, _lastError);
        }

        if (string.IsNullOrWhiteSpace(usernameOrEmail) || string.IsNullOrWhiteSpace(password))
        {
            _lastError = "Username / Email and Password are required.";
            return (false, _lastError);
        }

        try
        {
            var requestPayload = new SkppTokenRequest
            {
                UsernameOrEmail = usernameOrEmail.Trim(),
                Password = password
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestPayload, JsonOptions),
                Encoding.UTF8,
                "application/json");

            string requestUrl = $"{cleanApiUrl}/api/auth/token";
            using var response = await _httpClient.PostAsync(requestUrl, jsonContent);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                string errMsg = ExtractErrorMessage(responseBody, (int)response.StatusCode);
                _lastError = errMsg;
                OnConnectionStateChanged?.Invoke();
                return (false, errMsg);
            }

            var tokenResponse = JsonSerializer.Deserialize<SkppTokenResponse>(responseBody, JsonOptions);
            if (tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                _lastError = "Invalid response from server.";
                return (false, _lastError);
            }

            if (tokenResponse.User == null || (!tokenResponse.User.IsCoach && !tokenResponse.User.IsStudent))
            {
                _lastError = "Access forbidden: Account must have an active Coach or Student role in coaching groups.";
                return (false, _lastError);
            }

            await _lock.WaitAsync();
            try
            {
                var session = new SkppSessionData
                {
                    ApiUrl = cleanApiUrl,
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    AccessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, tokenResponse.ExpiresIn - 30)),
                    CachedUser = tokenResponse.User
                };

                await SaveSessionUnsafeAsync(session);
                _lastError = null;
            }
            finally
            {
                _lock.Release();
            }

            if (tokenResponse.User != null)
            {
                await UpdateProfileFromSkppAsync(tokenResponse.User);
            }

            OnConnectionStateChanged?.Invoke();
            _ = CheckUnreadMessagesAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            _lastError = $"Connection error: {ex.Message}";
            OnConnectionStateChanged?.Invoke();
            return (false, _lastError);
        }
    }

    public async Task DisconnectAsync()
    {
        var session = await GetSessionAsync();
        if (!string.IsNullOrWhiteSpace(session.RefreshToken) && !string.IsNullOrWhiteSpace(session.ApiUrl))
        {
            try
            {
                var revokePayload = new SkppRevokeRequest { RefreshToken = session.RefreshToken };
                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(revokePayload, JsonOptions),
                    Encoding.UTF8,
                    "application/json");

                string revokeUrl = $"{session.ApiUrl}/api/auth/revoke";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _httpClient.PostAsync(revokeUrl, jsonContent, cts.Token);
            }
            catch
            {
                // Proceed with local logout even if remote server is unreachable
            }
        }

        await _lock.WaitAsync();
        try
        {
            var updated = new SkppSessionData
            {
                ApiUrl = session.ApiUrl, // Preserve API URL for user convenience
                AccessToken = "",
                RefreshToken = "",
                AccessTokenExpiresAtUtc = null,
                CachedUser = null
            };

            await SaveSessionUnsafeAsync(updated);
            _lastError = null;
        }
        finally
        {
            _lock.Release();
        }

        UpdateUnreadCount(0);
        OnConnectionStateChanged?.Invoke();
    }

    public async Task<(bool Success, string? ErrorMessage, SkppUserDto? Profile)> VerifyAndRefreshProfileAsync()
    {
        var session = await GetSessionAsync();
        if (string.IsNullOrWhiteSpace(session.ApiUrl))
        {
            return (false, "API Base URL is not configured.", null);
        }

        string? token = await GetValidAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "Not authenticated or session expired.", null);
        }

        try
        {
            string profileUrl = $"{session.ApiUrl}/api/caissalytics/profile";
            using var req = new HttpRequestMessage(HttpMethod.Get, profileUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(req);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                string errMsg = ExtractErrorMessage(responseBody, (int)response.StatusCode);
                _lastError = errMsg;
                return (false, errMsg, null);
            }

            var user = JsonSerializer.Deserialize<SkppUserDto>(responseBody, JsonOptions);

            await _lock.WaitAsync();
            try
            {
                var current = await LoadSessionUnsafeAsync();
                current.CachedUser = user;
                await SaveSessionUnsafeAsync(current);
                _lastError = null;
            }
            finally
            {
                _lock.Release();
            }

            if (user != null)
            {
                await UpdateProfileFromSkppAsync(user);
            }

            OnConnectionStateChanged?.Invoke();
            _ = CheckUnreadMessagesAsync();
            return (true, null, user);
        }
        catch (Exception ex)
        {
            _lastError = $"Connection failed: {ex.Message}";
            return (false, _lastError, null);
        }
    }

    private async Task UpdateProfileFromSkppAsync(SkppUserDto user)
    {
        if (_userProfileService == null) return;

        try
        {
            var (first, last) = user.GetFirstAndLastName();
            if (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
            {
                var profile = await _userProfileService.GetProfileAsync();
                profile.FirstName = first;
                profile.LastName = last;
                if (user.FideId.HasValue && user.FideId.Value > 0 && string.IsNullOrWhiteSpace(profile.FideId))
                {
                    profile.FideId = user.FideId.Value.ToString();
                }
                await _userProfileService.SaveProfileAsync(profile);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SkppIntegrationService] Failed to sync profile names: {ex.Message}");
        }
    }

    public async Task<List<SkppHomeworkDto>> GetAssignedHomeworksAsync(int? groupId = null)
    {
        var session = await GetSessionAsync();
        if (string.IsNullOrWhiteSpace(session.ApiUrl)) return new();

        string? token = await GetValidAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return new();

        try
        {
            string url = $"{session.ApiUrl}/api/caissalytics/homeworks";
            if (groupId.HasValue)
            {
                url += $"?groupId={groupId.Value}";
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode) return new();

            string responseBody = await response.Content.ReadAsStringAsync();
            var list = JsonSerializer.Deserialize<List<SkppHomeworkDto>>(responseBody, JsonOptions);
            return list ?? new();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SkppIntegrationService] Error fetching homeworks: {ex.Message}");
            return new();
        }
    }

    public async Task<SkppHomeworkDto?> GetHomeworkDetailAsync(int id)
    {
        var session = await GetSessionAsync();
        if (string.IsNullOrWhiteSpace(session.ApiUrl)) return null;

        string? token = await GetValidAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            string url = $"{session.ApiUrl}/api/caissalytics/homeworks/{id}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode) return null;

            string responseBody = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<SkppHomeworkDto>(responseBody, JsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SkppIntegrationService] Error fetching homework {id}: {ex.Message}");
            return null;
        }
    }

    public async Task<(bool Success, string? ErrorMessage, SkppSubmissionDto? Submission)> SubmitHomeworkSolutionAsync(int taskId, string answer, string? pgn = null)
    {
        var session = await GetSessionAsync();
        if (string.IsNullOrWhiteSpace(session.ApiUrl)) return (false, "API Base URL is not configured.", null);

        string? token = await GetValidAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return (false, "Not authenticated or session expired.", null);

        try
        {
            var payload = new SkppSubmitHomeworkRequest
            {
                Answer = answer.Trim(),
                Pgn = string.IsNullOrWhiteSpace(pgn) ? null : pgn.Trim()
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json");

            string url = $"{session.ApiUrl}/api/caissalytics/homeworks/{taskId}/submit";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = jsonContent
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(req);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                string errMsg = ExtractErrorMessage(responseBody, (int)response.StatusCode);
                return (false, errMsg, null);
            }

            var submission = JsonSerializer.Deserialize<SkppSubmissionDto>(responseBody, JsonOptions);
            return (true, null, submission);
        }
        catch (Exception ex)
        {
            return (false, $"Submission error: {ex.Message}", null);
        }
    }

    public async Task<List<SkppMessageDto>> GetMessagesAsync(int? groupId = null, int page = 1, int pageSize = 50)
    {
        var session = await GetSessionAsync();
        if (string.IsNullOrWhiteSpace(session.ApiUrl) || string.IsNullOrWhiteSpace(session.RefreshToken))
            return new List<SkppMessageDto>();

        string? token = await GetValidAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            return new List<SkppMessageDto>();

        var query = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}"
        };
        if (groupId.HasValue)
        {
            query.Add($"groupId={groupId.Value}");
        }

        string url = $"{session.ApiUrl}/api/caissalytics/messages?{string.Join("&", query)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return new List<SkppMessageDto>();

            string content = await response.Content.ReadAsStringAsync();
            var list = JsonSerializer.Deserialize<List<SkppMessageDto>>(content, JsonOptions) ?? new List<SkppMessageDto>();

            if (!groupId.HasValue && page == 1)
            {
                int unread = list.Count(m => !m.IsRead);
                UpdateUnreadCount(unread);
            }

            return list;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SkppIntegrationService] Error fetching messages: {ex.Message}");
            return new List<SkppMessageDto>();
        }
    }

    public async Task<(bool Success, string? ErrorMessage, SkppMessageDto? Message)> SendMessageAsync(SkppSendMessageRequest requestPayload)
    {
        var session = await GetSessionAsync();
        if (string.IsNullOrWhiteSpace(session.ApiUrl) || string.IsNullOrWhiteSpace(session.RefreshToken))
            return (false, "Not connected to club platform.", null);

        string? token = await GetValidAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            return (false, "Authentication token expired. Please reconnect.", null);

        string url = $"{session.ApiUrl}/api/caissalytics/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestPayload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(request);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                SkppMessageDto? created = null;
                try
                {
                    created = JsonSerializer.Deserialize<SkppMessageDto>(responseBody, JsonOptions);
                }
                catch { }

                // Check unread count in case sent message affected state
                _ = CheckUnreadMessagesAsync();
                return (true, null, created);
            }

            return (false, ExtractErrorMessage(responseBody, (int)response.StatusCode), null);
        }
        catch (Exception ex)
        {
            return (false, $"Message error: {ex.Message}", null);
        }
    }

    public async Task<bool> MarkMessageAsReadAsync(int messageId)
    {
        var session = await GetSessionAsync();
        if (string.IsNullOrWhiteSpace(session.ApiUrl) || string.IsNullOrWhiteSpace(session.RefreshToken))
            return false;

        string? token = await GetValidAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
            return false;

        string url = $"{session.ApiUrl}/api/caissalytics/messages/{messageId}/read";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                if (_unreadMessageCount > 0)
                {
                    UpdateUnreadCount(_unreadMessageCount - 1);
                }
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> CheckUnreadMessagesAsync()
    {
        var state = await GetConnectionStateAsync();
        if (!state.IsConnected)
        {
            UpdateUnreadCount(0);
            return 0;
        }

        var messages = await GetMessagesAsync(groupId: null, page: 1, pageSize: 50);
        int unread = messages.Count(m => !m.IsRead);
        UpdateUnreadCount(unread);
        return unread;
    }

    private void UpdateUnreadCount(int newCount)
    {
        int clamped = Math.Max(0, newCount);
        if (_unreadMessageCount != clamped)
        {
            _unreadMessageCount = clamped;
            OnUnreadCountChanged?.Invoke(_unreadMessageCount);
        }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
    }

    public async Task<string?> GetValidAccessTokenAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var session = await LoadSessionUnsafeAsync();
            if (string.IsNullOrWhiteSpace(session.RefreshToken) || string.IsNullOrWhiteSpace(session.ApiUrl))
            {
                return null;
            }

            // Return current access token if not expired
            if (!string.IsNullOrWhiteSpace(session.AccessToken) &&
                session.AccessTokenExpiresAtUtc.HasValue &&
                session.AccessTokenExpiresAtUtc.Value > DateTime.UtcNow)
            {
                return session.AccessToken;
            }

            // Otherwise refresh token using POST /api/auth/refresh
            try
            {
                var refreshPayload = new SkppRefreshRequest { RefreshToken = session.RefreshToken };
                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(refreshPayload, JsonOptions),
                    Encoding.UTF8,
                    "application/json");

                string refreshUrl = $"{session.ApiUrl}/api/auth/refresh";
                using var response = await _httpClient.PostAsync(refreshUrl, jsonContent);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    // Refresh token is expired or revoked
                    session.AccessToken = "";
                    session.RefreshToken = "";
                    session.AccessTokenExpiresAtUtc = null;
                    await SaveSessionUnsafeAsync(session);
                    _lastError = "Session expired. Please reconnect.";
                    return null;
                }

                var refreshRes = JsonSerializer.Deserialize<SkppTokenResponse>(responseBody, JsonOptions);
                if (refreshRes == null || string.IsNullOrWhiteSpace(refreshRes.AccessToken))
                {
                    return null;
                }

                session.AccessToken = refreshRes.AccessToken;
                session.RefreshToken = refreshRes.RefreshToken;
                session.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, refreshRes.ExpiresIn - 30));
                if (refreshRes.User != null)
                {
                    session.CachedUser = refreshRes.User;
                }

                await SaveSessionUnsafeAsync(session);
                return session.AccessToken;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SkppIntegrationService] Error refreshing token: {ex.Message}");
                return null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<SkppSessionData> GetSessionAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await LoadSessionUnsafeAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<SkppSessionData> LoadSessionUnsafeAsync()
    {
        if (_cachedSession != null)
        {
            return CloneSession(_cachedSession);
        }

        if (File.Exists(_sessionFilePath))
        {
            try
            {
                string json = await File.ReadAllTextAsync(_sessionFilePath);
                var loaded = JsonSerializer.Deserialize<SkppSessionData>(json, JsonOptions);
                if (loaded != null)
                {
                    _cachedSession = loaded;
                    return CloneSession(_cachedSession);
                }
            }
            catch { }
        }

        _cachedSession = new SkppSessionData();
        return CloneSession(_cachedSession);
    }

    private async Task SaveSessionUnsafeAsync(SkppSessionData session)
    {
        _cachedSession = CloneSession(session);
        string json = JsonSerializer.Serialize(session, JsonOptions);
        await File.WriteAllTextAsync(_sessionFilePath, json);
    }

    private static string CleanUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        return url.Trim().TrimEnd('/');
    }

    private static string ExtractErrorMessage(string responseBody, int statusCode)
    {
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("error", out var errProp) && !string.IsNullOrWhiteSpace(errProp.GetString()))
                {
                    return errProp.GetString()!;
                }
                if (doc.RootElement.TryGetProperty("message", out var msgProp) && !string.IsNullOrWhiteSpace(msgProp.GetString()))
                {
                    return msgProp.GetString()!;
                }
            }
            catch { }
        }

        return statusCode switch
        {
            400 => "Bad request: Please verify your input.",
            401 => "Invalid username, email, or password.",
            403 => "Access forbidden: Account is not enrolled as a Coach or Student.",
            404 => "Server endpoint not found. Please verify the API Base URL.",
            500 => "Internal server error on the remote hub.",
            _ => $"HTTP request failed with status code {statusCode}."
        };
    }

    private static SkppSessionData CloneSession(SkppSessionData s) => new()
    {
        ApiUrl = s.ApiUrl ?? "",
        RefreshToken = s.RefreshToken ?? "",
        AccessToken = s.AccessToken ?? "",
        AccessTokenExpiresAtUtc = s.AccessTokenExpiresAtUtc,
        CachedUser = s.CachedUser
    };
}
