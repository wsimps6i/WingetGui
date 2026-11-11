using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using WingetGui.Core;

namespace WingetGui;

public partial class App : Application
{
    public static AppSettings Settings { get; } = AppSettings.Load();
    public static WingetService Winget { get; } = new();
    public static OperationQueue Queue { get; } = new();
    public static InstalledStore Installed { get; } = new(Winget);
    public static MainWindow Window { get; private set; } = null!;
    public static bool IsExiting { get; private set; }

    public static string Version { get; } =
        typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static string IconPath { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

    public static SelfUpdate Updater { get; } = new(new HttpClient { Timeout = TimeSpan.FromMinutes(10) });

    /// <summary>Where a downloaded update is unpacked before it replaces the install; cleared at startup.</summary>
    public static string UpdateStagingRoot { get; } = Path.Combine(AppSettings.DataRoot, "update");

    /// <summary>A Debug build runs from bin\, which self-update must not replace; it can still check.</summary>
#if DEBUG
    public static bool CanSelfUpdate => false;
#else
    public static bool CanSelfUpdate => true;
#endif

    /// <summary>Written by a failed -ApplyUpdate, read and removed by the next start.</summary>
    public static string UpdateFailureFile { get; } = Path.Combine(AppSettings.DataRoot, "update-failed.txt");

    /// <summary>Why the last self-update didn't apply, shown once in Settings; null normally.</summary>
    public static string? LastUpdateFailure { get; private set; }

    /// <summary>A newer Winget GUI release, once a check has found one.</summary>
    public static AvailableUpdate? AppUpdate { get; private set; }

    /// <summary>Raised on the UI thread after each Winget GUI update check.</summary>
    public static event Action? AppUpdateChanged;

    public static async Task<AvailableUpdate?> CheckForAppUpdateAsync()
    {
        AppUpdate = await Updater.CheckAsync(System.Version.Parse(Version));
        Log.Info(AppUpdate is null ? "Winget GUI is up to date" : $"Winget GUI {AppUpdate.Version.ToString(3)} is available");
        AppUpdateChanged?.Invoke();
        return AppUpdate;
    }

    /// <summary>The automatic check: quiet, failures only logged (offline, rate-limited, nothing released yet…).</summary>
    private static async Task AutoCheckForAppUpdateAsync()
    {
        if (!Settings.CheckForAppUpdates || !CanSelfUpdate)
            return;
        try
        {
            await CheckForAppUpdateAsync();
        }
        catch (Exception ex)
        {
            Log.Error($"Winget GUI update check failed: {ex.Message}");
        }
    }

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private DispatcherQueueTimer? _checkTimer;
    private TrayIcon? _tray;
    // "id version" pairs already announced, so each new update gets one notification, not one per check.
    private readonly HashSet<string> _announced = [];

    public App()
    {
        InitializeComponent(); // Log.Init already ran in Program.Main
        Log.Info($"Winget GUI {Version} started{(Program.StartInTray ? " in the notification area" : "")}");
        UnhandledException += (_, e) => Log.Error($"Unhandled: {e.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            if (File.Exists(UpdateFailureFile))
            {
                LastUpdateFailure = File.ReadAllText(UpdateFailureFile);
                File.Delete(UpdateFailureFile);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        Window = new MainWindow();
        _tray = new TrayIcon(this);
        // Anything installed, upgraded or removed changes the installed list.
        Queue.Drained += () => _ = Installed.RefreshAsync();
        Installed.Changed += AnnounceNewUpdates;
        RestartUpdateChecks();

        // Clicking a notification while the app wasn't running starts it with this activation kind.
        var openUpdates = AppInstance.GetCurrent().GetActivatedEventArgs().Kind == ExtendedActivationKind.AppNotification;
        if (!Program.StartInTray || openUpdates)
            ShowWindow(openUpdates);
        _ = Installed.RefreshAsync();

        try
        {
            if (Directory.Exists(UpdateStagingRoot))
                Directory.Delete(UpdateStagingRoot, recursive: true); // left over from the last update
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the updater copy may still be exiting; next start clears it
        }
        _ = AutoCheckForAppUpdateAsync();
    }

    /// <summary>Shows and focuses the window, from the tray, a notification or a second launch. Safe from any thread.</summary>
    public void ShowWindow(bool openUpdates = false) => _dispatcher.TryEnqueue(() =>
    {
        Window.ShowAndActivate();
        if (openUpdates)
            Window.ShowPage("Updates");
    });

    /// <summary>(Re)starts the background update check after the interval setting changes. 0 hours = off.</summary>
    public void RestartUpdateChecks()
    {
        _checkTimer?.Stop();
        if (Settings.UpdateCheckHours <= 0)
            return;
        _checkTimer ??= _dispatcher.CreateTimer();
        _checkTimer.Interval = TimeSpan.FromHours(Settings.UpdateCheckHours);
        _checkTimer.Tick -= CheckTick;
        _checkTimer.Tick += CheckTick;
        _checkTimer.Start();
    }

    private void CheckTick(DispatcherQueueTimer sender, object args)
    {
        Log.Info("Scheduled update check");
        _ = Installed.RefreshAsync();
        _ = AutoCheckForAppUpdateAsync();
    }

    /// <summary>After a refresh, notifies about updates not announced before, but only when the window isn't on screen: then the badge says it.</summary>
    private void AnnounceNewUpdates()
    {
        if (Installed.IsLoading || Installed.Error is not null)
            return;
        var fresh = Installed.Items.Where(p => p.CanUpdate && _announced.Add($"{p.Id} {p.AvailableVersion}")).ToList();
        _tray?.SetUpdateCount(Installed.UpdateCount);
        if (fresh.Count == 0 || Window.IsOnScreen)
            return;
        try
        {
            // Off by the user, by group policy, etc.: Show would silently do nothing, so say why in the log.
            var setting = AppNotificationManager.Default.Setting;
            if (setting != AppNotificationSetting.Enabled)
            {
                Log.Info($"{fresh.Count} new updates; notifications are off for this app ({setting}), so only the tray tooltip shows them");
                return;
            }
            var names = string.Join(", ", fresh.Take(3).Select(p => p.Name)) + (fresh.Count > 3 ? $" and {fresh.Count - 3} more" : "");
            var notification = new AppNotificationBuilder()
                .AddArgument("action", "updates")
                .AddText(fresh.Count == 1 ? "1 update available" : $"{fresh.Count} updates available")
                .AddText(names)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
            Log.Info($"Notified: {fresh.Count} new updates (notification id {notification.Id})");
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't show a notification: {ex.Message}");
        }
    }

    public static void Quit()
    {
        IsExiting = true;
        Log.Info("Exiting");
        ((App)Current)._tray?.Dispose(); // otherwise a dead icon lingers in the notification area until hovered
        Window.Close();
        Current.Exit();
    }

    public static void Install(PackageItem p, string? version, InstallScope scope) =>
        Queue.Enqueue($"Install {p.Name} {version ?? p.AvailableVersion}".TrimEnd(),
            (progress, ct) => Winget.InstallAsync(p, version, scope, progress, ct));

    public static void Upgrade(PackageItem p) =>
        Queue.Enqueue($"Update {p.Name} {p.InstalledVersion} → {p.AvailableVersion}",
            (progress, ct) => Winget.UpgradeAsync(p, progress, ct));

    public static void Uninstall(PackageItem p) =>
        Queue.Enqueue($"Uninstall {p.Name} {p.InstalledVersion}".TrimEnd(),
            (progress, ct) => Winget.UninstallAsync(p, progress, ct));
}
