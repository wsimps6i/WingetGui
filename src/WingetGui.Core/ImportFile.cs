using System.Text.Json;

namespace WingetGui.Core;

public sealed record ImportEntry(string Id, string? Version, string Source);

/// <summary>Reads a `winget export` file (https://aka.ms/winget-packages.schema.2.0.json).</summary>
public static class ImportFile
{
    /// <exception cref="FormatException">Not a winget export file.</exception>
    public static IReadOnlyList<ImportEntry> Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var entries = new List<ImportEntry>();
            foreach (var source in doc.RootElement.GetProperty("Sources").EnumerateArray())
            {
                var sourceName = source.TryGetProperty("SourceDetails", out var details) && details.TryGetProperty("Name", out var n)
                    ? n.GetString() ?? "" : "";
                foreach (var package in source.GetProperty("Packages").EnumerateArray())
                {
                    var id = package.GetProperty("PackageIdentifier").GetString();
                    if (string.IsNullOrWhiteSpace(id))
                        continue;
                    var version = package.TryGetProperty("Version", out var v) ? v.GetString() : null;
                    entries.Add(new ImportEntry(id.Trim(), string.IsNullOrWhiteSpace(version) ? null : version.Trim(), sourceName));
                }
            }
            return entries;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException("That isn't a winget export file (it needs a Sources list with PackageIdentifier entries).", ex);
        }
    }
}
