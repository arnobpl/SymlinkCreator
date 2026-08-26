using System.Text;
namespace SymlinkCreator.Tests;

[TestClass]
public sealed class SymlinkIntegrationTests(TestContext testContext)
{
    private const int ErrorPrivilegeNotHeld = 1314;

    [TestMethod]
    public async Task GeneratedScriptCreatesUnicodeFileSymbolicLinkWhenTheMachinePermitsIt()
    {
        using var temporary = TemporaryDirectory.Create();
        string sourcePath = temporary.CreateFile(
            Path.Combine("সঙ্গীত & 100%", "গান 01 !.txt"),
            "symbolic link content");
        string destinationDirectory = temporary.CreateDirectory("গন্তব্য folder");
        if (!CanCreateSymbolicLink(sourcePath, destinationDirectory))
        {
            Assert.Inconclusive("The current Windows environment does not grant the symbolic-link privilege.");
        }

        SymlinkPlan plan = new SymlinkPlanner().CreatePlan([sourcePath], destinationDirectory);
        string scriptPath = temporary.CreateFile(
            "create-symlink.cmd",
            new SymlinkScriptGenerator().Generate(plan));

        ProcessTestResult processResult = await ProcessTestHelper.RunBatchAsync(
            scriptPath,
            testContext.CancellationToken);

        Assert.AreEqual(
            0,
            processResult.ExitCode,
            processResult.StandardOutput + processResult.StandardError);
        string linkPath = Path.Combine(destinationDirectory, "গান 01 !.txt");
        Assert.IsTrue(File.Exists(linkPath));
        Assert.IsNotNull(new FileInfo(linkPath).LinkTarget);
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
