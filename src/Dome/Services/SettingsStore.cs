using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Dome.Services;

public sealed class UserSettings
{
    public string Theme { get; set; } = "System";
    public bool HoverExpand { get; set; } = true;
    public bool LyricsEnabled { get; set; }
    public bool WaterReminders { get; set; }
    public int DisplayIndex { get; set; }
    public string WaterDate { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    public List<int> Drinks { get; set; } = new();
    public List<string> Files { get; set; } = new();
    public List<string> Tasks { get; set; } = new();
}

public static class SettingsStore
{
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dome");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");
    public static UserSettings Load()
    {
        try
        {
            var result = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new();
            result.Drinks ??= new(); result.Files ??= new(); result.Tasks ??= new();
            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public static void Save(UserSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
}
