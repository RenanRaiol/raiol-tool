using raioltool.Services;

namespace raioltool.Tests;

public class VdfAndInventoryTests
{
    [Fact]
    public void Parse_ReadsNestedValuesEscapesAndComments()
    {
        const string text = """
            // top-level comment
            "AppState"
            {
                "appid" "123"
                "name" "Game \"Name\""
                "nested" { "path" "C:\\Games\\Example" }
            }
            """;

        IReadOnlyList<VdfEntry> entries = VdfParser.Parse(text);
        VdfEntry app = Assert.Single(entries);

        Assert.True(app.TryGetValue("appid", out string appId));
        Assert.Equal("123", appId);
        Assert.True(app.TryGetValue("name", out string name));
        Assert.Equal("Game \"Name\"", name);
        Assert.True(app.Find("nested")!.TryGetValue("path", out string path));
        Assert.Equal(@"C:\Games\Example", path);
    }

    [Fact]
    public void Parse_ReportsUnclosedObjectWithLineNumber()
    {
        VdfParseException exception = Assert.Throws<VdfParseException>(
            () => VdfParser.Parse("\"root\"\n{\n\"key\" \"value\""));

        Assert.Equal(3, exception.Line);
        Assert.Contains("não foi fechado", exception.Message);
    }

    [Fact]
    public void ParseLibraryFolders_SupportsModernAndLegacyEntries()
    {
        const string text = """
            "libraryfolders"
            {
                "0" { "path" "C:\\Steam" "apps" { "123" "1" } }
                "1" "D:\\Games"
                "contentstatsid" "987"
            }
            """;

        IReadOnlyList<string> paths = SteamInventoryService.ParseLibraryFolders(text);

        Assert.Equal(2, paths.Count);
        Assert.Contains(Path.GetFullPath(@"C:\Steam"), paths);
        Assert.Contains(Path.GetFullPath(@"D:\Games"), paths);
    }

    [Fact]
    public void TryReadApp_ReportsExistingInstallDirectoryWithoutChangingManifest()
    {
        string root = Path.Combine(Path.GetTempPath(), $"raioltool-test-{Guid.NewGuid():N}");
        string steamApps = Path.Combine(root, "steamapps");
        string common = Path.Combine(steamApps, "common");
        string install = Path.Combine(common, "Example Game");
        Directory.CreateDirectory(install);
        string manifest = Path.Combine(steamApps, "appmanifest_123.acf");
        const string content = """
            "AppState"
            {
                "appid" "123"
                "name" "Example"
                "installdir" "Example Game"
                "SizeOnDisk" "4096"
            }
            """;
        File.WriteAllText(manifest, content);

        try
        {
            bool parsed = SteamInventoryService.TryReadApp(content, manifest, "123", out SteamInstalledApp? app, out string? warning);

            Assert.True(parsed);
            Assert.Null(warning);
            Assert.NotNull(app);
            Assert.True(app.InstallDirectoryExists);
            Assert.Equal(4096, app.SizeOnDiskBytes);
            Assert.Equal(content, File.ReadAllText(manifest));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryReadApp_DoesNotResolveInstallPathOutsideCommon()
    {
        string root = Path.Combine(Path.GetTempPath(), $"raioltool-test-{Guid.NewGuid():N}");
        string steamApps = Path.Combine(root, "steamapps");
        Directory.CreateDirectory(steamApps);
        string manifest = Path.Combine(steamApps, "appmanifest_123.acf");
        const string content = """
            "AppState"
            {
                "appid" "123"
                "name" "Example"
                "installdir" "..\\outside"
            }
            """;

        try
        {
            bool parsed = SteamInventoryService.TryReadApp(content, manifest, "123", out SteamInstalledApp? app, out _);

            Assert.True(parsed);
            Assert.NotNull(app);
            Assert.False(app.InstallDirectoryExists);
            Assert.Equal(string.Empty, app.InstallDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
