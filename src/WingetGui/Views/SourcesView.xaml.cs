using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WingetGui.Core;

namespace WingetGui.Views;

/// <summary>winget sources: listed through the COM API; add/remove/reset through an elevated winget.exe (needs admin).</summary>
public sealed partial class SourcesView : UserControl
{
    private bool _busy;

    public SourcesView()
    {
        InitializeComponent();
        Status.IsOpen = false;
        Loaded += (_, _) => _ = LoadAsync();
    }

    public static string ExplicitText(bool isExplicit) => isExplicit ? " · explicit (searched only when named)" : "";

    private async Task LoadAsync()
    {
        try
        {
            List.ItemsSource = await App.Winget.GetSourcesAsync();
        }
        catch (Exception ex)
        {
            Show(new OperationResult(false, ex.Message));
        }
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => RemoveButton.IsEnabled = List.SelectedItem is not null;

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = new TextBox { Header = "Name", PlaceholderText = "contoso" };
        var url = new TextBox { Header = "Address", PlaceholderText = "https://winget.contoso.com/api" };
        var type = new ComboBox
        {
            Header = "Type",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "Microsoft.Rest", "Microsoft.PreIndexed.Package" },
            SelectedIndex = 0,
        };
        var isExplicit = new CheckBox { Content = "Explicit: only search it when asked for by name" };
        var dialog = new ContentDialog
        {
            Title = "Add a source",
            Content = new StackPanel { Spacing = 12, MinWidth = 380, Children = { name, url, type, isExplicit } },
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await ConfirmAsync(dialog))
            await RunAsync(() => WingetCli.AddSourceAsync(name.Text.Trim(), url.Text.Trim(), (string)type.SelectedItem, isExplicit.IsChecked == true));
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not SourceInfo source)
            return;
        var dialog = new ContentDialog
        {
            Title = $"Remove the {source.Name} source?",
            Content = "Packages from it will no longer show in search or get updates. Reset to defaults brings back winget's own sources.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await ConfirmAsync(dialog))
            await RunAsync(() => WingetCli.RemoveSourceAsync(source.Name));
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Reset sources to winget's defaults?",
            Content = "This removes every source you added and restores msstore, winget and winget-font.",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await ConfirmAsync(dialog))
            await RunAsync(WingetCli.ResetSourcesAsync);
    }

    /// <summary>One dialog or elevated command at a time (a second ContentDialog throws, and from async void that ends the app).</summary>
    private async Task<bool> ConfirmAsync(ContentDialog dialog)
    {
        if (_busy)
            return false;
        _busy = true;
        try
        {
            dialog.XamlRoot = XamlRoot;
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RunAsync(Func<Task<OperationResult>> action)
    {
        _busy = true;
        Show(new OperationResult(true, "Waiting for administrator permission…"), InfoBarSeverity.Informational);
        try
        {
            var result = await action();
            Show(result);
            if (result.Success)
            {
                await LoadAsync();
                _ = App.Installed.RefreshAsync(); // sources decide what has updates
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private void Show(OperationResult result, InfoBarSeverity? severity = null)
    {
        Status.Severity = severity ?? (result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        Status.Message = result.Message;
        Status.IsOpen = true;
    }
}
