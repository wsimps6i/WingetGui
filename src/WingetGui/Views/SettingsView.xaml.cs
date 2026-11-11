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
        VersionText.Text = $"Winget GUI {App.Version}";
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
