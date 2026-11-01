using System.IO;
using System.Text.Json;

namespace raioltool.Services;

public sealed class LuaToolSettings
{
    public string? LuaPath { get; set; }
    public string? LuacPath { get; set; }
    public string? StyluaPath { get; set; }
}

public sealed class LuaToolSettingsService
{
    private static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RaiolTool");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public LuaToolSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new LuaToolSettings();
        return JsonSerializer.Deserialize<LuaToolSettings>(File.ReadAllText(SettingsPath))
               ?? throw new InvalidDataException("O arquivo de configurações está vazio ou inválido.");
    }

    public void Save(LuaToolSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
