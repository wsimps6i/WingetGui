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
        VersionText.Text = $"Winget GUI {App.Version}";
    }

    private static readonly int[] CheckHours = [0, 1, 3, 6, 12, 24];

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
