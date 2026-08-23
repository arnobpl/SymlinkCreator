using System.Runtime.InteropServices;

namespace SymlinkCreator.Tests;

[TestClass]
public sealed class WindowsPathTests
{
    [TestMethod]
    public void ExpandShortNamesRestoresTheCanonicalPickerPath()
    {
        using var temporary = TemporaryDirectory.Create();
        string longPath = temporary.CreateFile(
            "a source file with a deliberately long name.txt",
            "content");
        string shortPath = GetShortPath(longPath);

        if (string.Equals(shortPath, longPath, StringComparison.Ordinal))
        {
            Assert.Inconclusive("The test volume does not provide an alternate short name.");
        }

        Assert.AreEqual(longPath, WindowsPath.ExpandShortNames(shortPath));
    }

    private static string GetShortPath(string path)
    {
        char[] buffer = new char[checked(path.Length + 1)];
        uint length = GetShortPathName(path, buffer, checked((uint)buffer.Length));
        if (length >= buffer.Length)
        {
            buffer = new char[checked((int)length)];
            length = GetShortPathName(path, buffer, checked((uint)buffer.Length));
        }

        return length == 0 || length >= buffer.Length
            ? throw new InvalidOperationException("Windows could not produce a short path for the test fixture.")
            : new string(buffer, 0, checked((int)length));
    }

    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetShortPathName(
        string longPath,
        [Out] char[] shortPath,
        uint bufferLength);
}
