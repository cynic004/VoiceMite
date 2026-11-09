using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Forms = System.Windows.Forms;

namespace VoiceMite.Windows;

public sealed record NativeResult(bool Success, string Code = "");
public sealed record TargetCaptureResult(bool Success, string Code, string TargetDescription);

/// <summary>
/// Conservative, one-shot paste. TextPattern is used only for local validation, never for writing.
/// No window title or input text is sent to an API. Unsupported selection providers fail closed.
/// </summary>
public sealed class SafeTargetService
{
    private const int MaxText = 32768;
    private readonly int ownPid = Environment.ProcessId;
    private Target? target;
    private string lastCode = "TargetMissing";
    public bool HasTarget => target != null;
    public string TargetDescription => target?.Description ?? "";
    public string DiagnosticReason { get; private set; } = "";
    private static readonly HashSet<string> BlockedProcesses = new(StringComparer.OrdinalIgnoreCase)
    { "cmd", "powershell", "powershell_ise", "pwsh", "WindowsTerminal", "OpenConsole", "conhost", "wsl", "bash", "mintty", "putty", "wt", "wezterm", "alacritty", "ssh", "Code", "Code - Insiders", "Cursor", "Antigravity", "devenv", "SecureCRT", "MobaXterm", "kitty" };

    public TargetCaptureResult Capture()
    {
        Clear();
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return Failed("TargetMissing");
            GetWindowThreadProcessId(window, out var pid);
            if (pid == ownPid) return Failed("TargetMissing");
            var process = Process.GetProcessById((int)pid);
            if (BlockedProcesses.Contains(process.ProcessName)) return Failed("TargetUnsafe");
            var title = new StringBuilder(512); GetWindowText(window, title, title.Capacity);
            if (title.ToString().StartsWith("DevTools", StringComparison.OrdinalIgnoreCase) || title.ToString().StartsWith("Developer Tools", StringComparison.OrdinalIgnoreCase))
                return Failed("TargetUnsafe");
            if (!SameOrLowerIntegrity((int)pid)) return Failed("TargetPermission");
            var element = AutomationElement.FocusedElement;
            if (element == null || element.Current.ProcessId != pid || !element.Current.HasKeyboardFocus)
                return Failed("TargetChanged");
            var safe = CheckEditable(element);
            if (!safe.Success) return Failed(safe.Code);
            var snapshot = ReadSnapshot(element);
            if (snapshot == null) return Failed("TargetUnsupported");
            target = new(window, (int)pid, process.StartTime.ToUniversalTime().Ticks, element,
                element.GetRuntimeId(), snapshot, process.ProcessName);
            lastCode = "";
            return new(true, "", target.Description);
        }
        catch (ElementNotAvailableException) { return Failed("TargetMissing"); }
        catch (UnauthorizedAccessException) { return Failed("TargetPermission"); }
        catch { return Failed("TargetUnsupported"); }
    }

    /// <summary>Call about twice per second while a target is retained. External focus changes invalidate it.</summary>
    public NativeResult Poll()
    {
        if (target == null) return new(false, lastCode);
        var validation = Validate(target, allowOwnForeground: true);
        if (!validation.Success) { lastCode = validation.Code; target = null; }
        return validation;
    }
    public void Clear() { target = null; lastCode = "TargetMissing"; }

    public async Task<NativeResult> InsertAsync(string text, CancellationToken cancellationToken = default)
    {
        var captured = target;
        if (captured == null) return new(false, lastCode);
        if (string.IsNullOrEmpty(text) || text.Length > 16000 || text.IndexOf('\0') >= 0) return new(false, "InputTooLong");
        if (captured.Snapshot.Length - (captured.Snapshot.End - captured.Snapshot.Start) + text.Length > MaxText)
            return new(false, "InputTooLong");
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) return new(false, "ClipboardUnsupported");
        var validation = Validate(captured, allowOwnForeground: true);
        if (!validation.Success) { Clear(); return validation; }
        if (AnyModifiersDown()) return new(false, "ReleaseModifiers");
        if (cancellationToken.IsCancellationRequested) return new(false, "Cancelled");
        // Back up only safely materializable formats. Delayed rendering, images and custom formats
        // are refused rather than risking damage to the user's clipboard.
        var backup = ClipboardTransaction.Capture();
        if (backup.Transaction == null) return new(false, backup.Code);
        using var clipboard = backup.Transaction;
        bool injected = false;
        try
        {
            if (!SetForegroundWindow(captured.Window)) { DiagnosticReason = "RestoreDenied"; return new(false, "TargetChanged"); }
            await Task.Delay(120, cancellationToken);
            // Never steal focus back after the user switches away during restoration.
            // If the window did not restore exactly the captured editor, fail closed.
            validation = Validate(captured, allowOwnForeground: false);
            if (!validation.Success) return validation;
            var expectedHash = ExpectedHash(captured.Element, captured.Snapshot, text);
            if (expectedHash == null) { DiagnosticReason = "ExpectedHash"; return new(false, "TargetChanged"); }
            var stageCode = clipboard.StageText(text);
            if (stageCode.Length != 0) return new(false, stageCode);
            cancellationToken.ThrowIfCancellationRequested();
            validation = Validate(captured, allowOwnForeground: false);
            if (!validation.Success) return validation;
            if (!clipboard.IsUnchanged) return new(false, "ClipboardChanged");
            if (AnyModifiersDown()) return new(false, "ReleaseModifiers");
            cancellationToken.ThrowIfCancellationRequested();
            // One key chord only; never Enter, Tab, Ctrl+A, Backspace, or an automatic retry.
            INPUT[] inputs = [Key(0x11), Key(0x56), Key(0x56, true), Key(0x11, true)];
            injected = true;
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            Clear(); // Consumed even on uncertain results: never offer duplicate retry.
            if (sent != inputs.Length) return new(false, "InsertUncertain");
            // Read-back is validation only. An uncertain outcome is never retried automatically.
            for (var attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(100);
                var after = ReadSnapshot(captured.Element);
                if (after?.Hash == expectedHash) return new(true, "Inserted");
            }
            return new(false, "InsertUncertain");
        }
        catch (OperationCanceledException) { return new(false, injected ? "InsertUncertain" : "Cancelled"); }
        catch (ExternalException) { return new(false, injected ? "InsertUncertain" : "ClipboardBusy"); }
        catch { return new(false, injected ? "InsertUncertain" : "TargetUnsupported"); }
        finally
        {
            if (injected) Clear();
            clipboard.RestoreIfUnchanged();
        }
    }

    /// <summary>Explicit user action: replaces clipboard and leaves the user's text available.</summary>
    public static NativeResult CopyText(string text)
    {
        try { Forms.Clipboard.SetText(text, Forms.TextDataFormat.UnicodeText); return new(true, "Copied"); }
        catch { return new(false, "ClipboardBusy"); }
    }

    private TargetCaptureResult Failed(string code) { lastCode = code; return new(false, code, ""); }
    private NativeResult Validate(Target captured, bool allowOwnForeground)
    {
        try
        {
            if (!IsWindow(captured.Window)) return new(false, "TargetMissing");
            GetWindowThreadProcessId(captured.Window, out var pid);
            if (pid != captured.ProcessId || Process.GetProcessById((int)pid).StartTime.ToUniversalTime().Ticks != captured.StartTicks)
                return Changed("Process");
            if (!SameOrLowerIntegrity((int)pid)) return new(false, "TargetPermission");
            var foreground = GetForegroundWindow(); GetWindowThreadProcessId(foreground, out var foregroundPid);
            if (foreground != captured.Window && !(allowOwnForeground && foregroundPid == ownPid))
                return Changed("Foreground");
            if (foreground == captured.Window)
            {
                var focused = AutomationElement.FocusedElement;
                if (focused == null || !focused.GetRuntimeId().SequenceEqual(captured.RuntimeId)) return Changed("FocusedElement");
            }
            var editable = CheckEditable(captured.Element); if (!editable.Success) return editable;
            var now = ReadSnapshot(captured.Element);
            if (now == null || now != captured.Snapshot) return Changed("Snapshot");
            return new(true);
        }
        catch (ElementNotAvailableException) { return new(false, "TargetMissing"); }
        catch { return new(false, "TargetUnsupported"); }
    }
    private NativeResult Changed(string reason) { DiagnosticReason = reason; return new(false, "TargetChanged"); }
    private static NativeResult CheckEditable(AutomationElement element)
    {
        var current = element.Current;
        if (current.IsPassword) return new(false, "TargetUnsafe");
        if (!current.IsEnabled || !current.IsKeyboardFocusable || current.IsOffscreen) return new(false, "TargetUnsupported");
        if (current.ControlType != ControlType.Edit && current.ControlType != ControlType.Document) return new(false, "TargetUnsupported");
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var textObject)) return new(false, "TargetUnsupported");
        var text = (TextPattern)textObject;
        if (text.SupportedTextSelection != SupportedTextSelection.Single) return new(false, "TargetUnsupported");
        var readOnly = text.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
        if (readOnly is not bool isReadOnly || isReadOnly) return new(false, "TargetUnsafe");
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && ((ValuePattern)value).Current.IsReadOnly)
            return new(false, "TargetUnsafe");
        return new(true);
    }
    private static Snapshot? ReadSnapshot(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;
        var textPattern = (TextPattern)pattern;
        var document = textPattern.DocumentRange;
        var text = ReadContent(element, textPattern);
        if (text == null || text.Length > MaxText) return null;
        var selection = textPattern.GetSelection();
        if (selection.Length != 1) return null;
        // Chromium exposes a field's accessible name as TextPattern document text when the
        // actual ValuePattern value is empty. A collapsed selection in an empty value is
        // unambiguously offset zero; the label must never be treated as editable contents.
        if (text.Length == 0)
        {
            return new(HashText(""), 0, 0, 0);
        }
        var prefix = document.Clone();
        prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, selection[0], TextPatternRangeEndpoint.Start);
        var start = prefix.GetText(MaxText + 1).Length;
        prefix = document.Clone();
        prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, selection[0], TextPatternRangeEndpoint.End);
        var end = prefix.GetText(MaxText + 1).Length;
        if (start > text.Length || end < start || end > text.Length) return null;
        return new(HashText(text), text.Length, start, end);
    }
    private static string? ExpectedHash(AutomationElement element, Snapshot snapshot, string insert)
    {
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;
        var text = ReadContent(element, (TextPattern)pattern);
        if (text == null) return null;
        if (text.Length != snapshot.Length || HashText(text) != snapshot.Hash) return null;
        return HashText(text[..snapshot.Start] + insert + text[snapshot.End..]);
    }
    private static string? ReadContent(AutomationElement element, TextPattern pattern)
    {
        var text = pattern.DocumentRange.GetText(MaxText + 1);
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject)) return text;
        var value = ((ValuePattern)valueObject).Current.Value;
        if (value.Length == 0) return "";
        // ValuePattern is only read here. Disagreeing providers cannot safely prove offsets.
        return HashText(text) == HashText(value) ? text : null;
    }
    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n").Replace('\r', '\n'))));
    private static bool SameOrLowerIntegrity(int pid)
    {
        try { return GetIntegrity(pid) <= GetIntegrity(Environment.ProcessId); }
        catch { return false; }
    }
    private static int GetIntegrity(int pid)
    {
        using var process = Process.GetProcessById(pid);
        if (!OpenProcessToken(process.Handle, 0x0008, out var token)) throw new UnauthorizedAccessException();
        try
        {
            GetTokenInformation(token, 25, IntPtr.Zero, 0, out var required);
            var buffer = Marshal.AllocHGlobal(required);
            try
            {
                if (!GetTokenInformation(token, 25, buffer, required, out _)) throw new UnauthorizedAccessException();
                var sid = Marshal.ReadIntPtr(buffer);
                var countPtr = GetSidSubAuthorityCount(sid); var count = Marshal.ReadByte(countPtr);
                return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { CloseHandle(token); }
    }
    private static bool AnyModifiersDown() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0);
    private static INPUT Key(ushort key, bool up = false) => new() { Type = 1, Data = new InputUnion { Keyboard = new KEYBDINPUT { Vk = key, Flags = up ? 2u : 0u } } };
    private sealed record Target(IntPtr Window, int ProcessId, long StartTicks, AutomationElement Element, int[] RuntimeId, Snapshot Snapshot, string Description);
    private sealed record Snapshot(string Hash, int Length, int Start, int End);
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT Mouse; [FieldOffset(0)] public KEYBDINPUT Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort Vk, Scan; public uint Flags, Time; public UIntPtr ExtraInfo; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr information, int size, out int required);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
