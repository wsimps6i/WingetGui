using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WingetGui.Core;
using WingetGui.Views;
using WinRT.Interop;

namespace WingetGui;

public sealed partial class MainWindow : Window
{
    // One instance of each view for the window's lifetime, so a search or a selection survives switching pages.
    private readonly Dictionary<string, UIElement> _views = new()
    {
        ["Installed"] = new PackagesView(PackageMode.Installed),
        ["Updates"] = new PackagesView(PackageMode.Updates),
        ["Search"] = new PackagesView(PackageMode.Search),
        ["Activity"] = new ActivityView(),
        ["Settings"] = new SettingsView(),
    };

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        var scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1240 * scale), (int)(780 * scale)));
        ApplyTheme(App.Settings.Theme);

        App.Installed.Changed += () =>
        {
            var n = App.Installed.UpdateCount;
            UpdatesBadge.Value = n;
            UpdatesBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        };
        App.Queue.ActiveChanged += () =>
        {
            var n = App.Queue.ActiveCount;
            ActivityBadge.Value = n;
            ActivityBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        };
        Nav.SelectedItem = InstalledItem;
    }

    public void ShowActivity() =>
        Nav.SelectedItem = Nav.FooterMenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == "Activity");

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args) =>
        Host.Content = _views[args.IsSettingsSelected ? "Settings" : (string)args.SelectedItemContainer.Tag];

    /// <summary>WinUI only allows the app-wide theme at startup, so a change from Settings is applied to the window's content and caption buttons.</summary>
    public void ApplyTheme(AppTheme theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        AppWindow.TitleBar.PreferredTheme = theme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);
}
