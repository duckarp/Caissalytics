namespace Caissalytics.Data;

public interface IHomeworkService
{
    event Action? OnSheetsChanged;
    Task<List<HomeworkSheet>> GetSheetsAsync();
    Task<HomeworkSheet?> GetSheetAsync(string id);
    Task SaveSheetAsync(HomeworkSheet sheet);
    Task DeleteSheetAsync(string id);
    Task<HomeworkSheet> DuplicateSheetAsync(string id);
    Task<HomeworkSheet> CreateNewSheetAsync(string title, int exerciseCount = 6, string templateType = "mate_in_one");
}
