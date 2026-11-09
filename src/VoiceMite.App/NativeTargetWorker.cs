using System.Windows.Threading;
using VoiceMite.Windows;

namespace VoiceMite.App;

/// <summary>
/// Serializes accessibility and clipboard operations on a dedicated background STA. A stalled
/// provider can occupy this worker, but cannot block the UI, shutdown, or accumulate new work.
/// </summary>
internal sealed class NativeTargetWorker : IDisposable
{
    private readonly object gate = new();
    private readonly TaskCompletionSource<Dispatcher> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread thread;
    private SafeTargetService? service;
    private Dispatcher? dispatcher;
    private CancellationTokenSource? activeCancellation;
    private long generation;
    private bool occupied, disposed, clearBeforeNext = true, hasTarget;
    private string description = "";

    internal NativeTargetWorker()
    {
        thread = new Thread(ThreadMain) { IsBackground = true, Name = "VoiceMite target accessibility" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public bool HasTarget { get { lock (gate) return hasTarget; } }
    public string TargetDescription { get { lock (gate) return description; } }

    public Task<TargetCaptureResult> CaptureAsync() => RunAsync(
        (native, _) => Task.FromResult(native.Capture()),
        code => new TargetCaptureResult(false, code, ""), TimeSpan.FromMilliseconds(1200));

    public Task<NativeResult> PollAsync() => RunAsync(
        (native, _) => Task.FromResult(native.Poll()),
        code => new NativeResult(false, code), TimeSpan.FromMilliseconds(800));

    public Task<NativeResult> InsertAsync(string text, CancellationToken cancellationToken) => RunAsync(
        (native, token) => native.InsertAsync(text, token),
        code => new NativeResult(false, code), TimeSpan.FromSeconds(5), cancellationToken);

    public void Clear()
    {
        CancellationTokenSource? cancellation;
        lock (gate)
        {
            if (disposed) return;
            ++generation;
            hasTarget = false; description = ""; clearBeforeNext = true;
            cancellation = activeCancellation;
        }
        TryCancel(cancellation);
        // Do not enqueue a Clear operation behind a hung provider. The next admitted operation
        // clears the service first, and any stale in-flight operation clears on completion.
    }

    private void ThreadMain()
    {
        try
        {
            var current = Dispatcher.CurrentDispatcher;
            lock (gate)
            {
                if (disposed) { ready.TrySetCanceled(); return; }
                service = new SafeTargetService(); dispatcher = current;
            }
            ready.TrySetResult(current);
            Dispatcher.Run();
        }
        catch { ready.TrySetException(new InvalidOperationException("TargetUnsupported")); }
    }

    private async Task<T> RunAsync<T>(Func<SafeTargetService, CancellationToken, Task<T>> action,
        Func<string, T> fail, TimeSpan timeout, CancellationToken externalCancellation = default)
    {
        if (externalCancellation.IsCancellationRequested) return fail("Cancelled");
        long id;
        CancellationTokenSource cancellation;
        lock (gate)
        {
            if (disposed || occupied) return fail("TargetUnsupported");
            occupied = true; id = ++generation;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
            activeCancellation = cancellation;
        }

        // ExecuteAsync remains responsible for releasing occupied even after a UI-facing timeout.
        // Keeping this slot reserved until the real work exits is the queue bound of one.
        var token = cancellation.Token;
        var actual = ExecuteAsync(id, cancellation, action, fail);
        using var delayCancellation = new CancellationTokenSource();
        var deadline = Task.Delay(timeout, delayCancellation.Token);
        var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, token);
        var first = await Task.WhenAny(actual, deadline, cancelled).ConfigureAwait(false);
        if (first == actual)
        {
            delayCancellation.Cancel();
            return await actual.ConfigureAwait(false);
        }

        var code = cancellation.IsCancellationRequested ? "Cancelled" : "TargetTimeout";
        delayCancellation.Cancel();
        lock (gate)
        {
            if (id == generation)
            {
                ++generation; hasTarget = false; description = ""; clearBeforeNext = true;
            }
        }
        TryCancel(cancellation);
        return fail(code);
    }

    private async Task<T> ExecuteAsync<T>(long id, CancellationTokenSource cancellation,
        Func<SafeTargetService, CancellationToken, Task<T>> action, Func<string, T> fail)
    {
        try
        {
            var owner = await ready.Task.ConfigureAwait(false);
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = owner.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
            {
                try
                {
                    var native = service!;
                    lock (gate)
                    {
                        if (clearBeforeNext) { native.Clear(); clearBeforeNext = false; }
                        if (disposed || id != generation || cancellation.IsCancellationRequested)
                        {
                            native.Clear(); completion.TrySetResult(fail("Cancelled")); return;
                        }
                    }

                    var result = await action(native, cancellation.Token);
                    lock (gate)
                    {
                        if (disposed || id != generation || cancellation.IsCancellationRequested)
                        {
                            native.Clear(); hasTarget = false; description = "";
                            completion.TrySetResult(fail("Cancelled"));
                        }
                        else
                        {
                            hasTarget = native.HasTarget; description = native.TargetDescription;
                            completion.TrySetResult(result);
                        }
                    }
                }
                catch
                {
                    lock (gate)
                    {
                        service?.Clear(); hasTarget = false; description = ""; clearBeforeNext = true;
                    }
                    completion.TrySetResult(fail(cancellation.IsCancellationRequested ? "Cancelled" : "TargetUnsupported"));
                }
            }));
            return await completion.Task.ConfigureAwait(false);
        }
        catch { return fail(cancellation.IsCancellationRequested ? "Cancelled" : "TargetUnsupported"); }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(activeCancellation, cancellation)) activeCancellation = null;
                occupied = false;
            }
            cancellation.Dispose();
        }
    }

    private static void TryCancel(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        Dispatcher? owner;
        lock (gate)
        {
            if (disposed) return;
            disposed = true; ++generation; hasTarget = false; description = "";
            clearBeforeNext = true; cancellation = activeCancellation; owner = dispatcher;
        }
        TryCancel(cancellation);
        // Shutdown is posted, never joined. A permanently blocked UIA provider remains on a
        // background thread and cannot keep the process alive after the main UI exits.
        try { owner?.BeginInvokeShutdown(DispatcherPriority.Send); }
        catch (InvalidOperationException) { }
        GC.SuppressFinalize(this);
    }
}
