using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;

namespace WingetGui.Core;

public sealed record AvailableUpdate(Version Version, Uri ZipUrl, string Sha256, Uri ReleasePage);

/// <summary>
/// Updates Winget GUI itself from this repository's GitHub Releases. Trust rests on: HTTPS, one fixed
/// repository, the exact asset name, and the SHA-256 digest GitHub publishes for the asset (no digest,
/// no update). The hash proves the download is the file that was released, not who released it;
/// Authenticode signing would add that (PLANNING.md, Self-update design).
/// </summary>
public sealed class SelfUpdate(HttpClient http)
{
    public const string Repository = "wsimps6i/WingetGui";
    public const string ExeName = "WingetGui.exe";

    public static string AssetName(Version version) => $"WingetGui-{version.ToString(3)}-win-x64.zip";

    /// <summary>The newer release, or null when up to date or nothing is released yet.</summary>
    public async Task<AvailableUpdate?> CheckAsync(Version current, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WingetGui", current.ToString(3)));
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null; // no release published yet
        response.EnsureSuccessStatusCode();
        return ParseLatest(await response.Content.ReadAsStringAsync(ct), current);
    }

    /// <exception cref="InvalidDataException">The release is newer but can't be trusted or used.</exception>
    internal static AvailableUpdate? ParseLatest(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
            throw new InvalidDataException($"The latest release's tag '{tag}' isn't a version number.");
        if (Normalize(version) <= Normalize(current))
            return null;

        var name = AssetName(version);
        var asset = root.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("name").GetString() == name);
        if (asset.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Release {tag} has no {name}.");

        var url = asset.GetProperty("browser_download_url").GetString() ?? "";
        if (!url.StartsWith($"https://github.com/{Repository}/releases/download/", StringComparison.Ordinal))
            throw new InvalidDataException($"Release {tag}'s download isn't on {Repository}'s releases; not trusted.");

        var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
        if (digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 7 + 64 || !digest[7..].All(Uri.IsHexDigit))
            throw new InvalidDataException($"Release {tag} has no SHA-256 digest for {name}, so it can't be verified. Not updating.");

        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;
        return new AvailableUpdate(version, new Uri(url), digest[7..].ToLowerInvariant(),
            new Uri(page ?? $"https://github.com/{Repository}/releases/tag/{tag}"));
    }

    /// <summary>
    /// Downloads the release zip, checks its SHA-256, unpacks it into <paramref name="stagingRoot"/>\version and
    /// confirms the exe inside is that version. Returns the unpacked folder.
    /// </summary>
    public async Task<string> DownloadAsync(AvailableUpdate update, string stagingRoot, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(stagingRoot);
        var zip = Path.Combine(stagingRoot, AssetName(update.Version));
        using (var response = await http.GetAsync(update.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = File.Create(zip))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0)
                        progress?.Report((double)done / total.Value);
                }
            }
            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (actual != update.Sha256)
            {
                File.Delete(zip);
                throw new InvalidDataException($"The download's checksum doesn't match the release (expected {update.Sha256}, got {actual}). Not updating.");
            }
        }

        var folder = Path.Combine(stagingRoot, update.Version.ToString(3));
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        ZipFile.ExtractToDirectory(zip, folder); // .NET refuses entries that would land outside the folder
        File.Delete(zip);

        var exe = Path.Combine(folder, ExeName);
        var fileVersion = File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).FileVersion : null;
        if (!Version.TryParse(fileVersion, out var exeVersion) || Normalize(exeVersion) != Normalize(update.Version))
        {
            Directory.Delete(folder, recursive: true);
            throw new InvalidDataException($"The download doesn't contain {ExeName} version {update.Version.ToString(3)}. Not updating.");
        }
        return folder;
    }

    /// <summary>1.2 == 1.2.0 == 1.2.0.0.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
}

/// <summary>Replaces an installed copy's files with a downloaded version's, keeping the old copy as installDir.previous until it worked.</summary>
public static class UpdateInstaller
{
    /// <summary>
    /// Moves <paramref name="installDir"/> aside (retrying while the old app finishes exiting), copies
    /// <paramref name="sourceDir"/> into its place, and puts the old copy back if that fails.
    /// </summary>
    public static void Swap(string sourceDir, string installDir, TimeSpan waitForUnlock)
    {
        installDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDir));
        if (!File.Exists(Path.Combine(installDir, SelfUpdate.ExeName)))
            throw new InvalidOperationException($"{installDir} has no {SelfUpdate.ExeName}, so it isn't a Winget GUI install. Not touching it.");
        if (!File.Exists(Path.Combine(sourceDir, SelfUpdate.ExeName)))
            throw new InvalidOperationException($"{sourceDir} has no {SelfUpdate.ExeName}.");

        var previous = installDir + ".previous";
        if (Directory.Exists(previous))
            Directory.Delete(previous, recursive: true);

        // Renaming fails as a whole while any file in the folder is in use, so it doubles as "has the old app exited yet".
        var deadline = DateTime.UtcNow + waitForUnlock;
        while (true)
        {
            try
            {
                Directory.Move(installDir, previous);
                break;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(500);
            }
        }

        try
        {
            CopyDirectory(sourceDir, installDir);
        }
        catch
        {
            if (Directory.Exists(installDir))
                Directory.Delete(installDir, recursive: true);
            Directory.Move(previous, installDir);
            throw;
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    }
}
