using System.Reflection;
using Caissalytics.Core;
using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class EcoClassifierTests : IDisposable
{
    private readonly string _testDir;
    private readonly DatabaseManager _dbManager;

    public EcoClassifierTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"caissalytics_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _dbManager = new DatabaseManager(_testDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors on temp dir
        }
    }

    [Theory]
    [InlineData(new[] { "e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "c3" }, "C51")]
    [InlineData(new[] { "e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "c3", "Nf6" }, "C52")]
    [InlineData(new[] { "e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "c3", "Nf6", "d4", "exd4", "cxd4" }, "C53")]
    [InlineData(new[] { "c4", "c6", "Nf3", "d5", "b3" }, "A12")]
    [InlineData(new[] { "c4", "e5" }, "A20")]
    [InlineData(new[] { "e4", "c5", "Nf3", "d6", "d4", "Nf6", "d5" }, "B56")]
    [InlineData(new[] { "e4", "c5", "Nf3", "d6", "d4" }, "B53")]
    [InlineData(new[] { "d4", "Nf6", "c4", "g6", "g3", "d5" }, "D71")]
    [InlineData(new[] { "e4", "e5", "Nf3", "Nf6", "d4" }, "C43")]
    [InlineData(new[] { "e4", "e5", "Nf3", "Nc6" }, "C44")]
    [InlineData(new[] { "e4" }, "B00")]
    [InlineData(new[] { "d4" }, "A40")]
    [InlineData(new[] { "c4" }, "A10")]
    [InlineData(new[] { "Nf3" }, "A04")]
    [InlineData(new[] { "b3" }, "A01")]
    [InlineData(new[] { "f4" }, "A02")]
    [InlineData(new[] { "f4", "d5" }, "A03")]
    public void Classify_KnownLines_ReturnsExpectedCode(string[] moves, string expected)
    {
        Assert.Equal(expected, EcoClassifier.Classify(moves));
    }

    [Fact]
    public void Classify_KingsideAndQueensideCastling_Normalized()
    {
        // 1.e4 e5 2.Nf3 Nc6 3.Bb5 a6 4.Ba4 Nf6 5.0-0 Be7 -> C84 (0-0 must normalize to O-O)
        Assert.Equal("C84", EcoClassifier.Classify(new[]
        {
            "e4", "e5", "Nf3", "Nc6", "Bb5", "a6", "Ba4", "Nf6", "0-0", "Be7"
        }));

        // 1.e4 c5 2.Nf3 d6 3.d4 cxd4 4.Nxd4 Nf6 5.Nc3 Nc6 6.Bg5 e6 7.Qd2 Be7 8.0-0-0 0-0 9.f4 -> B64
        Assert.Equal("B64", EcoClassifier.Classify(new[]
        {
            "e4", "c5", "Nf3", "d6", "d4", "cxd4", "Nxd4", "Nf6", "Nc3", "Nc6",
            "Bg5", "e6", "Qd2", "Be7", "0-0-0", "0-0", "f4"
        }));
    }

    [Fact]
    public void Classify_CheckAnnotationSuffixes_Stripped()
    {
        Assert.Equal("C51", EcoClassifier.Classify(new[]
        {
            "e4", "e5", "Nf3+", "Nc6", "Bc4", "Bc5", "c3!"
        }));
    }

    [Fact]
    public void Classify_DivergenceFromTree_FallsBackToLastTaggedAncestor()
    {
        // Sicilian Scheveningen: 1.e4 c5 2.Nf3 d6 3.d4 cxd4 4.Nxd4 Nf6 5.Nc3 e6 -> B80
        Assert.Equal("B80", EcoClassifier.Classify(new[]
        {
            "e4", "c5", "Nf3", "d6", "d4", "cxd4", "Nxd4", "Nf6", "Nc3", "e6"
        }));

        // 1.e4 c5 2.Nf3 d6 3.d4 Nf6 -> the Nf6 node is untagged (Moscow prefix),
        // so classification falls back to the last tagged ancestor, B53.
        Assert.Equal("B53", EcoClassifier.Classify(new[]
        {
            "e4", "c5", "Nf3", "d6", "d4", "Nf6"
        }));
    }

    [Fact]
    public void Classify_NoMatchingLine_ReturnsNull()
    {
        Assert.Null(EcoClassifier.Classify(new[] { "g3" }));
        Assert.Null(EcoClassifier.Classify(Array.Empty<string>()));
        Assert.Null(EcoClassifier.Classify(null!));
    }

    [Theory]
    [InlineData("0-0", "O-O")]
    [InlineData("o-o", "O-O")]
    [InlineData("0-0-0", "O-O-O")]
    [InlineData("o-o-o", "O-O-O")]
    [InlineData("Nf3+", "Nf3")]
    [InlineData("Qxh7#", "Qxh7")]
    [InlineData("Qd3?", "Qd3")]
    [InlineData("Qd3!?", "Qd3")]
    [InlineData("  Qd3 ", "Qd3")]
    public void Normalize_StripsSuffixesAndCanonicalizesCastling(string input, string expected)
    {
        Assert.Equal(expected, EcoClassifier.Normalize(input));
    }

    [Fact]
    public void EcoTable_AllSequencesAreLegalMovesFromTheStartPosition()
    {
        // Guards against transcription typos in the static ECO tree: every entry must
        // be a fully legal game line from the starting position.
        var table = GetEcoTable();
        Assert.NotEmpty(table);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (code, moves) in table)
        {
            var key = string.Join(' ', moves);
            Assert.True(seen.Add(key), $"Duplicate ECO move sequence: '{key}'");

            var pos = FenParser.Parse(BoardPosition.StartFen);
            foreach (var san in moves)
            {
                var move = SanParser.ParseSan(pos, san);
                Assert.False(move.IsEmpty,
                    $"Illegal move '{san}' in ECO {code} at position {FenParser.ToFen(pos)}");
                pos = MoveGenerator.ApplyMove(pos, move);
            }
        }
    }

    [Fact]
    public async Task ImportPgn_AutoFillsMissingEco()
    {
        await _dbManager.CreateDatabaseAsync("EcoImport");

        // Game 1: no ECO tag at all -> classified, PGN text left untouched.
        // Game 2: [ECO "???"] -> classified, header rewritten in the stored PGN.
        // Game 3: explicit ECO -> left untouched even though the moves classify differently.
        // Game 4: [ECO "???"] but unclassifiable -> stays "???", PGN left untouched.
        string pgn =
@"[Event ""Import 1""]
[White ""WhiteOne""]
[Black ""BlackOne""]
[Date ""2026.01.01""]
[Result ""1-0""]

1. e4 e5 2. Nf3 Nc6 3. Bc4 Bc5 c3 1-0

[Event ""Import 2""]
[White ""WhiteTwo""]
[Black ""BlackTwo""]
[Date ""2026.01.02""]
[ECO ""???""]
[Result ""0-1""]

1. d4 Nf6 2. c4 g6 3. g3 d5 0-1

[Event ""Import 3""]
[White ""WhiteThree""]
[Black ""BlackThree""]
[Date ""2026.01.03""]
[ECO ""C44""]
[Result ""1/2-1/2""]

1. e4 e5 2. Nf3 Nc6 3. Bb5 1/2-1/2

[Event ""Import 4""]
[White ""WhiteFour""]
[Black ""BlackFour""]
[Date ""2026.01.04""]
[ECO ""???""]
[Result ""0-1""]

1. g3 d5 0-1
";

        await _dbManager.ImportPgnTextAsync("EcoImport", pgn);

        var (games, _) = await _dbManager.SearchGamesAsync("EcoImport", new GameFilter());
        Assert.Equal(4, games.Count);

        Assert.Equal("C51", games.First(g => g.White == "WhiteOne").Eco);
        Assert.Equal("D71", games.First(g => g.White == "WhiteTwo").Eco);
        Assert.Equal("C44", games.First(g => g.White == "WhiteThree").Eco);
        Assert.Equal("???", games.First(g => g.White == "WhiteFour").Eco);

        var id1 = games.First(g => g.White == "WhiteOne").Id;
        var id2 = games.First(g => g.White == "WhiteTwo").Id;
        var id3 = games.First(g => g.White == "WhiteThree").Id;
        var id4 = games.First(g => g.White == "WhiteFour").Id;

        var pgn1 = (await _dbManager.GetGameByIdAsync("EcoImport", id1))!.Pgn;
        var pgn2 = (await _dbManager.GetGameByIdAsync("EcoImport", id2))!.Pgn;
        var pgn3 = (await _dbManager.GetGameByIdAsync("EcoImport", id3))!.Pgn;
        var pgn4 = (await _dbManager.GetGameByIdAsync("EcoImport", id4))!.Pgn;

        // No [ECO] line to rewrite when the tag was absent entirely.
        Assert.DoesNotContain("[ECO", pgn1);
        // "???" header rewritten to the classified code.
        Assert.Contains("[ECO \"D71\"]", pgn2);
        Assert.DoesNotContain("???", pgn2);
        // Explicit tag kept as-is.
        Assert.Contains("[ECO \"C44\"]", pgn3);
        // Unclassifiable game keeps its "???" tag.
        Assert.Contains("[ECO \"???\"]", pgn4);
    }

    [Fact]
    public async Task FillMissingEco_BackfillsGamesAndFiresOnDatabaseModified()
    {
        await _dbManager.CreateDatabaseAsync("EcoBackfill");

        string pgn =
@"[Event ""Backfill 1""]
[White ""WhiteOne""]
[Black ""BlackOne""]
[Date ""2026.02.01""]
[Result ""1-0""]

1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 6. Re1 b5 7. Bb3 O-O c3 1-0

[Event ""Backfill 2""]
[White ""WhiteTwo""]
[Black ""BlackTwo""]
[Date ""2026.02.02""]
[ECO ""???""]
[Result ""0-1""]

1. c4 e5 0-1

[Event ""Backfill 3""]
[White ""WhiteThree""]
[Black ""BlackThree""]
[Date ""2026.02.03""]
[ECO ""B30""]
[Result ""1-0""]

1. e4 c5 2. Nf3 Nc6 1-0
";

        await _dbManager.ImportPgnTextAsync("EcoBackfill", pgn);

        // Both Game 1 (no tag) and Game 2 ("???") were already classified at
        // import time, so reset Game 2 to its legacy pre-classification state —
        // this is what databases imported by older app versions look like.
        var dbPath = Path.Combine(_testDir, "EcoBackfill.db");
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "UPDATE games SET eco = '???' , pgn = replace(pgn, '[ECO \"A20\"]' , '[ECO \"???\"]') WHERE white = 'WhiteTwo'";
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }

        string? modifiedDb = null;
        _dbManager.OnDatabaseModified += name => modifiedDb = name;

        int filled = await _dbManager.FillMissingEcoAsync("EcoBackfill");

        var (games, _) = await _dbManager.SearchGamesAsync("EcoBackfill", new GameFilter());
        Assert.Equal(3, games.Count);

        Assert.Equal("C89", games.First(g => g.White == "WhiteOne").Eco);
        Assert.Equal("A20", games.First(g => g.White == "WhiteTwo").Eco);
        Assert.Equal("B30", games.First(g => g.White == "WhiteThree").Eco);

        Assert.Equal(1, filled);
        Assert.Equal("EcoBackfill", modifiedDb);

        // The "???" header was rewritten in the stored PGN.
        var id2 = games.First(g => g.White == "WhiteTwo").Id;
        var pgn2 = (await _dbManager.GetGameByIdAsync("EcoBackfill", id2))!.Pgn;
        Assert.Contains("[ECO \"A20\"]", pgn2);

        // Idempotent: everything is classified now.
        Assert.Equal(0, await _dbManager.FillMissingEcoAsync("EcoBackfill"));
    }

    [Fact]
    public async Task FillMissingEco_UnknownDatabase_ReturnsZero()
    {
        Assert.Equal(0, await _dbManager.FillMissingEcoAsync("NoSuchDatabase"));
    }

    private static (string Code, string[] Moves)[] GetEcoTable()
    {
        var field = typeof(EcoClassifier).GetField("EcoTable", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("EcoTable field not found on EcoClassifier.");
        return ((string, string[])[])field.GetValue(null)!;
    }
}
