namespace MiHotKeyApp.Ipc;

using System.Security.Cryptography;
using System.Text;
using System.Security.Principal;

internal static class AppIpcNames
{
    public static string GetMutexName(string baseDir)
    {
        return $@"Local\MiHotKey.Instance.{GetStableId(baseDir)}";
    }

    public static int GetLoopbackPort(string baseDir)
    {
        var bytes = GetStableBytes(baseDir, includeUser: true);
        var value = BitConverter.ToUInt16(bytes, 0);
        return 20000 + (value % 20000);
    }

    public static string GetAuthToken(string baseDir)
    {
        return Convert.ToHexString(SHA256.HashData(GetStableBytes(baseDir, includeUser: true)));
    }

    private static string GetStableId(string baseDir)
    {
        var hash = SHA256.HashData(GetStableBytes(baseDir, includeUser: false));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    private static byte[] GetStableBytes(string baseDir, bool includeUser)
    {
        var fullPath = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var text = fullPath.ToUpperInvariant();
        if (includeUser)
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value ?? "nosid";
            text = $"{sid}|{text}";
        }

        return Encoding.UTF8.GetBytes(text);
    }
}
