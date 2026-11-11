using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using WingetGui.Core;

namespace WingetGui;

/// <summary>
/// Entry point, in place of the XAML-generated Main (DISABLE_XAML_GENERATED_MAIN in the csproj).
/// A command-line switch runs without a window and exits with a code for scripts. Otherwise the app
/// is single-instance (MarkItDown-GUI's pattern): starting it again shows the copy that's already
/// running, even when that one is hidden in the notification area.
/// </summary>
public static partial class Program
{
    /// <summary>Started with -Tray (Start at sign-in): stay hidden in the notification area.</summary>
    public static bool StartInTray { get; private set; }

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var commandLine = CommandLine.Parse(args);
        if (commandLine.IsSilent)
            return RunSilent(commandLine);

        // Per exe path, so a dev build never hands over to an installed copy. (Not string.GetHashCode: .NET randomises it per process.)
        var pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.ProcessPath!.ToLowerInvariant())))[..16];
        var instance = AppInstance.FindOrRegisterForKey("WingetGui-" + pathHash);
        if (!instance.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            // The redirect finishes on a worker thread; this STA thread only waits for it.
            Task.Run(() => instance.RedirectActivationToAsync(activation).AsTask()).Wait();
            BringToFront(instance.ProcessId);
            return 0;
        }

        StartInTray = commandLine.StartInTray;
        // Here rather than in App, so the shortcut and notification setup below can log.
        Log.Init(Path.Combine(AppSettings.DataRoot, "logs"), App.Settings.LogRetentionDays);
        StartMenuShortcut.SetProcessId(); // before Register, so the Windows App SDK uses our fixed ID
        App? app = null;
        // Hooked before Register, as the Windows App SDK requires. Raised on a worker thread.
        AppNotificationManager.Default.NotificationInvoked += (_, _) =>
        {
            Log.Info("Notification clicked");
            app?.ShowWindow(openUpdates: true);
        };
        try
        {
            AppNotificationManager.Default.Register("Winget GUI", new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")));
        }
        catch (Exception ex)
        {
            Log.Error($"Notifications unavailable: {ex.Message}"); // the app works without them
        }
        StartMenuShortcut.Ensure(); // after Register: the shortcut carries the click handler it registered

        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            app = new App();
            // Raised on a worker thread each time another launch redirects here.
            instance.Activated += (_, redirected) =>
            {
                Log.Info($"Activated by another launch ({redirected.Kind})");
                app.ShowWindow(openUpdates: redirected.Kind == ExtendedActivationKind.AppNotification);
            };
        });
        return 0;
    }

    private static int RunSilent(ParsedCommandLine commandLine)
    {
        // A WinExe has no console of its own; borrow the one it was started from, if any (a scheduled task has none: the log still gets everything).
        AttachConsole(ATTACH_PARENT_PROCESS);
        var settings = AppSettings.Load();
        Log.Init(Path.Combine(AppSettings.DataRoot, "logs"), settings.LogRetentionDays);
        Console.WriteLine();
        var code = Task.Run(() => SilentRunner.RunAsync(commandLine, Console.Out, new WingetService(), settings)).GetAwaiter().GetResult();
        Console.Out.Flush();
        return code;
    }

    /// <summary>
    /// Brings the running copy's window to the front. Windows only lets the process the user just
    /// started change the foreground window, so this launch does it (see MarkItDown-GUI's Program.cs).
    /// The running copy shows itself first if it was hidden in the tray, so retry briefly until it has a window.
    /// </summary>
    private static void BringToFront(uint processId)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var process = Process.GetProcessById((int)processId);
                var window = process.MainWindowHandle;
                if (window != 0)
                {
                    if (IsIconic(window))
                        ShowWindowNative(window, SW_RESTORE);
                    SetForegroundWindow(window);
                    return;
                }
            }
            catch (ArgumentException)
            {
                return; // it has just closed
            }
            Thread.Sleep(100);
        }
    }

    private const int SW_RESTORE = 9;
    private const int ATTACH_PARENT_PROCESS = -1;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindowNative(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);
}
