using System.Text.Json.Serialization;

namespace Caissalytics.Data;

public class SkppTokenRequest
{
    [JsonPropertyName("usernameOrEmail")]
    public string UsernameOrEmail { get; set; } = "";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";
}

public class SkppRefreshRequest
{
    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = "";
}

public class SkppRevokeRequest
{
    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = "";
}

public class SkppTokenResponse
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("tokenType")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; } = 900;

    [JsonPropertyName("user")]
    public SkppUserDto? User { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class SkppUserDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("userName")]
    public string UserName { get; set; } = "";

    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("fideId")]
    public int? FideId { get; set; }

    [JsonPropertyName("currentClassical")]
    public int? CurrentClassical { get; set; }

    [JsonPropertyName("currentRapid")]
    public int? CurrentRapid { get; set; }

    [JsonPropertyName("currentBlitz")]
    public int? CurrentBlitz { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = ""; // "coach" or "student"

    [JsonPropertyName("groups")]
    public List<SkppCoachingGroupDto> Groups { get; set; } = new();

    [JsonIgnore]
    public bool IsCoach => string.Equals(Role, "coach", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsStudent => string.Equals(Role, "student", StringComparison.OrdinalIgnoreCase);

    public (string FirstName, string LastName) GetFirstAndLastName()
    {
        if (!string.IsNullOrWhiteSpace(FirstName) || !string.IsNullOrWhiteSpace(LastName))
        {
            return (FirstName?.Trim() ?? "", LastName?.Trim() ?? "");
        }

        if (string.IsNullOrWhiteSpace(FullName))
        {
            return ("", "");
        }

        var parts = FullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return (parts[0], "");
        }

        return (parts[0], string.Join(" ", parts.Skip(1)));
    }
}

public class SkppCoachingGroupDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("scheduleInfo")]
    public string ScheduleInfo { get; set; } = "";

    [JsonPropertyName("venue")]
    public string Venue { get; set; } = "";

    [JsonPropertyName("seasonName")]
    public string SeasonName { get; set; } = "";

    [JsonPropertyName("primaryCoach")]
    public SkppMemberDto? PrimaryCoach { get; set; }

    [JsonPropertyName("assistantCoach")]
    public SkppMemberDto? AssistantCoach { get; set; }

    [JsonPropertyName("students")]
    public List<SkppMemberDto> Students { get; set; } = new();
}

public class SkppMemberDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("fideId")]
    public int? FideId { get; set; }

    [JsonPropertyName("currentClassical")]
    public int? CurrentClassical { get; set; }

    [JsonPropertyName("currentRapid")]
    public int? CurrentRapid { get; set; }

    [JsonPropertyName("currentBlitz")]
    public int? CurrentBlitz { get; set; }
}

public class SkppSessionData
{
    public string ApiUrl { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public DateTime? AccessTokenExpiresAtUtc { get; set; }
    public SkppUserDto? CachedUser { get; set; }
}

public class SkppConnectionState
{
    public string ApiUrl { get; set; } = "";
    public bool IsConnected { get; set; }
    public SkppUserDto? User { get; set; }
    public string? LastError { get; set; }
}

public class SkppHomeworkDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("coachingGroupId")]
    public int CoachingGroupId { get; set; }

    [JsonPropertyName("coachingGroupName")]
    public string CoachingGroupName { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("fen")]
    public string Fen { get; set; } = "";

    [JsonPropertyName("pgn")]
    public string? Pgn { get; set; }

    [JsonPropertyName("solution")]
    public string? Solution { get; set; }

    [JsonPropertyName("dueDate")]
    public DateTime? DueDate { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("createdByName")]
    public string CreatedByName { get; set; } = "";

    [JsonPropertyName("isPublished")]
    public bool IsPublished { get; set; }

    [JsonPropertyName("mySubmission")]
    public SkppSubmissionDto? MySubmission { get; set; }

    [JsonIgnore]
    public bool HasSubmission => MySubmission != null;

    [JsonIgnore]
    public bool IsOverdue => DueDate.HasValue && DueDate.Value < DateTime.UtcNow && (MySubmission == null || MySubmission.Status == "Assigned");

    [JsonIgnore]
    public string StatusText => MySubmission?.Status ?? "Assigned";
}

public class SkppSubmissionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("coachingTaskId")]
    public int CoachingTaskId { get; set; }

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("studentName")]
    public string StudentName { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Assigned"; // "Assigned", "Submitted", "Reviewed", "Completed"

    [JsonPropertyName("studentAnswer")]
    public string? StudentAnswer { get; set; }

    [JsonPropertyName("coachFeedback")]
    public string? CoachFeedback { get; set; }

    [JsonPropertyName("points")]
    public int? Points { get; set; }

    [JsonPropertyName("wrongAttemptsCount")]
    public int WrongAttemptsCount { get; set; }

    [JsonPropertyName("submittedAt")]
    public DateTime? SubmittedAt { get; set; }

    [JsonPropertyName("reviewedAt")]
    public DateTime? ReviewedAt { get; set; }
}

public class SkppSubmitHomeworkRequest
{
    [JsonPropertyName("answer")]
    public string Answer { get; set; } = "";

    [JsonPropertyName("pgn")]
    public string? Pgn { get; set; }
}

public class SkppMessageDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("recipientRecordId")]
    public int RecipientRecordId { get; set; }

    [JsonPropertyName("senderId")]
    public string? SenderId { get; set; }

    [JsonPropertyName("senderName")]
    public string SenderName { get; set; } = "";

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("messageType")]
    public string MessageType { get; set; } = ""; // "CoachingGroup", "DirectMessage", "SystemNotification"

    [JsonPropertyName("targetGroupName")]
    public string? TargetGroupName { get; set; }

    [JsonPropertyName("referenceType")]
    public string? ReferenceType { get; set; } // "CoachingGroup", "CoachingTask", "CoachingGameReview"

    [JsonPropertyName("referenceId")]
    public int? ReferenceId { get; set; }

    [JsonPropertyName("isRead")]
    public bool IsRead { get; set; }

    [JsonPropertyName("readAt")]
    public DateTime? ReadAt { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}

public class SkppSendMessageRequest
{
    [JsonPropertyName("coachingGroupId")]
    public int? CoachingGroupId { get; set; }

    [JsonPropertyName("recipientUserId")]
    public string? RecipientUserId { get; set; }

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";
}

public class SkppSimpleSuccessResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
