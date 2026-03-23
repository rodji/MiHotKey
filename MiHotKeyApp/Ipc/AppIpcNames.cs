namespace MiHotKeyApp.Ipc;

using System.Security.Cryptography;
using System.Text;

internal static class AppIpcNames
{
    public static string GetMutexName(string baseDir)
    {
        return $@"Local\MiHotKey.Instance.{GetStableId(baseDir)}";
    }

    public static string GetPipeName(string baseDir)
    {
        return $"MiHotKey.Command.{GetStableId(baseDir)}";
    }

    private static string GetStableId(string baseDir)
    {
        var fullPath = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var bytes = Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }
}
