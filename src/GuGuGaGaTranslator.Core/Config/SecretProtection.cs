using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GuGuGaGaTranslator.Core.Config;

/// <summary>Windows DPAPI encryption scoped to the current user.</summary>
internal static class SecretProtection
{
    internal const string Prefix = "dpapi:v1:";

    internal static string Protect(string value) =>
        string.IsNullOrEmpty(value) ? value : Prefix + Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), true));

    internal static string Unprotect(string value) =>
        value.StartsWith(Prefix, StringComparison.Ordinal)
            ? Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value[Prefix.Length..]), false))
            : value;

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public nint Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy,
        nint reserved, nint prompt, int flags, out Blob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy,
        nint reserved, nint prompt, int flags, out Blob output);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        var output = new Blob();
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = protect
                ? CryptProtectData(ref input, null, 0, 0, 0, 1, out output)
                : CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output);
            if (!ok) throw new CryptographicException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            for (var i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != 0)
            {
                for (var i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0);
                LocalFree(output.Data);
            }
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
