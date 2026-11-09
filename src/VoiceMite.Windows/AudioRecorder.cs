using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace VoiceMite.Windows;

public sealed record MicrophoneDevice(int Id, string Name);

/// <summary>Explicit-start WinMM capture. Audio remains in memory and is zeroed when released.</summary>
public sealed class AudioRecorder : IDisposable
{
    private const int SampleRate = 16000, BufferSize = 3200, MaxBytes = SampleRate * 2 * 120;
    private const uint CallbackFunction = 0x30000, WimData = 0x3C0;
    private readonly object gate = new();
    private readonly List<Buffer> buffers = [];
    private readonly WaveCallback callback;
    private MemoryStream? audio;
    private IntPtr handle;
    private bool recording, accepting, limitSent, disposed;
    public bool IsRecording { get { lock (gate) return recording; } }
    public event Action<float>? LevelChanged;
    public event Action? LimitReached;

    public AudioRecorder() => callback = OnWave;

    public static IReadOnlyList<MicrophoneDevice> ListDevices()
    {
        var result = new List<MicrophoneDevice>();
        for (uint i = 0; i < waveInGetNumDevs(); i++)
            if (waveInGetDevCaps(new UIntPtr(i), out var caps, (uint)Marshal.SizeOf<WaveInCaps>()) == 0)
                result.Add(new((int)i, caps.Name));
        return result;
    }

    public void Start(int deviceId = -1)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (handle != IntPtr.Zero) throw new InvalidOperationException("AlreadyRecording");
        var format = new WaveFormat { FormatTag = 1, Channels = 1, SamplesPerSec = SampleRate,
            AvgBytesPerSec = SampleRate * 2, BlockAlign = 2, BitsPerSample = 16 };
        var error = waveInOpen(out handle, unchecked((uint)deviceId), ref format, callback, IntPtr.Zero, CallbackFunction);
        if (error != 0) { handle = IntPtr.Zero; throw new InvalidOperationException(error == 4 ? "MicrophoneBusy" : "MicrophoneUnavailable"); }
        try
        {
            audio = new MemoryStream(MaxBytes);
            limitSent = false;
            for (var i = 0; i < 4; i++)
            {
                var buffer = new Buffer(); buffers.Add(buffer);
                Check(waveInPrepareHeader(handle, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>()));
                Check(waveInAddBuffer(handle, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>()));
            }
            lock (gate) { recording = true; accepting = true; }
            Check(waveInStart(handle));
        }
        catch { Cancel(); throw; }
    }

    public Task<byte[]> StopAsync() => Task.FromResult(Stop(true));
    public void Cancel() => Stop(false);

    private byte[] Stop(bool keep)
    {
        // Stop requeueing, but retain the final partial buffer returned by Reset on a normal stop.
        lock (gate) { recording = false; accepting = keep; }
        if (handle != IntPtr.Zero)
        {
            waveInStop(handle);
            waveInReset(handle);
            foreach (var buffer in buffers)
                waveInUnprepareHeader(handle, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>());
            waveInClose(handle); handle = IntPtr.Zero;
        }
        lock (gate) accepting = false;
        foreach (var buffer in buffers) buffer.Dispose();
        buffers.Clear();
        byte[] result = [];
        lock (gate)
        {
            if (audio != null)
            {
                if (keep && audio.Length > 0)
                {
                    using var wav = new MemoryStream();
                    using var writer = new BinaryWriter(wav);
                    writer.Write("RIFF"u8); writer.Write((int)audio.Length + 36); writer.Write("WAVEfmt "u8);
                    writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(SampleRate);
                    writer.Write(SampleRate * 2); writer.Write((short)2); writer.Write((short)16);
                    writer.Write("data"u8); writer.Write((int)audio.Length);
                    audio.Position = 0; audio.CopyTo(wav); result = wav.ToArray();
                    if (wav.TryGetBuffer(out var temporary)) Array.Clear(temporary.Array!, temporary.Offset, temporary.Count);
                }
                if (audio.TryGetBuffer(out var raw)) Array.Clear(raw.Array!, raw.Offset, raw.Count);
                audio.Dispose(); audio = null;
            }
        }
        return result;
    }

    private void OnWave(IntPtr hwi, uint message, IntPtr instance, IntPtr headerPtr, IntPtr param)
    {
        if (message != WimData || headerPtr == IntPtr.Zero) return;
        float level = 0; bool reached = false;
        lock (gate)
        {
            if (!accepting || audio == null) return;
            var header = Marshal.PtrToStructure<WaveHeader>(headerPtr);
            var count = Math.Min((int)header.BytesRecorded, MaxBytes - (int)audio.Length);
            if (count > 0)
            {
                var bytes = new byte[count]; Marshal.Copy(header.Data, bytes, 0, count);
                audio.Write(bytes);
                double energy = 0;
                for (var i = 0; i + 1 < bytes.Length; i += 2) { var sample = BitConverter.ToInt16(bytes, i) / 32768d; energy += sample * sample; }
                level = (float)Math.Min(1, Math.Sqrt(energy / Math.Max(1, count / 2)) * 4);
                Array.Clear(bytes);
            }
            if (audio.Length >= MaxBytes)
            {
                if (!limitSent) { limitSent = true; reached = true; }
            }
            else if (recording) waveInAddBuffer(handle, headerPtr, (uint)Marshal.SizeOf<WaveHeader>());
        }
        try { LevelChanged?.Invoke(level); } catch { /* Capture callbacks must never unwind into WinMM. */ }
        if (reached) ThreadPool.QueueUserWorkItem(_ => { try { LimitReached?.Invoke(); } catch { } });
    }
    private static void Check(uint code) { if (code != 0) throw new InvalidOperationException("MicrophoneUnavailable"); }
    public void Dispose() { if (disposed) return; Cancel(); disposed = true; GC.SuppressFinalize(this); }

    private sealed class Buffer : IDisposable
    {
        public IntPtr Data { get; } = Marshal.AllocHGlobal(BufferSize);
        public IntPtr Header { get; } = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
        public Buffer() => Marshal.StructureToPtr(new WaveHeader { Data = Data, BufferLength = BufferSize }, Header, false);
        public void Dispose()
        {
            Marshal.Copy(new byte[BufferSize], 0, Data, BufferSize);
            Marshal.FreeHGlobal(Data); Marshal.FreeHGlobal(Header);
        }
    }
    [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct WaveFormat
    { public ushort FormatTag, Channels; public uint SamplesPerSec, AvgBytesPerSec; public ushort BlockAlign, BitsPerSample, ExtraSize; }
    [StructLayout(LayoutKind.Sequential)] private struct WaveHeader
    { public IntPtr Data; public uint BufferLength, BytesRecorded; public IntPtr User; public uint Flags, Loops; public IntPtr Next, Reserved; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WaveInCaps
    { public ushort Manufacturer, Product; public uint DriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; public uint Formats; public ushort Channels, Reserved; }
    private delegate void WaveCallback(IntPtr waveIn, uint message, IntPtr instance, IntPtr param1, IntPtr param2);
    [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern uint waveInGetDevCaps(UIntPtr device, out WaveInCaps caps, uint size);
    [DllImport("winmm.dll")] private static extern uint waveInOpen(out IntPtr handle, uint device, ref WaveFormat format, WaveCallback callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveInPrepareHeader(IntPtr h, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveInUnprepareHeader(IntPtr h, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveInAddBuffer(IntPtr h, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveInStart(IntPtr h);
    [DllImport("winmm.dll")] private static extern uint waveInStop(IntPtr h);
    [DllImport("winmm.dll")] private static extern uint waveInReset(IntPtr h);
    [DllImport("winmm.dll")] private static extern uint waveInClose(IntPtr h);
}
