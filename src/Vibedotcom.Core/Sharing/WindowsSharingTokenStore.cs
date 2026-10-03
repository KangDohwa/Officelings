using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vibedotcom.Core.Sharing;

public interface ISharingTokenStore
{
    void Save(SharingEndpoint endpoint, string officeId, string token);
    string? Read(SharingEndpoint endpoint, string officeId);
    void Delete();
}

public sealed class SharingCredentialException : Exception
{
    public SharingCredentialException() : base("Sharing credential unavailable. Re-enter or remove the stored token.") { }
}

public sealed class WindowsSharingTokenStore(string directory) : ISharingTokenStore
{
    // A sibling credential directory is intentionally outside the app/tracker export directory.
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vibedotcom.Credentials");
    private string FilePath => Path.Combine(directory, "presence.dpapi");
    private sealed record Credential(string Origin, string OfficeId, string Token);

    public static void ValidateToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 4096 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.' or '~' or '+' or '/' or '=')))
            throw new ArgumentException("Enter a valid bearer token.");
    }
    public void Save(SharingEndpoint endpoint, string officeId, string token)
    {
        ValidateToken(token);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(new Credential(endpoint.Origin.AbsoluteUri, officeId, token));
        try { StateJson.WriteAtomic(FilePath, Transform(plain, protect: true)); }
        catch (Exception) { throw new SharingCredentialException(); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public string? Read(SharingEndpoint endpoint, string officeId)
    {
        byte[]? plain = null;
        try
        {
            if (!File.Exists(FilePath)) return null;
            if (new FileInfo(FilePath).Length > 16384) throw new SharingCredentialException();
            plain = Transform(File.ReadAllBytes(FilePath), protect: false);
            var credential = JsonSerializer.Deserialize<Credential>(plain);
            if (credential == null || credential.Origin != endpoint.Origin.AbsoluteUri || credential.OfficeId != officeId)
                throw new SharingCredentialException();
            ValidateToken(credential.Token);
            return credential.Token;
        }
        catch (Exception) { throw new SharingCredentialException(); }
        finally { if (plain != null) CryptographicOperations.ZeroMemory(plain); }
    }
    public void Delete()
    {
        try { File.Delete(FilePath); }
        catch (Exception) { throw new SharingCredentialException(); }
    }
    // CryptProtectData without CRYPTPROTECT_LOCAL_MACHINE uses Windows DPAPI CurrentUser. No NuGet dependency.
    private static byte[] Transform(byte[] bytes, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new SharingCredentialException();
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default; nint description = 0;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = protect ? CryptProtectData(ref input, null, 0, 0, 0, 1, out output)
                : CryptUnprotectData(ref input, out description, 0, 0, 0, 1, out output);
            if (!ok || output.Size is < 1 or > 16384) throw new SharingCredentialException();
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            Marshal.Copy(new byte[input.Size], 0, input.Data, input.Size); Marshal.FreeHGlobal(input.Data);
            if (output.Data != 0)
            {
                if (output.Size is > 0 and <= 16384) Marshal.Copy(new byte[output.Size], 0, output.Data, output.Size);
                LocalFree(output.Data);
            }
            if (description != 0) LocalFree(description);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public nint Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, out nint description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
