using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using VoiceMite.Core;

namespace VoiceMite.App;

/// <summary>Opt-in integration check against an isolated fixture. Uses the real orb and STA worker.</summary>
internal static class TargetSmoke
{
    internal static bool Failed { get; private set; }

    internal static async Task RunAsync(AppController controller, string fixtureDll, string dotnetPath, string reportDirectory)
    {
        Failed = false;
        var results = new List<CheckResult>();
        Process? fixture = null;
        Directory.CreateDirectory(reportDirectory);
        void Check(string name, bool passed, string detail = "") => results.Add(new(name, passed ? "passed" : "failed", detail));
        try
        {
            fixtureDll = Path.GetFullPath(fixtureDll);
            dotnetPath = Path.GetFullPath(dotnetPath);
            if (!File.Exists(fixtureDll) || !File.Exists(dotnetPath)) throw new FileNotFoundException("Target fixture or dotnet host is missing.");
            var start = new ProcessStartInfo(dotnetPath) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(fixtureDll); start.ArgumentList.Add("--target");
            start.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(dotnetPath)!;
            fixture = Process.Start(start) ?? throw new InvalidOperationException("Fixture launch failed.");
            var ready = await WaitUntilAsync(() => { fixture.Refresh(); return fixture.MainWindowHandle != IntPtr.Zero; }, TimeSpan.FromSeconds(8));
            if (!ready) throw new InvalidOperationException("Fixture window did not appear.");
            var fixtureWindow = fixture.MainWindowHandle;
            var root = AutomationElement.FromHandle(fixtureWindow);
            if (root.TryGetCurrentPattern(TransformPattern.Pattern, out var transformObject))
                ((TransformPattern)transformObject).Move(450, 220);
            var field = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "plain"))
                ?? throw new InvalidOperationException("Fixture input was not found.");
            ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).SetValue(""); // Isolated fixture setup only.

            const string expected = "鼠标确认 target 123🙂";
            controller.Cancel();
            controller.Settings.Stt.Kind = ServiceKind.Demo;
            controller.Settings.Llm.Kind = ServiceKind.Demo;
            controller.Settings.TranscribeOnly = true;
            controller.Settings.Mode = InputMode.Verbatim;
            controller.Settings.LearnPreferences = false;
            controller.ShowPanel();
            controller.Panel.SetTranscript(expected);
            await controller.RewriteAsync(InputMode.Verbatim);
            Check("Local original-text draft is ready without API or recording", controller.Session.State == SessionState.Ready && controller.Panel.ResultText == expected && !controller.Audio.IsRecording);

            await controller.SelectTargetAsync();
            Check("Target selection hides panel and waits without recording", controller.SelectingTarget && !controller.Panel.IsVisible && !controller.Audio.IsRecording && !controller.Targets.HasTarget);
            var orbWindow = new WindowInteropHelper(controller.Orb).Handle;
            Check("Floating orb has WS_EX_NOACTIVATE", (GetWindowLongPtr(orbWindow, -20).ToInt64() & 0x08000000L) != 0);
            var bounds = field.Current.BoundingRectangle;
            Click(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            await Task.Delay(120);
            Check("Mouse selects isolated fixture input", GetForegroundWindow() == fixtureWindow && Automation.Compare(AutomationElement.FocusedElement, field));
            var foregroundBeforeOrb = GetForegroundWindow();
            var orbPoint = controller.Orb.PointToScreen(new Point(40, 40));
            Click(orbPoint.X, orbPoint.Y); // Actual mouse message reaches FloatingWindow handlers.
            var confirmed = await WaitUntilAsync(() => !controller.SelectingTarget && controller.Panel.IsVisible && (controller.Targets.HasTarget || controller.TargetCode != null), TimeSpan.FromSeconds(4));
            Check("Orb click keeps original foreground window", foregroundBeforeOrb == fixtureWindow && GetForegroundWindow() == fixtureWindow);
            Check("Orb click captures target through real STA worker without recording", confirmed && controller.Targets.HasTarget && controller.Session.State == SessionState.Ready && !controller.Audio.IsRecording, controller.TargetCode ?? "");
            if (!controller.Targets.HasTarget) throw new InvalidOperationException("Real target capture failed: " + controller.TargetCode);

            // Activating the tool models the user reviewing/editing and clicking Insert.
            controller.Panel.Activate();
            await Task.Delay(100);
            Check("Review panel can take foreground before explicit insert", GetForegroundWindow() == new WindowInteropHelper(controller.Panel).Handle);
            await controller.InsertAsync();
            var actual = ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
            Check("Explicit insert restores target and pastes through real STA worker", actual == expected && controller.NoticeKey == "Inserted", controller.TargetCode ?? "");
            Check("Successful insert consumes target", !controller.Targets.HasTarget);
            await controller.InsertAsync();
            Check("Second insert cannot duplicate consumed target", ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).Current.Value == expected && !controller.Targets.HasTarget && !controller.Audio.IsRecording);
        }
        catch (Exception error)
        {
            results.Add(new("Target smoke exception", "failed", error.GetType().Name + ": " + error.Message));
        }
        finally
        {
            if (fixture != null)
            {
                try { if (!fixture.HasExited) { fixture.Kill(true); await fixture.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); } }
                catch (Exception error) { results.Add(new("Isolated fixture cleanup", "failed", error.GetType().Name)); }
                fixture.Dispose();
            }
            Failed = results.Any(result => result.status == "failed");
            await File.WriteAllTextAsync(Path.Combine(reportDirectory, "target-tests.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            controller.Exit();
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout) { if (predicate()) return true; await Task.Delay(30); }
        return predicate();
    }
    private static void Click(double x, double y)
    {
        if (!SetCursorPos((int)Math.Round(x), (int)Math.Round(y))) throw new InvalidOperationException("Could not position test mouse.");
        INPUT[] inputs = [new() { Type = 0, Data = new InputUnion { Mouse = new MOUSEINPUT { Flags = 0x0002 } } }, new() { Type = 0, Data = new InputUnion { Mouse = new MOUSEINPUT { Flags = 0x0004 } } }];
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length) throw new InvalidOperationException("Could not click isolated test target.");
    }
    private sealed record CheckResult(string name, string status, string detail);
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
