using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WingetGui.Core;

namespace WingetGui.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        // Set before the handlers can fire usefully; both handlers only save when the value changed.
        ThemeButtons.SelectedIndex = (int)App.Settings.Theme;
        RetentionBox.Value = App.Settings.LogRetentionDays;
        ScopeBox.SelectedIndex = (int)App.Settings.DefaultInstallScope;
        CloseToTraySwitch.IsOn = App.Settings.CloseToTray;
        StartupSwitch.IsOn = StartupRegistration.IsEnabled;
        CheckIntervalBox.SelectedIndex = Math.Max(0, Array.IndexOf(CheckHours, App.Settings.UpdateCheckHours));
        VersionText.Text = $"Version {App.Version}";
        AppUpdateSwitch.IsOn = App.Settings.CheckForAppUpdates;
        App.AppUpdateChanged += ShowAppUpdate;
        ShowAppUpdate();
    }

    private static readonly int[] CheckHours = [0, 1, 3, 6, 12, 24];

    private void ShowAppUpdate()
    {
        var update = App.AppUpdate;
        InstallAppButton.Visibility = ReleaseLink.Visibility = update is null ? Visibility.Collapsed : Visibility.Visible;
        if (update is null)
            return;
        InstallAppButton.Content = $"Download and install {update.Version.ToString(3)}";
        InstallAppButton.IsEnabled = App.CanSelfUpdate;
        ReleaseLink.NavigateUri = update.ReleasePage;
        AppUpdateStatus.Text = App.CanSelfUpdate
            ? $"Winget GUI {update.Version.ToString(3)} is available."
            : $"Winget GUI {update.Version.ToString(3)} is available. This is a development build, so it can't update itself; install a release instead.";
    }

    private void AppUpdate_Toggled(object sender, RoutedEventArgs e)
    {
        if (AppUpdateSwitch.IsOn == App.Settings.CheckForAppUpdates)
            return;
        App.Settings.CheckForAppUpdates = AppUpdateSwitch.IsOn;
        App.Settings.Save();
    }

    private async void CheckApp_Click(object sender, RoutedEventArgs e)
    {
        CheckAppButton.IsEnabled = false;
        AppUpdateStatus.Text = "Checking…";
        try
        {
            if (await App.CheckForAppUpdateAsync() is null)
                AppUpdateStatus.Text = $"You have the latest version ({App.Version}).";
        }
        catch (Exception ex)
        {
            AppUpdateStatus.Text = $"Couldn't check for updates: {ex.Message}";
            Log.Error($"Winget GUI update check failed: {ex}");
        }
        CheckAppButton.IsEnabled = true;
    }

    /// <summary>Downloads and verifies the release, then hands over to the downloaded copy (-ApplyUpdate), which swaps the files once this one has exited.</summary>
    private async void InstallApp_Click(object sender, RoutedEventArgs e)
    {
        if (App.AppUpdate is not { } update || !App.CanSelfUpdate)
            return;
        if (App.Queue.ActiveCount > 0)
        {
            AppUpdateStatus.Text = "Installs or updates are still running. Update Winget GUI once they finish (see Activity).";
            return;
        }
        InstallAppButton.IsEnabled = CheckAppButton.IsEnabled = false;
        AppUpdateProgress.Value = 0;
        AppUpdateProgress.Visibility = Visibility.Visible;
        AppUpdateStatus.Text = $"Downloading Winget GUI {update.Version.ToString(3)}…";
        try
        {
            var folder = await App.Updater.DownloadAsync(update, App.UpdateStagingRoot, new Progress<double>(p => AppUpdateProgress.Value = p * 100));
            AppUpdateStatus.Text = "Verified. Restarting Winget GUI to finish the update…";
            Log.Info($"Applying Winget GUI {update.Version.ToString(3)} from {folder}");
            var updater = new ProcessStartInfo(Path.Combine(folder, SelfUpdate.ExeName)) { UseShellExecute = false, WorkingDirectory = folder };
            updater.ArgumentList.Add("-ApplyUpdate");
            updater.ArgumentList.Add(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
            Process.Start(updater);
            App.Quit(); // the updater waits for this process to exit before swapping files
        }
        catch (Exception ex)
        {
            AppUpdateStatus.Text = $"Update failed: {ex.Message}";
            Log.Error($"Winget GUI update failed: {ex}");
            AppUpdateProgress.Visibility = Visibility.Collapsed;
            InstallAppButton.IsEnabled = CheckAppButton.IsEnabled = true;
        }
    }

    private void CloseToTray_Toggled(object sender, RoutedEventArgs e)
    {
        if (CloseToTraySwitch.IsOn == App.Settings.CloseToTray)
            return;
        App.Settings.CloseToTray = CloseToTraySwitch.IsOn;
        App.Settings.Save();
    }

    private void Startup_Toggled(object sender, RoutedEventArgs e)
    {
        if (StartupSwitch.IsOn == StartupRegistration.IsEnabled)
            return;
        try
        {
            StartupRegistration.Set(StartupSwitch.IsOn);
            Log.Info($"Start at sign-in {(StartupSwitch.IsOn ? "on" : "off")}");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error($"Couldn't change Start at sign-in: {ex.Message}");
            StartupSwitch.IsOn = StartupRegistration.IsEnabled; // e.g. blocked by policy
        }
    }

    private void Check_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hours = CheckHours[Math.Max(0, CheckIntervalBox.SelectedIndex)];
        if (hours == App.Settings.UpdateCheckHours)
            return;
        App.Settings.UpdateCheckHours = hours;
        App.Settings.Save();
        ((App)Application.Current).RestartUpdateChecks();
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var theme = (AppTheme)Math.Max(0, ThemeButtons.SelectedIndex);
        if (theme == App.Settings.Theme)
            return;
        App.Settings.Theme = theme;
        App.Settings.Save();
        App.Window.ApplyTheme(theme);
    }

    private void Scope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var scope = (InstallScope)Math.Max(0, ScopeBox.SelectedIndex);
        if (scope == App.Settings.DefaultInstallScope)
            return;
        App.Settings.DefaultInstallScope = scope;
        App.Settings.Save();
    }

    private void Retention_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(args.NewValue))
        {
            sender.Value = App.Settings.LogRetentionDays; // emptied the box
            return;
        }
        var days = (int)args.NewValue;
        if (days == App.Settings.LogRetentionDays)
            return;
        App.Settings.LogRetentionDays = days;
        App.Settings.Save();
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Log.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Folder}\"") { UseShellExecute = true });
    }
}
