namespace WingetGui.Core;

/// <summary>The installed-package list shared by the Installed and Updates pages, loaded once and refreshed after operations.</summary>
public sealed class InstalledStore(WingetService winget, AppSettings settings)
{
    private Task? _refresh;

    public IReadOnlyList<PackageItem> Items { get; private set; } = [];
    public bool IsLoading => _refresh is { IsCompleted: false };
    public string? Error { get; private set; }

    /// <summary>Raised on the calling (UI) thread when loading starts or finishes.</summary>
    public event Action? Changed;

    /// <summary>Updates that Update all would install (pinned ones are held back).</summary>
    public int UpdateCount => Items.Count(p => p.CanUpdate);

    /// <summary>A refresh already running is reused rather than started twice.</summary>
    public Task RefreshAsync() => IsLoading ? _refresh! : _refresh = LoadAsync();

    private async Task LoadAsync()
    {
        Changed?.Invoke();
        var pinsTask = LoadPinsAsync();
        try
        {
            var items = (await winget.GetInstalledAsync()).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var pins = await pinsTask;
            foreach (var item in items)
            {
                item.Pin = pins.FirstOrDefault(p => p.Matches(item.Id))?.PinType;
                item.SkippedVersion = settings.SkippedVersions.GetValueOrDefault(item.Id);
            }
            Items = items;
            Error = null;
            Log.Info($"Installed: {Items.Count} packages, {UpdateCount} updates, {pins.Count} pins");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Log.Error($"Listing installed packages failed: {ex}");
        }
        _refresh = Task.CompletedTask;
        Changed?.Invoke();
    }

    /// <summary>Skips or stops skipping a package's available version, saves it, and tells the pages (no reload needed).</summary>
    public void SetSkipped(PackageItem item, bool skip)
    {
        if (skip && item.AvailableVersion is { } version)
            settings.SkippedVersions[item.Id] = version;
        else
            settings.SkippedVersions.Remove(item.Id);
        settings.Save();
        item.SkippedVersion = settings.SkippedVersions.GetValueOrDefault(item.Id);
        Log.Info($"{(skip ? "Skipping" : "Stopped skipping")} {item.Id} {item.AvailableVersion}");
        Changed?.Invoke();
    }

    /// <summary>Pins only decorate the list; if winget.exe can't list them the list still loads.</summary>
    private static async Task<IReadOnlyList<PinInfo>> LoadPinsAsync()
    {
        try
        {
            return await WingetCli.GetPinsAsync();
        }
        catch (Exception ex)
        {
            Log.Error($"Listing pins failed: {ex.Message}");
            return [];
        }
    }
}
