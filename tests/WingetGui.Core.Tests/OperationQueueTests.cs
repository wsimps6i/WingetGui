using WingetGui.Core;

namespace WingetGui.Core.Tests;

public class OperationQueueTests
{
    private static readonly OperationResult Ok = new(true, "Done.");

    [Fact]
    public async Task Runs_one_at_a_time_in_queue_order()
    {
        var queue = new OperationQueue();
        var order = new List<string>();
        var running = 0;
        var maxRunning = 0;
        Func<IProgress<OperationProgress>, CancellationToken, Task<OperationResult>> Work(string name) => async (_, _) =>
        {
            maxRunning = Math.Max(maxRunning, Interlocked.Increment(ref running));
            await Task.Delay(20);
            lock (order) order.Add(name);
            Interlocked.Decrement(ref running);
            return Ok;
        };

        queue.Enqueue("a", Work("a"));
        queue.Enqueue("b", Work("b"));
        queue.Enqueue("c", Work("c"));
        await queue.WhenIdle;

        Assert.Equal(["a", "b", "c"], order);
        Assert.Equal(1, maxRunning);
        Assert.All(queue.Operations, o => Assert.Equal(OperationStatus.Succeeded, o.Status));
        Assert.Equal(0, queue.ActiveCount);
    }

    [Fact]
    public async Task Cancelling_a_queued_operation_means_it_never_runs()
    {
        var queue = new OperationQueue();
        var gate = new TaskCompletionSource();
        var secondRan = false;
        queue.Enqueue("first", async (_, _) => { await gate.Task; return Ok; });
        var second = queue.Enqueue("second", (_, _) => { secondRan = true; return Task.FromResult(Ok); });

        queue.Cancel(second);
        gate.SetResult();
        await queue.WhenIdle;

        Assert.False(secondRan);
        Assert.Equal(OperationStatus.Cancelled, second.Status);
    }

    [Fact]
    public async Task Cancelling_the_running_operation_stops_it_and_the_queue_carries_on()
    {
        var queue = new OperationQueue();
        var started = new TaskCompletionSource();
        var running = queue.Enqueue("running", async (_, ct) => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); return Ok; });
        var next = queue.Enqueue("next", (_, _) => Task.FromResult(Ok));

        await started.Task;
        queue.Cancel(running);
        await queue.WhenIdle;

        Assert.Equal(OperationStatus.Cancelled, running.Status);
        Assert.Equal(OperationStatus.Succeeded, next.Status);
    }

    [Fact]
    public async Task A_failure_or_exception_is_recorded_and_does_not_stop_the_queue()
    {
        var queue = new OperationQueue();
        var failed = queue.Enqueue("failed", (_, _) => Task.FromResult(new OperationResult(false, "Install error")));
        var threw = queue.Enqueue("threw", (_, _) => throw new InvalidOperationException("boom"));
        var ok = queue.Enqueue("ok", (_, _) => Task.FromResult(Ok));
        await queue.WhenIdle;

        Assert.Equal((OperationStatus.Failed, "Install error"), (failed.Status, failed.Message));
        Assert.Equal((OperationStatus.Failed, "boom"), (threw.Status, threw.Message));
        Assert.Equal(OperationStatus.Succeeded, ok.Status);
    }

    [Fact]
    public async Task Drained_fires_once_the_queue_empties_and_a_new_enqueue_restarts_it()
    {
        var queue = new OperationQueue();
        var drained = 0;
        queue.Drained += () => drained++;

        // Real work is always async, so a batch (Update all) is fully queued before the first one finishes.
        var gate = new TaskCompletionSource();
        queue.Enqueue("a", async (_, _) => { await gate.Task; return Ok; });
        queue.Enqueue("b", (_, _) => Task.FromResult(Ok));
        gate.SetResult();
        await queue.WhenIdle;
        Assert.Equal(1, drained);

        var c = queue.Enqueue("c", (_, _) => Task.FromResult(Ok));
        await queue.WhenIdle;
        Assert.Equal(OperationStatus.Succeeded, c.Status);
        Assert.Equal(2, drained);
    }

    [Fact]
    public async Task ClearFinished_keeps_active_operations()
    {
        var queue = new OperationQueue();
        var gate = new TaskCompletionSource();
        queue.Enqueue("done", (_, _) => Task.FromResult(Ok));
        await queue.WhenIdle;
        var active = queue.Enqueue("active", async (_, _) => { await gate.Task; return Ok; });

        queue.ClearFinished();

        Assert.Equal([active], queue.Operations);
        gate.SetResult();
        await queue.WhenIdle;
    }
}
