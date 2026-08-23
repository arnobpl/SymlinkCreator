using System.Diagnostics;
using System.Text;
using SymlinkCreator.Core;

namespace SymlinkCreator.Tests;

[TestClass]
public sealed class SymlinkIntegrationTests(TestContext testContext)
{
    private const int ErrorPrivilegeNotHeld = 1314;

    [TestMethod]
    [Timeout(15_000, CooperativeCancellation = true)]
    public async Task GeneratedScriptCreatesFileSymbolicLinkWhenTheMachinePermitsIt()
    {
        using var temporary = TemporaryDirectory.Create();
        string sourcePath = temporary.CreateFile("source.txt", "symbolic link content");
        string destinationDirectory = temporary.CreateDirectory("destination");
        if (!CanCreateSymbolicLink(sourcePath, destinationDirectory))
        {
            Assert.Inconclusive("The current Windows environment does not grant the symbolic-link privilege.");
        }

        SymlinkPlan plan = new SymlinkPlanner().CreatePlan([sourcePath], destinationDirectory);
        string scriptPath = temporary.CreateFile(
            "create-symlink.cmd",
            new SymlinkScriptGenerator().Generate(plan));

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(scriptPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The generated symlink script could not be started.");
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(testContext.CancellationToken);
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(testContext.CancellationToken);
        try
        {
            await process.WaitForExitAsync(testContext.CancellationToken);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }

        string output = (await standardOutputTask) + (await standardErrorTask);

        Assert.AreEqual(0, process.ExitCode, output);
        string linkPath = Path.Combine(destinationDirectory, "source.txt");
        Assert.IsTrue(File.Exists(linkPath));
        Assert.AreEqual("symbolic link content", File.ReadAllText(linkPath, Encoding.UTF8));
    }

    private static bool CanCreateSymbolicLink(string sourcePath, string destinationDirectory)
    {
        string probePath = Path.Combine(destinationDirectory, ".symlink-privilege-probe");
        try
        {
            _ = File.CreateSymbolicLink(probePath, sourcePath);
            return true;
        }
        catch (IOException exception) when ((exception.HResult & 0xFFFF) == ErrorPrivilegeNotHeld)
        {
            return false;
        }
        finally
        {
            File.Delete(probePath);
        }
    }
}
