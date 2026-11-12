namespace WingetGui.Core;

/// <summary>Runs a command-line switch without a window, for scripts and scheduled tasks. Writes to <c>output</c> and the daily log.</summary>
public static class SilentRunner
{
    public static async Task<int> RunAsync(ParsedCommandLine cl, TextWriter output, WingetService winget, AppSettings settings)
    {
        if (cl.Error is not null)
        {
            output.WriteLine(cl.Error);
            output.WriteLine();
            output.WriteLine(CommandLine.Usage);
            return 2;
        }
        Log.Info($"Command line: {cl.Command} {cl.Argument}".TrimEnd());
        try
        {
            return cl.Command switch
            {
                SilentCommand.ListUpdates => await ListUpdatesAsync(output, winget, settings),
                SilentCommand.UpdateAll => await UpdateAllAsync(output, winget, settings),
                SilentCommand.Install => await InstallAsync(output, winget, cl.Argument!, settings),
                SilentCommand.Export => Report(output, await WingetCli.ExportAsync(Path.GetFullPath(cl.Argument!), includeVersions: false)),
                SilentCommand.Import => await ImportAsync(output, winget, Path.GetFullPath(cl.Argument!), settings),
                _ => Help(output),
            };
        }
        catch (Exception ex)
        {
            output.WriteLine($"Failed: {ex.Message}");
            Log.Error($"Command line {cl.Command} failed: {ex}");
            return 1;
        }
    }

    private static int Help(TextWriter output)
    {
        output.WriteLine(CommandLine.Usage);
        return 0;
    }

    private static async Task<InstalledStore> LoadAsync(WingetService winget, AppSettings settings)
    {
        var store = new InstalledStore(winget, settings);
        await store.RefreshAsync();
        return store.Error is null ? store : throw new InvalidOperationException(store.Error);
    }

    private static async Task<int> ListUpdatesAsync(TextWriter output, WingetService winget, AppSettings settings)
    {
        var store = await LoadAsync(winget, settings);
        var rows = store.Items.Where(p => p.Update != UpdateState.None).OrderBy(p => p.Update).ToList();
        foreach (var p in rows)
        {
            var note = p.IsPinned ? "  (pinned)" : p.IsSkipped ? "  (skipped)" : p.IsUpdateNotApplicable ? "  (not applicable: different installer type)" : "";
            output.WriteLine($"{p.Name}  [{p.Id}]  {p.InstalledVersion} -> {p.AvailableVersion}{note}");
        }
        output.WriteLine($"{store.UpdateCount} update(s) available.");
        return 0;
    }

    private static async Task<int> UpdateAllAsync(TextWriter output, WingetService winget, AppSettings settings)
    {
        var updates = (await LoadAsync(winget, settings)).Items.Where(p => p.CanUpdate).ToList();
        if (updates.Count == 0)
        {
            output.WriteLine("Everything is up to date.");
            return 0;
        }
        var failed = 0;
        foreach (var p in updates)
        {
            output.WriteLine($"Updating {p.Name} {p.InstalledVersion} -> {p.AvailableVersion}");
            if (!Report(output, await winget.UpgradeAsync(p, settings.OptionsFor(p.Id), new StageWriter(output), CancellationToken.None), p.Name))
                failed++;
        }
        output.WriteLine($"{updates.Count - failed} of {updates.Count} updated.");
        return failed == 0 ? 0 : 1;
    }

    private static async Task<int> InstallAsync(TextWriter output, WingetService winget, string id, AppSettings settings)
    {
        var (_, package) = (await winget.ResolveAsync([new ImportEntry(id, null, "")]))[0];
        if (package is null)
        {
            output.WriteLine($"No package has the id {id}.");
            return 1;
        }
        if (package.IsInstalled)
        {
            output.WriteLine($"{package.Name} is already installed ({package.InstalledVersion}).");
            return 0;
        }
        output.WriteLine($"Installing {package.Name} {package.AvailableVersion}");
        return Report(output, await winget.InstallAsync(package, null, settings.InstallOptionsFor(package.Id), new StageWriter(output), CancellationToken.None), package.Name) ? 0 : 1;
    }

    private static async Task<int> ImportAsync(TextWriter output, WingetService winget, string path, AppSettings settings)
    {
        var resolved = await winget.ResolveAsync(ImportFile.Parse(await File.ReadAllTextAsync(path)));
        var failed = 0;
        foreach (var (entry, package) in resolved)
        {
            if (package is null)
            {
                output.WriteLine($"Not found: {entry.Id}");
                failed++;
            }
            else if (package.IsInstalled)
            {
                output.WriteLine($"Already installed: {package.Name}");
            }
            else
            {
                output.WriteLine($"Installing {package.Name} {entry.Version ?? package.AvailableVersion}");
                if (!Report(output, await winget.InstallAsync(package, entry.Version, settings.InstallOptionsFor(package.Id), new StageWriter(output), CancellationToken.None), package.Name))
                    failed++;
            }
        }
        return failed == 0 ? 0 : 1;
    }

    private static int Report(TextWriter output, OperationResult result)
    {
        Report(output, result, null);
        return result.Success ? 0 : 1;
    }

    private static bool Report(TextWriter output, OperationResult result, string? name)
    {
        output.WriteLine($"  {result.Message}");
        var line = name is null ? result.Message : $"{name}: {result.Message}";
        if (result.Success) Log.Info(line); else Log.Error(line);
        return result.Success;
    }

    /// <summary>Writes each new stage once, synchronously (Progress&lt;T&gt; would post them out of order on the thread pool).</summary>
    private sealed class StageWriter(TextWriter output) : IProgress<OperationProgress>
    {
        private string? _last;

        public void Report(OperationProgress value)
        {
            if (value.Stage == _last)
                return;
            _last = value.Stage;
            output.WriteLine($"  {value.Stage}…");
        }
    }
}
