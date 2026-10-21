using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class AtomicFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "caissalytics_atomic_" + Guid.NewGuid().ToString("N"));

    public AtomicFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public async Task Write_ReplacesContent_AndLeavesNoTempFiles()
    {
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "old content that is longer than the new one");

        AtomicFile.WriteAllText(path, "{\"a\":1}");
        Assert.Equal("{\"a\":1}", File.ReadAllText(path));

        await AtomicFile.WriteAllTextAsync(path, "{\"b\":2}");
        Assert.Equal("{\"b\":2}", File.ReadAllText(path));

        Assert.Equal(new[] { path }, Directory.GetFiles(_dir));
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]); // no UTF-8 BOM, same as File.WriteAllText
    }

    [Fact]
    public async Task OwnerOnly_RestrictsPermissions_EvenWhenReplacingAWorldReadableFile()
    {
        if (OperatingSystem.IsWindows()) return;

        string path = Path.Combine(_dir, "user_profile.json");
        File.WriteAllText(path, "{}");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        await AtomicFile.WriteAllTextAsync(path, "{\"token\":\"x\"}", ownerOnly: true);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void Quarantine_MovesFileAside()
    {
        string path = Path.Combine(_dir, "repertoire_data.json");
        File.WriteAllText(path, "{ not json");

        string? moved = AtomicFile.Quarantine(path);

        Assert.NotNull(moved);
        Assert.False(File.Exists(path));
        Assert.Equal("{ not json", File.ReadAllText(moved));
        Assert.Null(AtomicFile.Quarantine(path));
    }

    [Fact]
    public void HomeworkService_CorruptFile_IsKeptInsteadOfOverwritten()
    {
        string path = Path.Combine(_dir, "homework_sheets.json");
        File.WriteAllText(path, "[{ \"Title\": \"My sheet\", truncated");

        _ = new HomeworkService(_dir);

        string backup = Assert.Single(Directory.GetFiles(_dir, "homework_sheets.json.corrupt-*"));
        Assert.Equal("[{ \"Title\": \"My sheet\", truncated", File.ReadAllText(backup));
    }

    [Fact]
    public void RepertoireService_CorruptFile_IsKeptInsteadOfOverwritten()
    {
        string path = Path.Combine(_dir, "repertoire_data.json");
        File.WriteAllText(path, "{ \"WhiteMoves\": [ truncated");

        _ = new RepertoireService(_dir);

        Assert.Single(Directory.GetFiles(_dir, "repertoire_data.json.corrupt-*"));
    }

    [Fact]
    public async Task UserProfileService_CorruptProfile_IsKeptInsteadOfOverwritten()
    {
        string path = Path.Combine(_dir, "user_profile.json");
        File.WriteAllText(path, "{ \"LichessApiToken\": \"lip_secret\", trunc");
        var service = new UserProfileService(path);

        var profile = await service.GetProfileAsync();
        await service.SaveProfileAsync(profile);

        string backup = Assert.Single(Directory.GetFiles(_dir, "user_profile.json.corrupt-*"));
        Assert.Contains("lip_secret", File.ReadAllText(backup));
    }
}
