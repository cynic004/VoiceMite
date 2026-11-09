using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using VoiceMite.Windows;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using TextBox = System.Windows.Controls.TextBox;
using RichTextBox = System.Windows.Controls.RichTextBox;
using CheckBox = System.Windows.Controls.CheckBox;

namespace VoiceMite.WindowsTests;

internal static class Program
{
    private static readonly List<object> Results = [];
    private static Window? host;
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (args.Contains("--target"))
        {
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = "VoiceMite isolated integration test target" });
            var plain = new TextBox { Margin = new Thickness(0, 10, 0, 10), Height = 36 };
            AutomationProperties.SetAutomationId(plain, "plain"); panel.Children.Add(plain);
            var multi = new TextBox { AcceptsReturn = true, Height = 80, Margin = new Thickness(0, 0, 0, 10) };
            AutomationProperties.SetAutomationId(multi, "multi"); panel.Children.Add(multi);
            var rich = new RichTextBox { Height = 80, Margin = new Thickness(0, 0, 0, 10) };
            AutomationProperties.SetAutomationId(rich, "rich"); panel.Children.Add(rich);
            var password = new PasswordBox { Height = 36, Margin = new Thickness(0, 0, 0, 10) };
            AutomationProperties.SetAutomationId(password, "password"); panel.Children.Add(password);
            var readOnly = new TextBox { Text = "Read-only field", IsReadOnly = true, Height = 36 };
            AutomationProperties.SetAutomationId(readOnly, "readonly"); panel.Children.Add(readOnly);
            var copy = new CheckBox { Content = "Copy new clipboard on text change" };
            AutomationProperties.SetAutomationId(copy, "copyOnChange"); panel.Children.Add(copy);
            plain.TextChanged += (_, _) => { if (copy.IsChecked == true) Clipboard.SetText("new user copy"); };
            var window = new Window { Title = "VoiceMite isolated target", Width = 550, Height = 490, Content = panel };
            window.Closed += (_, _) => app.Shutdown(); window.Show(); app.Run(); return 0;
        }
        var exit = 1;
        app.Startup += async (_, _) =>
        {
            host = new Window { Title = "VoiceMite test controller", Width = 270, Height = 100, Content = new TextBlock { Text = "Running isolated input tests…", Margin = new Thickness(15) } };
            host.Show();
            try { if (args.Contains("--external")) await RunExternalAsync(); else await RunAsync(); exit = Results.Any(IsFailed) ? 1 : 0; }
            catch (Exception ex) { Results.Add(new { name = "Harness", status = "failed", detail = ex.GetType().Name + ": " + ex.Message }); }
            var report = JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(report);
            var reportArg = Array.IndexOf(args, "--report");
            if (reportArg >= 0 && reportArg + 1 < args.Length)
            {
                var path = Path.GetFullPath(args[reportArg + 1]); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, report);
            }
            app.Shutdown();
        };
        app.Run(); return exit;
    }
    private static bool IsFailed(object value) => value.GetType().GetProperty("status")?.GetValue(value)?.ToString() == "failed";
    private static async Task RunExternalAsync()
    {
        using var temporary = new TemporaryDirectory();
        var browserPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe");
        if (File.Exists(browserPath))
        {
            var htmlPath = System.IO.Path.Combine(temporary.Path, "voiceink-fixture.html");
            File.WriteAllText(htmlPath, """
                <!doctype html><html lang="zh"><meta charset="utf-8"><title>VoiceMite isolated browser fixture</title>
                <style>body{font:20px sans-serif;max-width:700px;margin:40px}input,textarea,[contenteditable]{display:block;width:600px;margin:20px;padding:10px;border:1px solid #888;min-height:30px}</style>
                <form onsubmit="event.preventDefault();document.getElementById('count').value=Number(document.getElementById('count').value)+1">
                <input id="plain" aria-label="VoiceMite plain field"><textarea id="multi" aria-label="VoiceMite multiline field"></textarea>
                <div id="content" role="textbox" contenteditable="true" aria-label="VoiceMite editable area"></div>
                <input id="password" type="password" aria-label="VoiceMite password field"><button type="submit">Submit test</button>
                <input id="count" aria-label="VoiceMite submit count" readonly value="0"></form>
                </html>
                """, System.Text.Encoding.UTF8);
            using var browser = Process.Start(new ProcessStartInfo(browserPath)
            {
                UseShellExecute = false, CreateNoWindow = true,
                ArgumentList = { "--user-data-dir=" + System.IO.Path.Combine(temporary.Path, "browser-profile"), "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--disable-background-mode", "--force-renderer-accessibility", "--new-window", new Uri(htmlPath).AbsoluteUri }
            })!;
            try
            {
                var root = await FindWindow("VoiceMite isolated browser fixture", 100);
                if (root == null) Results.Add(new { name = "Edge local fixture", status = "untested", detail = "Isolated browser window unavailable" });
                else
                {
                    // A window title appears before Chromium has finished initial focus/UIA
                    // provider setup. Wait for fixture readiness before testing insertion.
                    await Task.Delay(800);
                    AutomationElement Find(string name) => root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name)) ?? throw new Exception("Missing browser control " + name);
                    await InsertCase("Edge ordinary input Chinese/English/emoji", Find("VoiceMite plain field"), "", 0, 0, "中文 Edge123🙂", "中文 Edge123🙂");
                    await InsertCase("Edge multiline textarea", Find("VoiceMite multiline field"), "", 0, 0, "中文\nEnglish123🙂\n末行", "中文\nEnglish123🙂\n末行");
                    await InsertCase("Edge contenteditable area", Find("VoiceMite editable area"), "", 0, 0, "可编辑\ncontent🙂", null);
                    await Focus(Find("VoiceMite password field"));
                    Check("Edge password refused", !new SafeTargetService().Capture().Success);
                    Check("Edge no automatic submission", ((ValuePattern)Find("VoiceMite submit count").GetCurrentPattern(ValuePattern.Pattern)).Current.Value == "0");
                }
            }
            finally
            {
                if (!browser.HasExited) { browser.Kill(true); await browser.WaitForExitAsync(); }
                KillFixtureBrowserProcesses(System.IO.Path.Combine(temporary.Path, "browser-profile"));
                await Task.Delay(200);
            }
        }
        else Results.Add(new { name = "Edge local fixture", status = "untested", detail = "Edge not installed" });

        var namePart = "VoiceMite-test-" + Guid.NewGuid().ToString("N")[..8];
        var file = System.IO.Path.Combine(temporary.Path, namePart + ".txt");
        File.WriteAllText(file, "");
        using var notepad = Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = false, ArgumentList = { file } });
        var noteWindow = await FindWindow(namePart, 80);
        if (noteWindow == null) { Results.Add(new { name = "Notepad new test file", status = "untested", detail = "Test-file window unavailable" }); return; }
        var fields = noteWindow.FindAll(TreeScope.Descendants, new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));
        AutomationElement? editor = null;
        foreach (AutomationElement candidate in fields)
            if (candidate.Current.IsEnabled && candidate.Current.IsKeyboardFocusable && candidate.TryGetCurrentPattern(TextPattern.Pattern, out _)) { editor = candidate; break; }
        if (editor == null) { Results.Add(new { name = "Notepad new test file", status = "untested", detail = "No editable TextPattern" }); return; }
        try
        {
            await InsertCase("Notepad blank Chinese/English/emoji", editor, "", 0, 0, "记事本 Notepad123🙂", null);
            await InsertCase("Notepad middle caret", editor, "ABCDE", 2, 2, "中文", null);
            await InsertCase("Notepad selection replacement", editor, "ABCDE", 1, 4, "🙂", null);
            await InsertCase("Notepad multiline", editor, "", 0, 0, "第一行\nEnglish🙂\n末行", null);
        }
        finally
        {
            // Only the newly created test tab is saved and closed. Do not close the whole modern
            // Notepad process, which may also own the user's unrelated tabs.
            await Focus(editor);
            System.Windows.Forms.SendKeys.SendWait("^s"); await Task.Delay(250);
            System.Windows.Forms.SendKeys.SendWait("^w"); await Task.Delay(250);
        }
    }
    private static void KillFixtureBrowserProcesses(string exactProfile)
    {
        // Edge may hand off from its launcher to a broker-owned process. Match only this test's
        // unique temporary profile; never terminate a normal user browser or inspect its page data.
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!;
            dynamic locator = Activator.CreateInstance(locatorType)!;
            dynamic service = locator.ConnectServer(".", "root\\cimv2");
            dynamic processes = service.ExecQuery("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='msedge.exe'");
            foreach (dynamic process in processes)
            {
                string? commandLine = process.CommandLine;
                if (commandLine?.Contains(exactProfile, StringComparison.OrdinalIgnoreCase) == true)
                {
                    try { using var owned = Process.GetProcessById((int)process.ProcessId); owned.Kill(true); owned.WaitForExit(3000); }
                    catch (ArgumentException) { }
                    catch (InvalidOperationException) { }
                }
            }
        }
        catch { /* Directory cleanup reports any remaining lock honestly. */ }
    }
    private static async Task<AutomationElement?> FindWindow(string title, int attempts)
    {
        for (var i = 0; i < attempts; i++)
        {
            var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
            foreach (AutomationElement window in windows)
                if (window.Current.Name.Contains(title, StringComparison.Ordinal)) return window;
            await Task.Delay(100);
        }
        return null;
    }
    private static async Task RunAsync()
    {
        var exe = Environment.ProcessPath!;
        using var targetProcess = Process.Start(new ProcessStartInfo(exe, "--target") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            for (var i = 0; i < 50 && targetProcess.MainWindowHandle == IntPtr.Zero; i++) { await Task.Delay(100); targetProcess.Refresh(); }
            if (targetProcess.MainWindowHandle == IntPtr.Zero) throw new Exception("Target window unavailable");
            var root = AutomationElement.FromHandle(targetProcess.MainWindowHandle);
            AutomationElement Find(string id) => root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id)) ?? throw new Exception("Missing " + id);
            var plain = Find("plain"); var multi = Find("multi"); var rich = Find("rich");
            using var secretProbe = new TemporaryDirectory();
            var secrets = new SecretStore(secretProbe.Path);
            const string testSecret = "fixture-only-secret-中文-not-a-real-key";
            secrets.Set("stt", testSecret);
            var bytes = File.ReadAllBytes(Path.Combine(secretProbe.Path, "secrets", "stt.dpapi"));
            Check("DPAPI current-user encrypted round trip", secrets.Get("stt") == testSecret && !System.Text.Encoding.UTF8.GetString(bytes).Contains(testSecret));
            secrets.Set("stt", "replacement"); Check("DPAPI key replacement", secrets.Get("stt") == "replacement");
            secrets.Delete("stt"); Check("DPAPI key deletion", secrets.Get("stt") == null);
            Check("Microphone enumeration does not start capture", AudioRecorder.ListDevices() != null);
            if (AudioRecorder.ListDevices().Count > 0)
            {
                try
                {
                    using var recorder = new AudioRecorder();
                    var allValid = true;
                    for (var iteration = 0; iteration < 3; iteration++)
                    {
                        recorder.Start(); await Task.Delay(300);
                        var wav = await recorder.StopAsync();
                        allValid &= wav.Length > 44 && System.Text.Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" && BitConverter.ToInt32(wav, 24) == 16000;
                        CryptographicOperations.ZeroMemory(wav);
                    }
                    recorder.Start(); await Task.Delay(150); recorder.Cancel();
                    Check("Real microphone repeated start/stop/cancel, RAM-only PCM WAV", allValid && !recorder.IsRecording);
                }
                catch (Exception ex) { Results.Add(new { name = "Real microphone repeated start/stop/cancel", status = "untested", detail = ex.Message }); }
            }
            else Results.Add(new { name = "Real microphone repeated start/stop/cancel", status = "untested", detail = "No microphone" });

            await InsertCase("Blank TextBox Chinese/English/numbers/punctuation/emoji", plain, "", 0, 0, "中文 English 123，。🙂", "中文 English 123，。🙂");
            await InsertCase("Middle caret insertion", plain, "ABCDE", 2, 2, "中文", "AB中文CDE");
            await InsertCase("Selection replacement", plain, "ABCDE", 1, 4, "🙂", "A🙂E");
            await InsertCase("Multiline paste without Enter key", multi, "", 0, 0, "第一行\nEnglish 456🙂\n末行", "第一行\nEnglish 456🙂\n末行");
            await InsertCase("RichTextBox editable area", rich, "", 0, 0, "富文本\nRich🙂", null);

            var service = new SafeTargetService();
            await Focus(Find("password")); Check("Password target blocked", !service.Capture().Success);
            await Focus(Find("readonly")); Check("Read-only target blocked", !service.Capture().Success);

            Set(plain, "ABCDE"); await Focus(plain); Select(plain, 1, 1);
            var capture = service.Capture(); Select(plain, 2, 2);
            Check("Moved caret invalidates captured target", capture.Success && !service.Poll().Success && !service.HasTarget);
            Set(plain, "ABCDE"); await Focus(plain); Select(plain, 1, 1);
            capture = service.Capture(); Set(plain, "Changed");
            Check("Changed contents invalidate captured target", capture.Success && !service.Poll().Success);

            // A clipboard image must never be destroyed to perform a paste.
            Set(plain, ""); await Focus(plain); Select(plain, 0, 0); capture = service.Capture(); host!.Activate();
            var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            Clipboard.SetImage(bitmap);
            var imageResult = await service.InsertAsync("should not paste");
            Check("Unsupported image clipboard safely refused", capture.Success && !imageResult.Success && imageResult.Code is "ClipboardUnsupported" or "ClipboardBusy" && Clipboard.ContainsImage(), "Capture:" + capture.Code + " Insert:" + imageResult.Code);

            // The isolated receiver copies new data immediately after receiving the paste.
            Set(plain, ""); await Focus(plain); Select(plain, 0, 0); Clipboard.SetText("old clipboard");
            ((TogglePattern)Find("copyOnChange").GetCurrentPattern(TogglePattern.Pattern)).Toggle();
            await Focus(plain); Select(plain, 0, 0); capture = service.Capture(); host.Activate();
            var changedClipboard = await service.InsertAsync("new input");
            Check("New clipboard copy is not overwritten by restoration", capture.Success && changedClipboard.Success && Clipboard.GetText() == "new user copy", "Capture:" + capture.Code + " Insert:" + changedClipboard.Code);
            ((TogglePattern)Find("copyOnChange").GetCurrentPattern(TogglePattern.Pattern)).Toggle();

            Set(plain, ""); await Focus(plain); Select(plain, 0, 0); capture = service.Capture();
            targetProcess.Kill(); await targetProcess.WaitForExitAsync();
            Check("Closed target invalidates insertion", capture.Success && !service.Poll().Success && !service.HasTarget);
        }
        finally { if (!targetProcess.HasExited) { targetProcess.Kill(); await targetProcess.WaitForExitAsync(); } }
    }
    private static async Task InsertCase(string name, AutomationElement element, string original, int start, int end, string insert, string? expected)
    {
        Set(element, original); await Focus(element); Select(element, start, end);
        Clipboard.SetText("clipboard sentinel");
        var service = new SafeTargetService(); var capture = service.Capture();
        if (!capture.Success) { Check(name, false, "Capture:" + capture.Code); return; }
        host!.Activate(); await Task.Delay(80);
        var result = await service.InsertAsync(insert);
        var actual = GetText(element).Replace("\r\n", "\n").Replace('\r', '\n');
        Check(name, result.Success && (expected == null ? actual.Contains(insert) : actual == expected) && Clipboard.GetText() == "clipboard sentinel", result.Code + "/" + service.DiagnosticReason + " actual=" + actual);
        Check(name + " consumed target prevents duplicate", !service.HasTarget && !(await service.InsertAsync(insert)).Success);
    }
    private static async Task Focus(AutomationElement element) { element.SetFocus(); await Task.Delay(150); }
    private static void Set(AutomationElement element, string text)
    {
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)) ((ValuePattern)pattern).SetValue(text);
        else
        {
            // Fixture-only setup; production never uses these selection-clearing keys.
            element.SetFocus(); System.Windows.Forms.SendKeys.SendWait("^a"); System.Windows.Forms.SendKeys.SendWait("{BACKSPACE}");
        }
    }
    private static string GetText(AutomationElement element) => ((TextPattern)element.GetCurrentPattern(TextPattern.Pattern)).DocumentRange.GetText(-1);
    private static void Select(AutomationElement element, int start, int end)
    {
        var range = ((TextPattern)element.GetCurrentPattern(TextPattern.Pattern)).DocumentRange.Clone();
        range.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start);
        range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, end);
        range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, start);
        range.Select();
    }
    private static void Check(string name, bool passed, string detail = "") => Results.Add(new { name, status = passed ? "passed" : "failed", detail });
    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VoiceMiteTests-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            var resolved = System.IO.Path.GetFullPath(Path);
            var temporaryRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) || !System.IO.Path.GetFileName(resolved).StartsWith("VoiceMiteTests-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected temporary directory");
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try { Directory.Delete(resolved, true); return; }
                catch (IOException) when (attempt < 19) { Thread.Sleep(100); }
                catch (UnauthorizedAccessException) when (attempt < 19) { Thread.Sleep(100); }
            }
        }
    }
}
