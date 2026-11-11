using Microsoft.Management.Deployment;
using Windows.Foundation;

namespace WingetGui.Core;

/// <summary>
/// Everything that talks to winget, through its COM API (Microsoft.Management.Deployment).
/// Runs as the signed-in user; the out-of-proc COM server isn't meant for elevated callers.
/// Gotchas found in the Phase 0 spike are noted where they apply.
/// </summary>
public sealed class WingetService
{
    private readonly Lazy<Task<PackageManager>> _pm = new(() => Task.Run(() =>
    {
        try { return new PackageManager(); }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Couldn't start winget. Install or update App Installer from the Microsoft Store, then try again.", ex);
        }
    }));

    public async Task<IReadOnlyList<PackageItem>> GetInstalledAsync(CancellationToken ct = default)
    {
        var pm = await _pm.Value;
        return await Task.Run(async () =>
        {
            var catalog = Connect(pm, CompositeSearchBehavior.LocalCatalogs, Items(pm.GetPackageCatalogs()));
            var result = await catalog.FindPackagesAsync(new FindPackagesOptions()).AsTask(ct);
            if (result.Status != FindPackagesResultStatus.Ok)
                throw new InvalidOperationException($"Listing installed packages failed: {result.Status}");
            return (IReadOnlyList<PackageItem>)Items(result.Matches).Select(m => ToItem(m.CatalogPackage)).ToList();
        }, ct);
    }

    /// <summary>
    /// Searches each source on its own and yields each one's results as it finishes: msstore alone
    /// can take most of 30 s (Phase 0), and winget's results shouldn't wait for it.
    /// </summary>
    public async IAsyncEnumerable<SearchBatch> SearchAsync(string query, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var pm = await _pm.Value;
        // Explicit sources (winget-font) are only searched when asked for by name, as in the CLI.
        var sources = Items(pm.GetPackageCatalogs()).Where(c => !c.Info.Explicit).ToList();
        var searches = sources.Select(source => Task.Run(async () =>
        {
            var name = source.Info.Name;
            try
            {
                var catalog = Connect(pm, CompositeSearchBehavior.RemotePackagesFromAllCatalogs, [source]);
                var options = new FindPackagesOptions { ResultLimit = 50 };
                options.Selectors.Add(new PackageMatchFilter
                {
                    Field = PackageMatchField.CatalogDefault,
                    Option = PackageFieldMatchOption.ContainsCaseInsensitive,
                    Value = query,
                });
                var result = await catalog.FindPackagesAsync(options).AsTask(ct);
                return result.Status == FindPackagesResultStatus.Ok
                    ? new SearchBatch(name, Items(result.Matches).Select(m => ToItem(m.CatalogPackage, name)).ToList(), null)
                    : new SearchBatch(name, [], result.Status.ToString());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error($"Search '{query}' in {name} failed: {ex.Message}");
                return new SearchBatch(name, [], ex.Message);
            }
        }, ct)).ToList();

        await foreach (var done in Task.WhenEach(searches).WithCancellation(ct))
            yield return await done;
    }

    public async Task<IReadOnlyList<SourceInfo>> GetSourcesAsync()
    {
        var pm = await _pm.Value;
        return await Task.Run(() => (IReadOnlyList<SourceInfo>)Items(pm.GetPackageCatalogs())
            .Select(c => new SourceInfo(c.Info.Name, c.Info.Argument, c.Info.Type, c.Info.Explicit, c.Info.TrustLevel.ToString()))
            .ToList());
    }

    /// <summary>Finds each import entry's package by exact id in its source (any source if the file didn't name one, or names one not configured here).</summary>
    public async Task<IReadOnlyList<(ImportEntry Entry, PackageItem? Package)>> ResolveAsync(IReadOnlyList<ImportEntry> entries, CancellationToken ct = default)
    {
        var pm = await _pm.Value;
        return await Task.Run(async () =>
        {
            var all = Items(pm.GetPackageCatalogs()).ToList();
            var connections = new Dictionary<string, PackageCatalog>(StringComparer.OrdinalIgnoreCase);
            var resolved = new List<(ImportEntry, PackageItem?)>();
            foreach (var entry in entries)
            {
                var source = all.FirstOrDefault(c => c.Info.Name.Equals(entry.Source, StringComparison.OrdinalIgnoreCase));
                var key = source?.Info.Name ?? "";
                if (!connections.TryGetValue(key, out var catalog))
                    connections[key] = catalog = Connect(pm, CompositeSearchBehavior.RemotePackagesFromAllCatalogs, source is null ? all : [source]);
                var options = new FindPackagesOptions { ResultLimit = 1 };
                options.Filters.Add(new PackageMatchFilter { Field = PackageMatchField.Id, Option = PackageFieldMatchOption.Equals, Value = entry.Id });
                var result = await catalog.FindPackagesAsync(options).AsTask(ct);
                var match = Items(result.Matches).FirstOrDefault();
                resolved.Add((entry, match is null ? null : ToItem(match.CatalogPackage, source?.Info.Name)));
            }
            return (IReadOnlyList<(ImportEntry, PackageItem?)>)resolved;
        }, ct);
    }

    public Task<PackageDetails> GetDetailsAsync(PackageItem item) => Task.Run(() =>
    {
        var package = item.Package!;
        // Packages known only from Add/Remove Programs have no catalog version; their installed version still has some metadata.
        var version = package.DefaultInstallVersion ?? package.InstalledVersion;
        var meta = version?.GetCatalogPackageMetadata();
        return new PackageDetails(
            Blank(meta?.Publisher),
            Blank(meta?.Description) ?? Blank(meta?.ShortDescription),
            Blank(meta?.PackageUrl),
            Blank(meta?.License),
            Blank(meta?.ReleaseNotes),
            Items(package.AvailableVersions).Select(v => v.Version).ToList());
    });

    public async Task<OperationResult> InstallAsync(PackageItem item, string? version, InstallScope scope, IProgress<OperationProgress> progress, CancellationToken ct)
    {
        var pm = await _pm.Value;
        var options = new InstallOptions
        {
            PackageInstallMode = PackageInstallMode.Silent,
            PackageInstallScope = scope switch
            {
                InstallScope.User => PackageInstallScope.User,
                InstallScope.Machine => PackageInstallScope.System,
                _ => PackageInstallScope.Any,
            },
        };
        if (version is not null && version != item.Package!.DefaultInstallVersion?.Version)
            options.PackageVersionId = Items(item.Package.AvailableVersions).FirstOrDefault(v => v.Version == version)
                ?? throw new InvalidOperationException($"Version {version} of {item.Name} isn't available any more.");
        return InstallResultOf(await pm.InstallPackageAsync(item.Package, options).AsTask(ct, InstallProgressOf(progress)));
    }

    public async Task<OperationResult> UpgradeAsync(PackageItem item, IProgress<OperationProgress> progress, CancellationToken ct)
    {
        var pm = await _pm.Value;
        var options = new InstallOptions { PackageInstallMode = PackageInstallMode.Silent };
        return InstallResultOf(await pm.UpgradePackageAsync(item.Package!, options).AsTask(ct, InstallProgressOf(progress)));
    }

    public async Task<OperationResult> UninstallAsync(PackageItem item, IProgress<OperationProgress> progress, CancellationToken ct)
    {
        var pm = await _pm.Value;
        var options = new UninstallOptions { PackageUninstallMode = PackageUninstallMode.Silent };
        var r = await pm.UninstallPackageAsync(item.Package!, options)
            .AsTask(ct, new MapProgress<UninstallProgress>(p => p.State == PackageUninstallProgressState.Uninstalling
                ? Progress("Uninstalling", p.UninstallationProgress)
                : new(Stage(p.State.ToString()), null), progress));
        return r.Status == UninstallResultStatus.Ok
            ? new(true, r.RebootRequired ? "Uninstalled. Restart to finish." : "Uninstalled.", r.RebootRequired)
            : new(false, Failure(r.Status.ToString(), r.UninstallerErrorCode, r.ExtendedErrorCode));
    }

    // For LocalCatalogs, pass only remote catalogs: the composite adds the installed catalog itself,
    // and adding it again gives InvalidOptions (Phase 0).
    private static PackageCatalog Connect(PackageManager pm, CompositeSearchBehavior behavior, IEnumerable<PackageCatalogReference> catalogs)
    {
        var options = new CreateCompositePackageCatalogOptions { CompositeSearchBehavior = behavior };
        foreach (var c in catalogs)
            options.Catalogs.Add(c);
        var connection = pm.CreateCompositePackageCatalog(options).Connect();
        return connection.Status == ConnectResultStatus.Ok
            ? connection.PackageCatalog
            : throw new InvalidOperationException($"Couldn't connect to winget sources: {connection.Status}");
    }

    private static PackageItem ToItem(CatalogPackage p, string? source = null)
    {
        var installed = p.InstalledVersion;
        var latest = p.DefaultInstallVersion;
        var update = UpdateState.None;
        if (installed is not null && latest is not null)
        {
            if (p.IsUpdateAvailable)
                update = UpdateState.Available;
            else if (installed.CompareToVersion(latest.Version) == CompareResult.Lesser)
                update = UpdateState.NotApplicable;
        }
        return new PackageItem
        {
            Id = p.Id,
            Name = p.Name,
            InstalledVersion = installed?.Version,
            AvailableVersion = installed is null ? latest?.Version : update == UpdateState.None ? null : latest!.Version,
            Source = source ?? latest?.PackageCatalog?.Info.Name ?? "",
            Update = update,
            Package = p,
        };
    }

    private static MapProgress<InstallProgress> InstallProgressOf(IProgress<OperationProgress> target) =>
        new(p => p.State switch
        {
            PackageInstallProgressState.Downloading => Progress("Downloading", p.DownloadProgress, p.BytesDownloaded, p.BytesRequired),
            PackageInstallProgressState.Installing => Progress("Installing", p.InstallationProgress),
            _ => new(Stage(p.State.ToString()), null),
        }, target);

    /// <summary>"Downloading 42% (12.3 of 29.1 MB)". Exe/MSI installers report no install progress (0), so that stays indeterminate.</summary>
    internal static OperationProgress Progress(string stage, double fraction, ulong done = 0, ulong total = 0)
    {
        if (fraction <= 0) return new(stage + "…", null);
        var text = $"{stage} {fraction:P0}";
        if (total > 0) text += $" ({done / 1048576.0:0.0} of {total / 1048576.0:0.0} MB)";
        return new(text, fraction);
    }

    private static OperationResult InstallResultOf(InstallResult r) => r.Status == InstallResultStatus.Ok
        ? new(true, r.RebootRequired ? "Done. Restart to finish." : "Done.", r.RebootRequired)
        : new(false, Failure(r.Status.ToString(), r.InstallerErrorCode, r.ExtendedErrorCode));

    internal static string Failure(string status, uint installerCode, Exception? extended)
    {
        var text = Stage(status);
        if (installerCode != 0) text += $" (installer exit code {installerCode})";
        if (extended is not null) text += $": {extended.Message.Trim()} (0x{extended.HResult:X8})";
        return text;
    }

    /// <summary>"PostInstall" → "Post install", "InstallError" → "Install error".</summary>
    internal static string Stage(string state) =>
        string.Concat(state.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // The out-of-proc proxy doesn't answer QueryInterface for IIterable, so foreach/LINQ directly on
    // API lists throws E_NOINTERFACE (Phase 0). Index through IVectorView instead.
    internal static IEnumerable<T> Items<T>(IReadOnlyList<T> list)
    {
        for (var i = 0; i < list.Count; i++)
            yield return list[i];
    }

    private sealed class MapProgress<T>(Func<T, OperationProgress> map, IProgress<OperationProgress> target) : IProgress<T>
    {
        public void Report(T value) => target.Report(map(value));
    }
}
