using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WingetGui.Core;

public enum OperationStatus { Queued, Running, Succeeded, Failed, Cancelled }

public sealed partial class QueuedOperation : ObservableObject
{
    public required string Title { get; init; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsActive), nameof(IsIndeterminate))]
    public partial OperationStatus Status { get; set; }

    [ObservableProperty]
    public partial string? Stage { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsIndeterminate), nameof(ProgressValue))]
    public partial double? Percent { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    public bool IsActive => Status is OperationStatus.Queued or OperationStatus.Running;
    public bool IsIndeterminate => Status == OperationStatus.Running && Percent is null;
    public double ProgressValue => (Percent ?? 0) * 100;

    internal Func<IProgress<OperationProgress>, CancellationToken, Task<OperationResult>> Work { get; init; } = null!; // always set by Enqueue
    internal CancellationTokenSource Cancellation { get; } = new();
}

/// <summary>
/// Runs install/upgrade/uninstall operations one at a time, in the order queued: winget runs
/// installers one by one anyway, and two at once fight over the same installer locks.
/// shortcut: call only from one thread (the UI thread); add a lock if a background caller appears (tray, Phase 3).
/// </summary>
public sealed class OperationQueue
{
    private Task? _run;

    public ObservableCollection<QueuedOperation> Operations { get; } = [];

    /// <summary>Raised whenever the number of queued + running operations changes.</summary>
    public event Action? ActiveChanged;

    /// <summary>Raised when the queue empties after running at least one operation, so the installed list can be refreshed.</summary>
    public event Action? Drained;

    public int ActiveCount => Operations.Count(o => o.IsActive);

    /// <summary>Completes when nothing is queued or running.</summary>
    public Task WhenIdle => _run ?? Task.CompletedTask;

    public QueuedOperation Enqueue(string title, Func<IProgress<OperationProgress>, CancellationToken, Task<OperationResult>> work)
    {
        var op = new QueuedOperation { Title = title, Work = work };
        Operations.Insert(0, op); // newest first in the Activity list
        Log.Info($"Queued: {title}");
        ActiveChanged?.Invoke();
        if (_run is null || _run.IsCompleted)
            _run = RunAsync();
        return op;
    }

    public void Cancel(QueuedOperation op)
    {
        op.Cancellation.Cancel();
        if (op.Status == OperationStatus.Queued)
        {
            op.Status = OperationStatus.Cancelled;
            Log.Info($"Cancelled before it started: {op.Title}");
            ActiveChanged?.Invoke();
        }
    }

    public void ClearFinished()
    {
        foreach (var op in Operations.Where(o => !o.IsActive).ToList())
            Operations.Remove(op);
    }

    private async Task RunAsync()
    {
        // Oldest queued first; the list itself is newest first.
        while (Operations.LastOrDefault(o => o.Status == OperationStatus.Queued) is { } op)
            await RunOneAsync(op);
        Drained?.Invoke();
    }

    private async Task RunOneAsync(QueuedOperation op)
    {
        op.Status = OperationStatus.Running;
        Log.Info($"Started: {op.Title}");
        var progress = new Progress<OperationProgress>(p =>
        {
            op.Stage = p.Stage;
            op.Percent = p.Percent;
        });
        try
        {
            var result = await op.Work(progress, op.Cancellation.Token);
            op.Message = result.Message;
            op.Status = result.Success ? OperationStatus.Succeeded : OperationStatus.Failed;
        }
        catch (OperationCanceledException)
        {
            op.Message = "Cancelled.";
            op.Status = OperationStatus.Cancelled;
        }
        catch (Exception ex)
        {
            op.Message = ex.Message;
            op.Status = OperationStatus.Failed;
        }
        op.Stage = null;
        op.Percent = null;
        if (op.Status == OperationStatus.Failed)
            Log.Error($"Failed: {op.Title}: {op.Message}");
        else
            Log.Info($"{op.Status}: {op.Title}: {op.Message}");
        ActiveChanged?.Invoke();
    }
}
