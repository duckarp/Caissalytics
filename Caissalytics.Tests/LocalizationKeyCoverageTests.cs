using System.Text.RegularExpressions;
using Caissalytics.Localization;
using Xunit;

namespace Caissalytics.Tests;

public class LocalizationKeyCoverageTests
{
    // Matches literal keys such as Loc["Common_Save"] or Loc["Puzzle_Solved", n]
    private static readonly Regex LiteralKeyPattern = new(@"Loc\[""([A-Za-z0-9_]+)""", RegexOptions.Compiled);

    [Theory]
    [InlineData("en")]
    [InlineData("sk")]
    public async Task EveryLiteralKeyUsedInSource_IsDefined(string language)
    {
        var loc = new LocalizationService(loadPersistedSettings: false);
        await loc.SetLanguageAsync(language);

        var sourceRoot = Path.Combine(FindRepositoryRoot(), "Caissalytics");
        var usedKeys = Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor") || f.EndsWith(".cs"))
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => LiteralKeyPattern.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();

        Assert.NotEmpty(usedKeys);

        // The indexer returns the key itself when no translation exists
        var missing = usedKeys.Where(k => loc[k] == k).OrderBy(k => k).ToList();
        Assert.True(missing.Count == 0, $"Keys missing for '{language}': {string.Join(", ", missing)}");
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Caissalytics.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root (Caissalytics.slnx).");
    }
}
