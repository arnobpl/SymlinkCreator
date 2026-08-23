namespace SymlinkCreator.Application.Core;

public interface ISymlinkOperationService
{
    public SymlinkOperationResult Execute(SymlinkRequest request);
}
