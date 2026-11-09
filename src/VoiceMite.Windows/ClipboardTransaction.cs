using System.Runtime.InteropServices;
using System.Text;

namespace VoiceMite.Windows;

/// <summary>Every sequence comparison and corresponding write happens under one clipboard lock.</summary>
internal sealed class ClipboardTransaction : IDisposable
{
    private readonly List<(uint Format, byte[] Bytes)> formats = [];
    private readonly System.Windows.Forms.NativeWindow owner = new();
    private uint originalSequence, stagedSequence;
    private ClipboardTransaction() => owner.CreateHandle(new System.Windows.Forms.CreateParams { Caption = "VoiceMiteClipboard", Parent = new IntPtr(-3) });
    public static (ClipboardTransaction? Transaction, string Code) Capture()
    {
        var transaction = new ClipboardTransaction();
        var success = false;
        if (!OpenClipboard(transaction.owner.Handle)) { transaction.Dispose(); return (null, "ClipboardBusy"); }
        try
        {
            transaction.originalSequence = GetClipboardSequenceNumber();
            uint format = 0; long total = 0;
            while ((format = EnumClipboardFormats(format)) != 0)
            {
                // Only formats whose representation is a self-contained HGLOBAL are safe to copy.
                if (format is not (1 or 7 or 13 or 16))
                {
                    var name = new StringBuilder(128); GetClipboardFormatName(format, name, name.Capacity);
                    // OLE transport bookkeeping is not a user data format and contains process-local
                    // references. Materialize and preserve the actual text formats instead.
                    if (name.ToString() is "DataObject" or "Ole Private Data") continue;
                    if (name.ToString() is not ("Rich Text Format" or "HTML Format" or "Csv")) return (null, "ClipboardUnsupported");
                }
                var handle = GetClipboardData(format);
                var size = (long)GlobalSize(handle);
                if (handle == IntPtr.Zero || size <= 0 || size > 1024 * 1024 || (total += size) > 4 * 1024 * 1024)
                    return (null, "ClipboardUnsupported");
                var data = GlobalLock(handle); if (data == IntPtr.Zero) return (null, "ClipboardUnsupported");
                try { var bytes = new byte[(int)size]; Marshal.Copy(data, bytes, 0, bytes.Length); transaction.formats.Add((format, bytes)); }
                finally { GlobalUnlock(handle); }
            }
            // Reading a delayed-rendered OLE format can itself materialize data and advance
            // the sequence. Record the final sequence while the clipboard is still locked.
            transaction.originalSequence = GetClipboardSequenceNumber();
            success = true;
            return (transaction, "");
        }
        finally { CloseClipboard(); if (!success) transaction.Dispose(); }
    }
    public string StageText(string text)
    {
        if (!OpenClipboard(owner.Handle)) return "ClipboardBusy";
        try
        {
            if (GetClipboardSequenceNumber() != originalSequence) return "ClipboardChanged";
            // Allocate before EmptyClipboard so allocation failure leaves the clipboard intact.
            var handle = Allocate(Encoding.Unicode.GetBytes(text + "\0"));
            if (handle == IntPtr.Zero) return "ClipboardBusy";
            if (!EmptyClipboard()) { GlobalFree(handle); return "ClipboardBusy"; }
            if (SetClipboardData(13, handle) == IntPtr.Zero) { GlobalFree(handle); RestoreUnderLock(); return "ClipboardBusy"; }
            // Windows publishes the final sequence when this clipboard lock is closed.
        }
        finally { CloseClipboard(); }
        if (!OpenClipboard(owner.Handle)) return "ClipboardBusy";
        try
        {
            if (GetClipboardOwner() != owner.Handle) return "ClipboardChanged";
            stagedSequence = GetClipboardSequenceNumber();
            return "";
        }
        finally { CloseClipboard(); }
    }
    public bool IsUnchanged => stagedSequence != 0 && GetClipboardSequenceNumber() == stagedSequence;
    public void RestoreIfUnchanged()
    {
        if (stagedSequence == 0 || !OpenClipboard(owner.Handle)) return;
        try
        {
            if (GetClipboardSequenceNumber() == stagedSequence) RestoreUnderLock();
        }
        finally { CloseClipboard(); }
    }
    private void RestoreUnderLock()
    {
        var handles = new List<(uint Format, IntPtr Handle)>();
        try
        {
            foreach (var format in formats)
            {
                var handle = Allocate(format.Bytes); if (handle == IntPtr.Zero) return;
                handles.Add((format.Format, handle));
            }
            if (!EmptyClipboard()) return;
            for (var index = 0; index < handles.Count; index++)
            {
                var item = handles[index];
                if (SetClipboardData(item.Format, item.Handle) != IntPtr.Zero)
                {
                    // The clipboard now owns successful handles.
                    handles[index] = (item.Format, IntPtr.Zero);
                }
            }
        }
        finally { foreach (var item in handles) if (item.Handle != IntPtr.Zero) GlobalFree(item.Handle); }
    }
    public void Dispose()
    {
        foreach (var format in formats) Array.Clear(format.Bytes);
        formats.Clear(); owner.DestroyHandle();
    }
    private static IntPtr Allocate(byte[] bytes)
    {
        var handle = GlobalAlloc(0x0002, (UIntPtr)bytes.Length); if (handle == IntPtr.Zero) return IntPtr.Zero;
        var data = GlobalLock(handle); if (data == IntPtr.Zero) { GlobalFree(handle); return IntPtr.Zero; }
        try { Marshal.Copy(bytes, 0, data, bytes.Length); } finally { GlobalUnlock(handle); }
        return handle;
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClipboardFormatName(uint format, StringBuilder name, int maxCount);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr handle);
}
