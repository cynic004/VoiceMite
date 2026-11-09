using System.Runtime.InteropServices;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VoiceMite.Windows;

/// <summary>Current-user DPAPI encryption. Never put returned secrets in settings or logs.</summary>
public sealed class SecretStore
{
    private readonly string directory;
    public SecretStore(string? rootDirectory = null)
    {
        directory = Path.Combine(rootDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceMite"), "secrets");
    }
    private string GetPath(string id) => id is "stt" or "llm" ? Path.Combine(directory, id + ".dpapi") : throw new ArgumentException("InvalidSecretId");
    public string? Get(string id)
    {
        var path = GetPath(id);
        if (!File.Exists(path)) return null;
        byte[]? clear = null;
        try { clear = Transform(File.ReadAllBytes(path), false); return Encoding.UTF8.GetString(clear); }
        catch { throw new CryptographicException("SecretUnavailable"); }
        finally { if (clear != null) CryptographicOperations.ZeroMemory(clear); }
    }
    public void Set(string id, string secret)
    {
        var path = GetPath(id);
        if (string.IsNullOrWhiteSpace(secret)) { Delete(id); return; }
        if (secret.Length > 8192) throw new ArgumentException("InvalidKey");
        Directory.CreateDirectory(directory);
        var clear = Encoding.UTF8.GetBytes(secret);
        try
        {
            var encrypted = Transform(clear, true);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temp, encrypted); File.Move(temp, path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public void Delete(string id) { var path = GetPath(id); if (File.Exists(path)) File.Delete(path); }
    private static byte[] Transform(byte[] input, bool protect)
    {
        var blob = new DataBlob { Length = input.Length, Data = Marshal.AllocHGlobal(input.Length) };
        DataBlob output = default;
        try
        {
            Marshal.Copy(input, 0, blob.Data, input.Length);
            var ok = protect
                ? CryptProtectData(ref blob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref blob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException("SecretUnavailable");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            Marshal.Copy(new byte[input.Length], 0, blob.Data, input.Length); Marshal.FreeHGlobal(blob.Data);
            if (output.Data != IntPtr.Zero) { if (!protect) Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length); LocalFree(output.Data); }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
}
