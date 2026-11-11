using System.Text.Json;
using System.Text.Json.Serialization;

namespace WingetGui.Core;

public enum AppTheme { System, Light, Dark }

/// <summary>
/// The user's preferences, in %LOCALAPPDATA%\WingetGui\settings.json (same pattern as MarkItDown-GUI).
/// A missing or unreadable file means the defaults. To add a setting, add a property here and a card on the Settings page.
/// </summary>
public sealed class AppSettings
{
    public static string DataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WingetGui");

    private static readonly string FilePath = Path.Combine(DataRoot, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Scope for Install selected and imports, and the starting choice in the details pane.</summary>
    public InstallScope DefaultInstallScope { get; set; } = InstallScope.Default;

    /// <summary>Closing the window hides it to the notification area instead of exiting.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Hours between background update checks while the app runs; 0 turns them off.</summary>
    public int UpdateCheckHours { get; set; } = 6;

    /// <summary>Look for a newer Winget GUI release at startup and with each background check.</summary>
    public bool CheckForAppUpdates { get; set; } = true;

    /// <summary>Days of daily log files to keep, today included.</summary>
    public int LogRetentionDays { get; set; } = 14;

    public static AppSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    /// <summary>Best effort: a setting that can't be saved still applies until the app closes.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataRoot);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Couldn't save settings: {ex.Message}");
        }
    }
}
