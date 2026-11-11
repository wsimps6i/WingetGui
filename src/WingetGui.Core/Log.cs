using System.Globalization;

namespace WingetGui.Core;

/// <summary>
/// Daily log files, logs\yyyy-MM-dd.log under the data folder. Files older than the retention
/// setting are deleted at startup. Writing is best effort: a log that can't be written never stops an operation.
/// </summary>
public static class Log
{
    private static readonly Lock Gate = new();
    private static string? _folder;

    public static string Folder => _folder ?? Path.Combine(AppSettings.DataRoot, "logs");

    public static void Init(string folder, int retentionDays)
    {
        _folder = folder;
        try
        {
            Directory.CreateDirectory(folder);
            Prune(folder, retentionDays, DateTime.Today);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static void Info(string message) => Write("INFO ", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        if (_folder is null)
            return; // not initialised: tests, or before App starts. Never write to the user's real log by accident.
        var now = DateTime.Now;
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(Path.Combine(Folder, $"{now:yyyy-MM-dd}.log"), $"{now:HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Keeps today's file and the <paramref name="retentionDays"/> - 1 days before it. Only touches files named yyyy-MM-dd.log.</summary>
    internal static void Prune(string folder, int retentionDays, DateTime today)
    {
        var oldestKept = today.Date.AddDays(1 - Math.Max(1, retentionDays));
        foreach (var file in Directory.EnumerateFiles(folder, "*.log"))
        {
            if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                && day < oldestKept)
                File.Delete(file);
        }
    }
}
