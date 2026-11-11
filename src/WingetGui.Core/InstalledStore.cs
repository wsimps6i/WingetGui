namespace WingetGui.Core;

/// <summary>The installed-package list shared by the Installed and Updates pages, loaded once and refreshed after operations.</summary>
public sealed class InstalledStore(WingetService winget)
{
    private Task? _refresh;

    public IReadOnlyList<PackageItem> Items { get; private set; } = [];
    public bool IsLoading => _refresh is { IsCompleted: false };
    public string? Error { get; private set; }

    /// <summary>Raised on the calling (UI) thread when loading starts or finishes.</summary>
    public event Action? Changed;

    public int UpdateCount => Items.Count(p => p.Update == UpdateState.Available);

    /// <summary>A refresh already running is reused rather than started twice.</summary>
    public Task RefreshAsync() => IsLoading ? _refresh! : _refresh = LoadAsync();

    private async Task LoadAsync()
    {
        Changed?.Invoke();
        try
        {
            Items = (await winget.GetInstalledAsync()).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            Error = null;
            Log.Info($"Installed: {Items.Count} packages, {UpdateCount} updates");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Log.Error($"Listing installed packages failed: {ex}");
        }
        _refresh = Task.CompletedTask;
        Changed?.Invoke();
    }
}
