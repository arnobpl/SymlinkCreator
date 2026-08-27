namespace SymlinkCreator.Application.Core;

public sealed record SymlinkOperationResult(
    SymlinkPlan Plan,
    string? RetainedScriptPath,
    ProcessExecutionResult ProcessResult);

public sealed class SymlinkOperationService(
    ISymlinkPlanner planner,
    ISymlinkScriptGenerator scriptGenerator,
    ScriptWorkspace workspace,
    IPrivilegedProcessRunner processRunner) : ISymlinkOperationService
{
    private readonly ISymlinkPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));
    private readonly ISymlinkScriptGenerator _scriptGenerator = scriptGenerator ?? throw new ArgumentNullException(nameof(scriptGenerator));
    private readonly ScriptWorkspace _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    private readonly IPrivilegedProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public async Task<SymlinkOperationResult> ExecuteAsync(
        SymlinkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        (SymlinkPlan plan, string script) = await Task.Run(
            () => PrepareOperation(request, cancellationToken),
            cancellationToken);
        string scriptPath = _workspace.CreateScriptPath(request.RetainScriptFile);

        try
        {
            await _workspace.WriteScriptAsync(scriptPath, script, cancellationToken);
            ProcessExecutionResult rawProcessResult = await _processRunner.RunAsync(
                scriptPath,
                cancellationToken);
            (string standardError, SymlinkScriptProgress progress) = SymlinkScriptProgressParser.Parse(
                rawProcessResult.ExitCode,
                rawProcessResult.StandardError,
                plan.Entries.Count);
            ProcessExecutionResult processResult = rawProcessResult with
            {
                StandardError = standardError
            };

            return processResult.ExitCode != 0
                ? throw new SymlinkExecutionException(
                    processResult.StandardError,
                    processResult.ExitCode,
                    processResult.WasCancelled,
                    GetFailedLinkPath(plan, processResult, progress),
                    progress,
                    plan.Entries.Count)
                : new SymlinkOperationResult(
                plan,
                request.RetainScriptFile ? scriptPath : null,
                processResult);
        }
        finally
        {
            if (!request.RetainScriptFile)
            {
                ScriptWorkspace.TryDeleteTemporaryFile(scriptPath);
            }
        }
    }

    private (SymlinkPlan Plan, string Script) PrepareOperation(
        SymlinkRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SymlinkPlan plan = _planner.CreatePlan(
            request.SourcePaths,
            request.DestinationDirectory,
            request.UseRelativePath);
        cancellationToken.ThrowIfCancellationRequested();
        string script = _scriptGenerator.Generate(plan);
        cancellationToken.ThrowIfCancellationRequested();
        return (plan, script);
    }

    private static string? GetFailedLinkPath(
        SymlinkPlan plan,
        ProcessExecutionResult result,
        SymlinkScriptProgress progress)
    {
        return !result.WasCancelled &&
            progress.FailedEntryIndex is int index &&
            index >= 0 &&
            index < plan.Entries.Count
            ? plan.Entries[index].LinkPath
            : null;
    }
}
