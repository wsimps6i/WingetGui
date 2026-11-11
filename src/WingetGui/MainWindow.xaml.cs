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
        ["Sources"] = new SourcesView(),
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
        AppWindow.SetIcon(App.IconPath);
        AppWindow.Closing += OnClosing;
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

    /// <summary>Visible and not minimised: new updates then show as the badge rather than a notification.</summary>
    public bool IsOnScreen => AppWindow.IsVisible && AppWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Minimized };

    public void ShowAndActivate()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        AppWindow.Show();
        Activate();
        // Activate alone left the window behind others after a notification click (it opened, but unseen).
        // A click on our notification or tray icon gives this process the right to take the foreground; use it.
        SetForegroundWindow(WindowNative.GetWindowHandle(this));
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    public void ShowPage(string tag) =>
        Nav.SelectedItem = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().First(i => (string)i.Tag == tag);

    private bool _confirmingExit;

    /// <summary>Close hides to the tray (setting), or exits, asking first if installs are still queued or running.</summary>
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (App.IsExiting)
            return;
        args.Cancel = true; // must be set before any await
        if (App.Settings.CloseToTray)
        {
            AppWindow.Hide();
            return;
        }
        if (App.Queue.ActiveCount == 0)
        {
            App.Quit();
            return;
        }
        if (_confirmingExit)
            return; // one dialog at a time
        _confirmingExit = true;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Exit while installs are running?",
            Content = $"{App.Queue.ActiveCount} operation(s) are still queued or running. Exiting cancels those that haven't started; one already running may finish or be cut short.",
            PrimaryButtonText = "Exit anyway",
            CloseButtonText = "Keep running",
            DefaultButton = ContentDialogButton.Close,
        };
        var exit = await dialog.ShowAsync() == ContentDialogResult.Primary;
        _confirmingExit = false;
        if (exit)
            App.Quit();
    }

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
