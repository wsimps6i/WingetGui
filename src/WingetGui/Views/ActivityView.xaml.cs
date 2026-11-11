using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WingetGui.Core;

namespace WingetGui.Views;

/// <summary>The operation queue: what's queued, running and finished, with cancel.</summary>
public sealed partial class ActivityView : UserControl
{
    public OperationQueue Queue => App.Queue;

    public ActivityView()
    {
        InitializeComponent();
        Queue.Operations.CollectionChanged += (_, _) => ShowEmpty();
        ShowEmpty();
    }

    public static string StatusText(OperationStatus status, string? stage) => status switch
    {
        OperationStatus.Running => stage ?? "Starting…",
        OperationStatus.Queued => "Waiting",
        _ => status.ToString(),
    };

    private void ShowEmpty() => EmptyText.Visibility = Queue.Operations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Cancel_Click(object sender, RoutedEventArgs e) => Queue.Cancel((QueuedOperation)((FrameworkElement)sender).Tag);

    private void ClearFinished_Click(object sender, RoutedEventArgs e) => Queue.ClearFinished();
}
