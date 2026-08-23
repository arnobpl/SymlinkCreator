namespace SymlinkCreator.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    private TemporaryDirectory(string root)
    {
        Root = root;
        _ = Directory.CreateDirectory(root);
    }

    public string Root { get; }

    public static TemporaryDirectory Create(string? parent = null)
    {
        string basePath = parent ?? Path.GetTempPath();
        string root = Path.Combine(basePath, "SymlinkCreator.Tests_" + Guid.NewGuid().ToString("N"));
        return new TemporaryDirectory(root);
    }

    public string CreateDirectory(string name)
    {
        string path = Path.Combine(Root, name);
        _ = Directory.CreateDirectory(path);
        return path;
    }

    public string CreateFile(string name, string content = "source")
    {
        string path = Path.Combine(Root, name);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
