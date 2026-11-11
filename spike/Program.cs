// Phase 0 spike: can an unpackaged .NET 10 process drive winget through the COM API?
// Usage: WingetSpike info | catalogs | list | updates | search <text> | install <id> | upgrade <id> | uninstall <id> | probe
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Management.Deployment;
using Windows.Foundation;

var cmd = args.FirstOrDefault() ?? "info";
var arg = args.Skip(1).FirstOrDefault() ?? "";
var sw = Stopwatch.StartNew();

var elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
Console.WriteLine($"Elevated: {elevated}");

PackageManager pm;
try { pm = new PackageManager(); }
catch (Exception ex) { Console.WriteLine($"FAILED to create PackageManager: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}"); return 1; }
Console.WriteLine($"PackageManager created in {sw.ElapsedMilliseconds} ms");

using var cts = new CancellationTokenSource();
if (int.TryParse(Environment.GetEnvironmentVariable("SPIKE_CANCEL_MS"), out var cancelMs)) cts.CancelAfter(cancelMs);
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); Console.WriteLine("Cancelling..."); };

switch (cmd)
{
    case "info":
        Console.WriteLine($"winget COM version: {pm.Version}");
        break;

    case "catalogs":
        foreach (var c in Items(pm.GetPackageCatalogs()))
            Console.WriteLine($"{c.Info.Name,-12} {c.Info.Type,-28} {c.Info.Argument} (trust {c.Info.TrustLevel}, explicit {c.Info.Explicit})");
        break;

    case "list":
    case "updates":
    {
        var catalog = Connect(CompositeSearchBehavior.LocalCatalogs);
        var result = await catalog.FindPackagesAsync(new FindPackagesOptions());
        var rows = Items(result.Matches).Select(m => m.CatalogPackage)
            .Where(p => cmd == "list" || p.IsUpdateAvailable).ToList();
        foreach (var p in rows)
            Console.WriteLine($"{Trim(p.Name, 40),-40} {Trim(p.Id, 40),-40} {p.InstalledVersion?.Version,-16} {(p.IsUpdateAvailable ? p.DefaultInstallVersion?.Version : "")}");
        Console.WriteLine($"{rows.Count} packages ({result.Status}) in {sw.ElapsedMilliseconds} ms");
        break;
    }

    case "search":
    {
        var catalog = Connect(CompositeSearchBehavior.RemotePackagesFromAllCatalogs);
        var opts = new FindPackagesOptions { ResultLimit = 20 };
        opts.Selectors.Add(new PackageMatchFilter { Field = PackageMatchField.CatalogDefault, Option = PackageFieldMatchOption.ContainsCaseInsensitive, Value = arg });
        var result = await catalog.FindPackagesAsync(opts);
        foreach (var m in Items(result.Matches))
            Console.WriteLine($"{Trim(m.CatalogPackage.Name, 40),-40} {m.CatalogPackage.Id,-40} {m.CatalogPackage.DefaultInstallVersion?.Version}");
        Console.WriteLine($"{result.Matches.Count} matches ({result.Status}) in {sw.ElapsedMilliseconds} ms");
        break;
    }

    case "install":
    case "upgrade":
    {
        var pkg = await FindById(arg);
        var meta = pkg.DefaultInstallVersion.GetCatalogPackageMetadata();
        Console.WriteLine($"Details: {meta.PackageName} by {meta.Publisher}, licence {meta.License}, homepage {meta.PackageUrl}");
        var opts = new InstallOptions { PackageInstallMode = PackageInstallMode.Silent };
        var op = cmd == "install" ? pm.InstallPackageAsync(pkg, opts) : pm.UpgradePackageAsync(pkg, opts);
        var r = await Run(op, p => $"{p.State} dl {p.DownloadProgress:P0} inst {p.InstallationProgress:P0}");
        Console.WriteLine($"Result: {r.Status} reboot={r.RebootRequired} installerError={r.InstallerErrorCode} hr=0x{r.ExtendedErrorCode?.HResult:X8}");
        break;
    }

    case "uninstall":
    {
        var pkg = await FindById(arg);
        var r = await Run(pm.UninstallPackageAsync(pkg, new UninstallOptions { PackageUninstallMode = PackageUninstallMode.Silent }),
            p => $"{p.State} {p.UninstallationProgress:P0}");
        Console.WriteLine($"Result: {r.Status} reboot={r.RebootRequired} uninstallerError={r.UninstallerErrorCode} hr=0x{r.ExtendedErrorCode?.HResult:X8}");
        break;
    }

    case "show":
    {
        var catalog = Connect(CompositeSearchBehavior.LocalCatalogs);
        var opts = new FindPackagesOptions();
        opts.Filters.Add(new PackageMatchFilter { Field = PackageMatchField.Id, Option = PackageFieldMatchOption.Equals, Value = arg });
        foreach (var m in Items((await catalog.FindPackagesAsync(opts)).Matches))
        {
            var p = m.CatalogPackage;
            Console.WriteLine($"{p.Id}: installed {p.InstalledVersion?.Version}, default {p.DefaultInstallVersion?.Version}, updateAvailable {p.IsUpdateAvailable}");
            Console.WriteLine($"  installed: catalog={p.InstalledVersion?.PackageCatalog?.Info.Name} channel={p.InstalledVersion?.Channel}");
            Console.WriteLine($"  available: {string.Join(", ", Items(p.AvailableVersions).Take(5).Select(v => v.Version))}");
            if (p.DefaultInstallVersion is { } dv)
            {
                var inst = dv.GetApplicableInstaller(new InstallOptions());
                Console.WriteLine($"  applicable installer for default: {(inst is null ? "NONE" : $"{inst.InstallerType} {inst.Architecture} scope={inst.Scope}")}");
            }
        }
        break;
    }

    case "addsource": // expected to need admin; name/url are throwaway
    {
        var r = await Run(pm.AddPackageCatalogAsync(new AddPackageCatalogOptions { Name = "spiketest", SourceUri = "https://example.invalid/winget", Type = "Microsoft.Rest" }),
            (double p) => $"{p:P0}");
        Console.WriteLine($"Result: {r.Status} hr=0x{r.ExtendedErrorCode?.HResult:X8}");
        break;
    }

    case "probe":
        // Which management features does this API version expose? (pins, source add/remove, export...)
        foreach (var m in typeof(PackageManager).GetMethods().Where(m => m.DeclaringType == typeof(PackageManager)).Select(m => m.Name).Distinct().Order())
            Console.WriteLine($"PackageManager.{m}");
        foreach (var t in typeof(PackageManager).Assembly.GetExportedTypes()
                     .Where(t => t.Name.Contains("Pin") || t.Name.Contains("Catalog") && t.Name.Contains("Options") || t.Name.Contains("Export") || t.Name.Contains("Repair")))
            Console.WriteLine($"type {t.Name}");
        break;
}
Console.WriteLine($"Done in {sw.ElapsedMilliseconds} ms");
return 0;

PackageCatalog Connect(CompositeSearchBehavior behavior)
{
    var opts = new CreateCompositePackageCatalogOptions { CompositeSearchBehavior = behavior };
    foreach (var c in Items(pm.GetPackageCatalogs())) opts.Catalogs.Add(c);
    var conn = pm.CreateCompositePackageCatalog(opts).Connect();
    if (conn.Status != ConnectResultStatus.Ok) throw new Exception($"Connect failed: {conn.Status}");
    return conn.PackageCatalog;
}

async Task<CatalogPackage> FindById(string id)
{
    var opts = new FindPackagesOptions();
    opts.Selectors.Add(new PackageMatchFilter { Field = PackageMatchField.Id, Option = PackageFieldMatchOption.Equals, Value = id });
    var result = await Connect(CompositeSearchBehavior.AllCatalogs).FindPackagesAsync(opts);
    return Items(result.Matches).FirstOrDefault()?.CatalogPackage ?? throw new Exception($"No package with id {id}");
}

async Task<TResult> Run<TResult, TProgress>(IAsyncOperationWithProgress<TResult, TProgress> op, Func<TProgress, string> show)
{
    string last = "";
    var progress = new Progress<TProgress>(p => { var s = show(p); if (s != last) Console.WriteLine($"  {sw.ElapsedMilliseconds,6} ms  {last = s}"); });
    try { return await op.AsTask(cts.Token, progress); }
    catch (OperationCanceledException) { Console.WriteLine("Cancelled."); throw; }
}

static string Trim(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

// The out-of-proc proxy doesn't answer QueryInterface for IIterable, so foreach/LINQ on API lists throws E_NOINTERFACE; index via IVectorView instead.
static IEnumerable<T> Items<T>(IReadOnlyList<T> list) { for (int i = 0; i < list.Count; i++) yield return list[i]; }
