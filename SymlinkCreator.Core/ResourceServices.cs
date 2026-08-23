namespace SymlinkCreator.Core;

public interface IStringResourceService
{
    public string GetString(string key);
}

public interface ISymlinkOperationService
{
    public SymlinkOperationResult Execute(SymlinkRequest request);
}
