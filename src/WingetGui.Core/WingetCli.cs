using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace WingetGui.Core;

public sealed record PinInfo(string Id, string PinType)
{
    /// <summary>winget shortens a long id with "…" to fit its table; match on the part shown.</summary>
    public bool Matches(string id) =>
        Id.EndsWith('…') ? id.StartsWith(Id[..^1], StringComparison.OrdinalIgnoreCase) : id.Equals(Id, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// winget.exe, only for what the COM API lacks (PLANNING.md, Phase 0): pins, export, and source
/// add/remove/reset, which need admin and so run in an elevated child process.
/// </summary>
public static partial class WingetCli
{
    public static async Task<IReadOnlyList<PinInfo>> GetPinsAsync(CancellationToken ct = default)
    {
        var (_, output) = await RunAsync(["pin", "list"], ct);
        return ParsePins(output);
    }

    public static async Task<OperationResult> PinAsync(PackageItem item)
    {
        List<string> args = ["pin", "add", "--id", item.Id, "--exact", "--accept-source-agreements"];
        if (item.Source.Length > 0)
            args.AddRange(["--source", item.Source]);
        var (exit, output) = await RunAsync(args);
        return Result(exit, output, $"Pinned {item.Name}. Update all will skip it.");
    }

    public static async Task<OperationResult> UnpinAsync(PackageItem item)
    {
        var (exit, output) = await RunAsync(["pin", "remove", "--id", item.Id, "--exact"]);
        return Result(exit, output, $"Unpinned {item.Name}.");
    }

    public static async Task<OperationResult> ExportAsync(string path, bool includeVersions)
    {
        List<string> args = ["export", "--output", path, "--accept-source-agreements"];
        if (includeVersions)
            args.Add("--include-versions");
        var (exit, output) = await RunAsync(args);
        // winget export warns (and can exit non-zero) about installed packages no source has; the file is still written.
        return File.Exists(path)
            ? new(true, $"Exported to {path}.")
            : Result(exit, output, "");
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>The installer `winget show` would download on this PC (URL, SHA-256), plus its size when the server answers a HEAD request with one.</summary>
    public static async Task<InstallerDownload?> GetInstallerAsync(PackageItem item, CancellationToken ct = default)
    {
        List<string> args = ["show", "--id", item.Id, "--exact", "--accept-source-agreements"];
        if (item.Source.Length > 0)
            args.AddRange(["--source", item.Source]);
        var (exit, output) = await RunAsync(args, ct);
        if (exit != 0 || ParseInstaller(output) is not { } download)
            return null; // e.g. Microsoft Store apps, which have no download URL
        try
        {
            using var response = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, download.Url), ct);
            return response.IsSuccessStatusCode && response.Content.Headers.ContentLength is { } size and > 0 ? download with { Size = size } : download;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return download; // the size is a nice-to-have
        }
    }

    /// <summary>
    /// Reads the "Installer Url:" and "Installer SHA256:" lines of `winget show`.
    /// shortcut: matches winget's English labels; in another display language no installer is shown — switch to the COM API if it ever exposes the URL.
    /// </summary>
    internal static InstallerDownload? ParseInstaller(string output)
    {
        var lines = output.Split('\n').Select(l => l.Trim()).ToList();
        string? Value(string label) => lines.FirstOrDefault(l => l.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase))?[(label.Length + 1)..].Trim();
        var url = Value("Installer Url");
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
            ? new InstallerDownload(url!, Value("Installer SHA256"))
            : null;
    }

    public static Task<OperationResult> AddSourceAsync(string name, string url, string type, bool isExplicit)
    {
        // These go onto an elevated command line, so only plain names and absolute https URLs get through.
        if (!SourceName().IsMatch(name))
            return Task.FromResult(new OperationResult(false, "Use letters, digits, dots, dashes or underscores for the name."));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || url.Contains('"'))
            return Task.FromResult(new OperationResult(false, "The address must be a full https:// URL."));
        if (type is not ("Microsoft.Rest" or "Microsoft.PreIndexed.Package"))
            return Task.FromResult(new OperationResult(false, $"Unknown source type {type}."));
        List<string> args = ["source", "add", "--name", name, "--arg", uri.AbsoluteUri, "--type", type, "--accept-source-agreements"];
        if (isExplicit)
            args.Add("--explicit");
        return RunElevatedAsync(args, $"Added source {name}.");
    }

    public static Task<OperationResult> RemoveSourceAsync(string name) => SourceName().IsMatch(name)
        ? RunElevatedAsync(["source", "remove", "--name", name], $"Removed source {name}.")
        : Task.FromResult(new OperationResult(false, $"Unexpected source name {name}."));

    public static Task<OperationResult> ResetSourcesAsync() =>
        RunElevatedAsync(["source", "reset", "--force"], "Sources reset to winget's defaults.");

    /// <summary>
    /// Reads `winget pin list`'s fixed-width table. Columns are located from the header line above
    /// the dashes, in winget's fixed order: Name, Id, Version, Source, Pin type.
    /// shortcut: assumes no earlier header name contains a space (true in English); a localised header with one would shift columns — switch to reading winget's pinning.db if that ever matters.
    /// </summary>
    internal static IReadOnlyList<PinInfo> ParsePins(string output)
    {
        // Progress spinners are redrawn with \r on the same line; keep only what was drawn last.
        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).Select(l => l[(l.LastIndexOf('\r') + 1)..]).ToList();
        var dashes = lines.FindIndex(l => l.Length > 10 && l.All(c => c == '-'));
        if (dashes < 1)
            return []; // "There are no pins configured." or an error message
        var header = lines[dashes - 1];
        var starts = Enumerable.Range(0, header.Length).Where(i => header[i] != ' ' && (i == 0 || header[i - 1] == ' ')).ToList();
        if (starts.Count < 5)
            return [];
        var pins = new List<PinInfo>();
        foreach (var line in lines.Skip(dashes + 1).Where(l => l.Length > starts[4]))
            pins.Add(new PinInfo(line[starts[1]..starts[2]].Trim(), line[starts[4]..].Trim()));
        return pins;
    }

    internal static async Task<(int Exit, string Output)> RunAsync(IEnumerable<string> args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("winget")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args.Append("--disable-interactivity"))
            psi.ArgumentList.Add(a);
        Log.Info($"winget {string.Join(' ', psi.ArgumentList)}");
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, await stdout + await stderr);
    }

    /// <summary>Runs winget elevated (one UAC prompt). Output can't be captured across elevation, so the exit code is all we get.</summary>
    private static async Task<OperationResult> RunElevatedAsync(IReadOnlyList<string> args, string success)
    {
        var psi = new ProcessStartInfo("winget")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = string.Join(' ', args.Append("--disable-interactivity").Select(Quote)),
        };
        Log.Info($"winget (elevated) {psi.Arguments}");
        try
        {
            using var process = Process.Start(psi)!;
            await process.WaitForExitAsync();
            return process.ExitCode == 0
                ? new(true, success)
                : new(false, $"winget failed with exit code 0x{process.ExitCode:X8}. Details are in winget's own log (winget --logs).");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: the UAC prompt was declined
        {
            return new(false, "Cancelled at the administrator prompt.");
        }
        catch (Win32Exception ex)
        {
            // e.g. a separate admin account, which doesn't have the winget app alias.
            return new(false, $"Couldn't run winget as administrator: {ex.Message}");
        }
    }

    private static OperationResult Result(int exit, string output, string success)
    {
        if (exit == 0)
            return new(true, success);
        var last = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 2 && !l.All(c => "-\\|/ ".Contains(c)));
        return new(false, $"{last ?? "winget failed"} (exit code 0x{exit:X8})");
    }

    private static string Quote(string a) => a.Length > 0 && !a.Any(c => char.IsWhiteSpace(c) || c == '"') ? a : $"\"{a}\"";

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex SourceName();
}
