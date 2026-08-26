namespace SymlinkCreator.Application.Core;

public interface ISymlinkOperationService
{
    public Task<SymlinkOperationResult> ExecuteAsync(
        SymlinkRequest request,
        CancellationToken cancellationToken);
}
