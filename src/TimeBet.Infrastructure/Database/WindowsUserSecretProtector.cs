using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace TimeBet.Infrastructure.Database;

internal static class WindowsUserSecretProtector
{
    public static byte[] Protect(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Transform(Encoding.UTF8.GetBytes(value), protect: true);
    }

    public static string Unprotect(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Encoding.UTF8.GetString(Transform(value, protect: false));
    }

    private static byte[] Transform(byte[] value, bool protect)
    {
        var input = new DataBlob(value);
        try
        {
            var result = new DataBlob();
            var succeeded = protect
                ? CryptProtectData(ref input.Blob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref result.Blob)
                : CryptUnprotectData(ref input.Blob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref result.Blob);
            if (!succeeded)
            {
                throw new SecurityException("Windows 用户级密钥保护不可用。");
            }

            try
            {
                var output = new byte[result.Blob.cbData];
                Marshal.Copy(result.Blob.pbData, output, 0, output.Length);
                return output;
            }
            finally
            {
                if (result.Blob.pbData != IntPtr.Zero)
                {
                    LocalFree(result.Blob.pbData);
                }
            }
        }
        finally
        {
            input.Dispose();
        }
    }

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref Blob dataIn,
        string? description,
        IntPtr entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        ref Blob dataOut);

    [DllImport("Crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref Blob dataIn,
        IntPtr description,
        IntPtr entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        ref Blob dataOut);

    [DllImport("Kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int cbData;
        public IntPtr pbData;
    }

    private sealed class DataBlob : IDisposable
    {
        public DataBlob(byte[] value)
        {
            Blob = new Blob
            {
                cbData = value.Length,
                pbData = Marshal.AllocHGlobal(value.Length),
            };
            Marshal.Copy(value, 0, Blob.pbData, value.Length);
        }

        public DataBlob()
        {
        }

        public Blob Blob;

        public void Dispose()
        {
            if (Blob.pbData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Blob.pbData);
                Blob.pbData = IntPtr.Zero;
            }
        }
    }
}
