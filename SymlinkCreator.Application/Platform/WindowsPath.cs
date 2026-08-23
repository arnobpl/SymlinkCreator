using System.Runtime.InteropServices;

namespace SymlinkCreator.Application.Platform;

internal static partial class WindowsPath
{
    public static string ExpandShortNames(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        char[] buffer = new char[checked(path.Length + 1)];
        uint length = GetLongPathName(path, buffer, checked((uint)buffer.Length));
        if (length == 0)
        {
            // The picker path remains valid even when a parent directory cannot be queried.
            return path;
        }

        if (length >= buffer.Length)
        {
            buffer = new char[checked((int)length)];
            length = GetLongPathName(path, buffer, checked((uint)buffer.Length));
            if (length == 0 || length >= buffer.Length)
            {
                return path;
            }
        }

        return new string(buffer, 0, checked((int)length));
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetLongPathNameW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetLongPathName(
        string shortPath,
        [Out] char[] longPath,
        uint bufferLength);
}
