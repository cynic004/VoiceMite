using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using VoiceMite.App;
using VoiceMite.Windows;

internal static class Program
{
    private sealed record TestResult(string Name, string Status, double ElapsedMilliseconds, string? Failure = null);
    private static readonly List<TestResult> Results = [];

    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("SIMULATED WORKER TESTS: real WPF STA dispatcher; fake native target service. No desktop input is injected.");
        await Test("dedicated_sta_and_single_thread", async () =>
        {
            using var worker = new NativeTargetWorker();
            var apartment = ApartmentState.Unknown; var firstThread = 0; var secondThread = 0;
            var first = await Invoke(worker, (_, _) =>
            {
                apartment = Thread.CurrentThread.GetApartmentState(); firstThread = Environment.CurrentManagedThreadId;
                return Task.FromResult(new NativeResult(true));
            });
            var second = await Invoke(worker, (_, _) =>
            {
                secondThread = Environment.CurrentManagedThreadId;
                return Task.FromResult(new NativeResult(true));
            });
            Assert(first.Success && second.Success && apartment == ApartmentState.STA, "Operation did not run on STA");
            Assert(firstThread == secondThread && firstThread != Environment.CurrentManagedThreadId, "Worker did not retain one independent thread");
            var capture = await worker.CaptureAsync();
            Assert(capture.Success && worker.HasTarget && worker.TargetDescription == "SIMULATED input field", "Successful state was not published");
        });

        await Test("500_fast_completions_without_disposed_token_races", async () =>
        {
            using var worker = new NativeTargetWorker();
            for (var iteration = 0; iteration < 500; iteration++)
                Assert((await Invoke(worker, (_, _) => Task.FromResult(new NativeResult(true)))).Success, "Fast operation failed");
        });

        await Test("timeout_bounded_queue_late_state_isolation_and_recovery", async () =>
        {
            using var worker = new NativeTargetWorker();
            var release = new TaskCompletionSource<NativeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = Invoke(worker, (native, _) =>
            {
                // A late completion really has stale native target state to publish. The guard
                // must clear it; simply testing a service with no target would miss the defect.
                native.Capture(); entered.SetResult(); return release.Task;
            }, 80);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert((await Invoke(worker, (_, _) => Task.FromResult(new NativeResult(true)))).Code == "TargetUnsupported", "Concurrent work was admitted");
            Assert((await pending).Code == "TargetTimeout" && !worker.HasTarget, "Timeout did not invalidate cached state");
            Assert((await worker.PollAsync()).Code == "TargetUnsupported", "Timed-out live operation released its queue slot");
            release.SetResult(new(true));
            await WaitFree(worker);
            Assert(!worker.HasTarget, "Late completion republished a stale target");
            Assert((await worker.PollAsync()).Code == "TargetMissing", "Native state was not cleared before recovery");
        });

        await Test("clear_cancels_active_work_and_clears_native_state", async () =>
        {
            using var worker = new NativeTargetWorker();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = Invoke(worker, async (native, token) =>
            {
                native.Capture(); entered.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return new NativeResult(true);
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            worker.Clear();
            Assert(!worker.HasTarget && (await pending).Code == "Cancelled", "Clear did not invalidate and cancel work");
            await WaitFree(worker);
            Assert((await worker.PollAsync()).Code == "TargetMissing", "Clear left a native target behind");
        });

        await Test("nonblocking_disposal_with_unresponsive_operation", async () =>
        {
            var worker = new NativeTargetWorker();
            var neverCompletes = new TaskCompletionSource<NativeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = Invoke(worker, (native, _) =>
            {
                native.Capture(); entered.SetResult(); return neverCompletes.Task;
            }, 1000);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var watch = Stopwatch.StartNew(); worker.Dispose(); watch.Stop();
            Assert(watch.ElapsedMilliseconds < 100, "Dispose waited for an unresponsive operation");
            Assert((await pending).Code == "Cancelled" && !worker.HasTarget, "Dispose failed to return cancellation and clear public state");
        });

        var passed = Results.Count(x => x.Status == "passed");
        var failed = Results.Count - passed;
        var reportArgument = Array.IndexOf(args, "--report");
        if (reportArgument >= 0 && reportArgument + 1 < args.Length)
        {
            var path = Path.GetFullPath(args[reportArgument + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                suite = "VoiceMite.WorkerTests",
                testedAtUtc = DateTimeOffset.UtcNow,
                environment = Environment.OSVersion.VersionString,
                classification = "simulated-native-service-with-real-Windows-STA-dispatcher",
                noDesktopInputInjected = true,
                liveVendorOrMicrophoneTest = false,
                passed, failed, results = Results
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"RESULT: {passed} worker scenarios passed; {failed} failed (500 fast-completion cycles included).");
        return failed == 0 ? 0 : 1;
    }

    private static Task<NativeResult> Invoke(NativeTargetWorker worker,
        Func<SafeTargetService, CancellationToken, Task<NativeResult>> action,
        int timeoutMilliseconds = 1000, CancellationToken token = default)
    {
        // Exercise the production scheduler, not a copy of its implementation. Reflection only
        // supplies a controlled slow operation; all admission/cancellation/timeout code is linked.
        var method = typeof(NativeTargetWorker).GetMethod("RunAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(typeof(NativeResult));
        return (Task<NativeResult>)method.Invoke(worker,
            [action, new Func<string, NativeResult>(code => new(false, code)), TimeSpan.FromMilliseconds(timeoutMilliseconds), token])!;
    }

    private static async Task WaitFree(NativeTargetWorker worker)
    {
        var occupied = typeof(NativeTargetWorker).GetField("occupied", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var watch = Stopwatch.StartNew();
        while ((bool)occupied.GetValue(worker)!)
        {
            if (watch.ElapsedMilliseconds > 1500) throw new InvalidOperationException("Worker did not release its single operation slot");
            await Task.Delay(10);
        }
    }

    private static void Assert(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException(failure);
    }

    private static async Task Test(string name, Func<Task> action)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            await action();
            Results.Add(new(name, "passed", watch.Elapsed.TotalMilliseconds));
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            Results.Add(new(name, "failed", watch.Elapsed.TotalMilliseconds, error.GetType().Name + ": " + error.Message));
            Console.WriteLine("FAIL " + name + ": " + error.Message);
        }
    }
}
