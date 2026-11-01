using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace raioltool.Services;

public sealed record SteamInstalledApp(
    string AppId,
    string Name,
    string InstallDirectory,
    string ManifestPath,
    bool InstallDirectoryExists,
    long? SizeOnDiskBytes);

public sealed record SteamInventoryReport(
    IReadOnlyList<string> SteamAppsDirectories,
    IReadOnlyList<SteamInstalledApp> Apps,
    IReadOnlyList<string> Warnings,
    int ManifestFilesScanned);

public static partial class SteamInventoryService
{
    private const long MaximumManifestBytes = 2 * 1024 * 1024;
    private static readonly Regex ManifestNamePattern = AppManifestFileNameRegex();

    [GeneratedRegex(@"^appmanifest_(\d+)\.acf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AppManifestFileNameRegex();

    public static SteamInventoryReport Scan()
    {
        var warnings = new List<string>();
        IReadOnlyList<string> libraries = DiscoverSteamAppsDirectories(warnings);
        var apps = new List<SteamInstalledApp>();
        int scanned = 0;

        foreach (string steamApps in libraries)
        {
            IEnumerable<string> manifests;
            try
            {
                manifests = Directory.EnumerateFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Não foi possível listar {steamApps}: {ex.Message}");
                continue;
            }

            foreach (string manifestPath in manifests)
            {
                Match match = ManifestNamePattern.Match(Path.GetFileName(manifestPath));
                if (!match.Success) continue;

                try
                {
                    var info = new FileInfo(manifestPath);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        warnings.Add($"Manifesto ignorado por ser um link: {manifestPath}");
                        continue;
                    }

                    if (info.Length > MaximumManifestBytes)
                    {
                        warnings.Add($"Manifesto excede o limite de leitura de 2 MiB: {manifestPath}");
                        continue;
                    }

                    string content = File.ReadAllText(manifestPath);
                    scanned++;
                    bool parsed = TryReadApp(content, manifestPath, match.Groups[1].Value, out SteamInstalledApp? app, out string? warning);
                    if (parsed && app is not null)
                        apps.Add(app);
                    if (warning is not null)
                        warnings.Add(warning);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or VdfParseException)
                {
                    warnings.Add($"Não foi possível ler {manifestPath}: {ex.Message}");
                }
            }
        }

        return new SteamInventoryReport(
            libraries,
            apps.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings,
            scanned);
    }

    public static IReadOnlyList<string> DiscoverSteamAppsDirectories(ICollection<string>? warnings = null)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? candidate in GetSteamRoots())
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate.Trim()));
                if (Directory.Exists(full)) roots.Add(full);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                warnings?.Add($"Caminho Steam ignorado ({candidate}): {ex.Message}");
            }
        }

        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            string steamApps = Path.Combine(root, "steamapps");
            if (Directory.Exists(steamApps)) libraries.Add(Path.GetFullPath(steamApps));

            string libraryFile = Path.Combine(steamApps, "libraryfolders.vdf");
            if (!File.Exists(libraryFile)) continue;

            try
            {
                var file = new FileInfo(libraryFile);
                if (file.Length > MaximumManifestBytes)
                {
                    warnings?.Add($"libraryfolders.vdf excede o limite de leitura de 2 MiB: {libraryFile}");
                    continue;
                }

                foreach (string libraryPath in ParseLibraryFolders(File.ReadAllText(libraryFile)))
                {
                    string candidateSteamApps = Path.Combine(libraryPath, "steamapps");
                    if (Directory.Exists(candidateSteamApps))
                        libraries.Add(Path.GetFullPath(candidateSteamApps));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or VdfParseException or ArgumentException or NotSupportedException)
            {
                warnings?.Add($"Não foi possível ler {libraryFile}: {ex.Message}");
            }
        }

        return libraries.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<string> ParseLibraryFolders(string vdfText)
    {
        IReadOnlyList<VdfEntry> root = VdfParser.Parse(vdfText);
        VdfEntry? folders = root.FirstOrDefault(entry =>
            string.Equals(entry.Key, "libraryfolders", StringComparison.OrdinalIgnoreCase));
        if (folders?.Children is null) return Array.Empty<string>();

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (VdfEntry entry in folders.Children)
        {
            if (!IsNumericKey(entry.Key)) continue;
            string? path = entry.IsObject
                ? (entry.TryGetValue("path", out string nestedPath) ? nestedPath : null)
                : entry.Value;
            if (string.IsNullOrWhiteSpace(path)) continue;

            try
            {
                paths.Add(Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                continue;
            }
        }

        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool TryReadApp(
        string vdfText,
        string manifestPath,
        string fileAppId,
        out SteamInstalledApp? app,
        out string? warning)
    {
        app = null;
        warning = null;

        IReadOnlyList<VdfEntry> root = VdfParser.Parse(vdfText);
        VdfEntry? appState = root.FirstOrDefault(entry =>
            string.Equals(entry.Key, "AppState", StringComparison.OrdinalIgnoreCase));
        if (appState?.Children is null)
        {
            warning = $"AppState não encontrado em {manifestPath}";
            return false;
        }

        string appId = appState.TryGetValue("appid", out string parsedId) ? parsedId : fileAppId;
        if (!string.Equals(appId, fileAppId, StringComparison.Ordinal))
            warning = $"O AppID interno ({appId}) não corresponde ao nome do arquivo ({fileAppId}): {manifestPath}";

        string name = appState.TryGetValue("name", out string parsedName) ? parsedName : "(nome ausente)";
        string installDir = appState.TryGetValue("installdir", out string parsedDir) ? parsedDir : string.Empty;
        long? sizeOnDisk = appState.TryGetValue("SizeOnDisk", out string sizeText)
                           && long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out long parsedSize)
                           && parsedSize >= 0
            ? parsedSize
            : null;

        string commonDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, "common"));
        string installPath = ResolveInstallDirectory(commonDirectory, installDir);
        bool exists = installPath.Length > 0 && Directory.Exists(installPath);

        app = new SteamInstalledApp(appId, name, installPath, manifestPath, exists, sizeOnDisk);
        return true;
    }

    private static string ResolveInstallDirectory(string commonDirectory, string installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir) || Path.IsPathRooted(installDir)) return string.Empty;

        try
        {
            string fullCommon = Path.GetFullPath(commonDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string fullInstall = Path.GetFullPath(Path.Combine(fullCommon, installDir));
            return fullInstall.StartsWith(fullCommon, StringComparison.OrdinalIgnoreCase) ? fullInstall : string.Empty;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    private static IEnumerable<string?> GetSteamRoots()
    {
        yield return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        string? executable = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamExe", null) as string;
        yield return executable is null ? null : Path.GetDirectoryName(executable);
        yield return Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string;
        yield return Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam");
    }

    private static bool IsNumericKey(string value) =>
        value.Length > 0 && value.All(char.IsDigit);
}
