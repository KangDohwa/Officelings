using System.Runtime.InteropServices;

namespace Vibedotcom.SharingChecks;

// CurrentUser DPAPI needs a loaded Windows profile. Restricted test identities
// can lack one even on Windows. Diagnose that context without skipping the check
// or weakening the production credential scope to LocalMachine.
internal sealed class DpapiTestContextException(int error) : Exception(
    $"CurrentUser DPAPI unavailable (Win32 {error}); run these synthetic checks under a loaded Windows user profile.");

internal static class DpapiTestContext
{
    internal static void RequireCurrentUser()
    {
        var input = new Blob { Size = 1, Data = Marshal.AllocHGlobal(1) };
        Blob output = default;
        try
        {
            Marshal.WriteByte(input.Data, 42); // Synthetic probe, never a stored credential.
            if (!CryptProtectData(ref input, null, 0, 0, 0, 1, out output))
                throw new DpapiTestContextException(Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != 0) LocalFree(output.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public nint Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy,
        nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
