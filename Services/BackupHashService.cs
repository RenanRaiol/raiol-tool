using System.Security.Cryptography;
using System.IO;

namespace raioltool.Services;

public sealed record BackupFileHash(string RelativePath, long LengthBytes, string Sha256);

public sealed record BackupHashReport(
    string RootDirectory,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<BackupFileHash> Files,
    IReadOnlyList<string> Warnings);

public static class BackupHashService
{
    public static async Task<BackupHashReport> ScanAsync(
        string rootDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        string root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"A pasta não existe: {root}");

        var files = new List<BackupFileHash>();
        var warnings = new List<string>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);

        while (pendingDirectories.TryPop(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Não foi possível listar {directory}: {ex.Message}");
                continue;
            }

            foreach (string entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Não foi possível consultar {entry}: {ex.Message}");
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    warnings.Add($"Link ignorado: {entry}");
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pendingDirectories.Push(entry);
                    continue;
                }

                try
                {
                    var info = new FileInfo(entry);
                    await using var stream = new FileStream(
                        entry,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 1024 * 128,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
                    files.Add(new BackupFileHash(
                        Path.GetRelativePath(root, entry),
                        stream.Length,
                        Convert.ToHexString(hash)));
                    progress?.Report($"Hash calculado: {files.Count} arquivo(s)");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Não foi possível calcular hash de {entry}: {ex.Message}");
                }
            }
        }

        return new BackupHashReport(
            root,
            DateTimeOffset.UtcNow,
            files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings);
    }
}
