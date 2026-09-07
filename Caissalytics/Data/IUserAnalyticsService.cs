namespace Caissalytics.Data;

public interface IUserAnalyticsService
{
    event Action? OnAnalyticsRefreshed;

    Task<PersonalAnalyticsReport> GenerateAnalyticsReportAsync(
        string? databaseScope = null,
        UserProfile? profile = null,
        AnalyticsFilterOptions? filter = null);
}
