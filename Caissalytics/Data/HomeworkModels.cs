namespace Caissalytics.Data;

public class HomeworkSheet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Tactics Homework";
    public string Subtitle { get; set; } = "Find the winning move in each position. Write down your solution.";
    public string CoachName { get; set; } = "";
    public string ClubName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int DiagramsPerRow { get; set; } = 2; // 2 (e.g. 2x2, 2x3, 2x4) or 3
    public bool ShowCoordinates { get; set; } = true;
    public bool ShowStudentHeader { get; set; } = true; // Name, Date, Score
    public bool ShowSolutionLines { get; set; } = true; // blank dotted lines for student handwriting
    public bool IncludeAnswerKey { get; set; } = true; // prints answer sheet on page 2
    public List<HomeworkExercise> Exercises { get; set; } = new();
}

public class HomeworkExercise
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Order { get; set; } = 1;
    public string Fen { get; set; } = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    public string ToMove { get; set; } = "white"; // "white" or "black"
    public string Orientation { get; set; } = "white"; // "white" or "black"
    public string Title { get; set; } = "Exercise 1";
    public string Prompt { get; set; } = "White to move and win";
    public string Solution { get; set; } = "";
    public int Points { get; set; } = 1;
    public string Difficulty { get; set; } = "Medium"; // "Easy", "Medium", "Hard"
}
