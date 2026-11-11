using Microsoft.UI.Xaml;
using WingetGui.Core;

namespace WingetGui;

public partial class App : Application
{
    public static AppSettings Settings { get; } = AppSettings.Load();
    public static WingetService Winget { get; } = new();
    public static OperationQueue Queue { get; } = new();
    public static InstalledStore Installed { get; } = new(Winget);
    public static MainWindow Window { get; private set; } = null!;

    public static string Version { get; } =
        typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public App()
    {
        InitializeComponent();
        Log.Init(Path.Combine(AppSettings.DataRoot, "logs"), Settings.LogRetentionDays);
        Log.Info($"Winget GUI {Version} started");
        UnhandledException += (_, e) => Log.Error($"Unhandled: {e.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
        // Anything installed, upgraded or removed changes the installed list.
        Queue.Drained += () => _ = Installed.RefreshAsync();
        _ = Installed.RefreshAsync();
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
