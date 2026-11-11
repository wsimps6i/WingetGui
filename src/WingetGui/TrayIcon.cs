using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace WingetGui;

/// <summary>The notification-area icon: click to open, right-click for Open / Check for updates / Exit.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;

    public TrayIcon(App app)
    {
        var open = new RelayCommand(() => app.ShowWindow());
        _icon = new TaskbarIcon
        {
            ToolTipText = "Winget GUI",
            IconSource = new BitmapImage(new Uri(App.IconPath)),
            LeftClickCommand = open,
            NoLeftClickDelay = true,
            // A native Win32 menu: works while the window is hidden, and the items need Commands rather than Click handlers.
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = new MenuFlyout
            {
                Items =
                {
                    new MenuFlyoutItem { Text = "Open Winget GUI", Command = open },
                    new MenuFlyoutItem { Text = "Check for updates now", Command = new RelayCommand(() => _ = App.Installed.RefreshAsync()) },
                    new MenuFlyoutSeparator(),
                    new MenuFlyoutItem { Text = "Exit", Command = new RelayCommand(App.Quit) },
                },
            },
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    public void SetUpdateCount(int count) =>
        _icon.ToolTipText = count switch
        {
            0 => "Winget GUI: up to date",
            1 => "Winget GUI: 1 update available",
            _ => $"Winget GUI: {count} updates available",
        };

    public void Dispose() => _icon.Dispose();
}
