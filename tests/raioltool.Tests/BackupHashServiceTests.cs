using raioltool.Services;

namespace raioltool.Tests;

public class BackupHashServiceTests
{
    [Fact]
    public async Task ScanAsync_CalculatesSha256AndLeavesFilesUnchanged()
    {
        string root = Path.Combine(Path.GetTempPath(), $"raioltool-hash-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "backup.bin");
        const string contents = "abc";
        await File.WriteAllTextAsync(file, contents);

        try
        {
            BackupHashReport report = await BackupHashService.ScanAsync(root);

            BackupFileHash entry = Assert.Single(report.Files);
            Assert.Equal("backup.bin", entry.RelativePath);
            Assert.Equal(3, entry.LengthBytes);
            Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", entry.Sha256);
            Assert.Equal(contents, await File.ReadAllTextAsync(file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
