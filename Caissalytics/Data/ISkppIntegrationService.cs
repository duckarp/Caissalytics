namespace Caissalytics.Data;

public interface ISkppIntegrationService
{
    event Action? OnConnectionStateChanged;
    event Action<int>? OnUnreadCountChanged;

    int UnreadMessageCount { get; }

    Task<SkppConnectionState> GetConnectionStateAsync();
    Task SaveApiUrlAsync(string apiUrl);
    Task<(bool Success, string? ErrorMessage)> ConnectAsync(string apiUrl, string usernameOrEmail, string password);
    Task DisconnectAsync();
    Task<(bool Success, string? ErrorMessage, SkppUserDto? Profile)> VerifyAndRefreshProfileAsync();
    Task<string?> GetValidAccessTokenAsync();
    Task<List<SkppHomeworkDto>> GetAssignedHomeworksAsync(int? groupId = null);
    Task<SkppHomeworkDto?> GetHomeworkDetailAsync(int id);
    Task<(bool Success, string? ErrorMessage, SkppSubmissionDto? Submission)> SubmitHomeworkSolutionAsync(int taskId, string answer, string? pgn = null);

    Task<List<SkppMessageDto>> GetMessagesAsync(int? groupId = null, int page = 1, int pageSize = 50);
    Task<(bool Success, string? ErrorMessage, SkppMessageDto? Message)> SendMessageAsync(SkppSendMessageRequest request);
    Task<bool> MarkMessageAsReadAsync(int messageId);
    Task<int> CheckUnreadMessagesAsync();
}
