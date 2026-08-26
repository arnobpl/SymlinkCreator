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

        SymlinkPlan plan = _planner.CreatePlan(
            request.SourcePaths,
            request.DestinationDirectory,
            request.UseRelativePath);
        // Planning and generation are synchronous; observe cancellation between those phases
        // before creating or running the generated script.
        cancellationToken.ThrowIfCancellationRequested();
        string scriptPath = _workspace.CreateScriptPath(request.RetainScriptFile);

        try
        {
            string script = _scriptGenerator.Generate(plan);
            cancellationToken.ThrowIfCancellationRequested();
            await _workspace.WriteScriptAsync(scriptPath, script, cancellationToken);
            ProcessExecutionResult processResult = await _processRunner.RunAsync(
                scriptPath,
                cancellationToken);

            return processResult.ExitCode != 0
                ? throw new SymlinkExecutionException(
                    CreateFailureMessage(processResult),
                    processResult.ExitCode,
                    processResult.WasCancelled)
                : new SymlinkOperationResult(
                plan,
                request.RetainScriptFile ? scriptPath : null,
                processResult);
        }
        finally
        {
            if (!request.RetainScriptFile)
            {
                ScriptWorkspace.DeleteIfExists(scriptPath);
            }
        }
    }

    private static string CreateFailureMessage(ProcessExecutionResult result)
    {
        if (result.WasCancelled)
        {
            return ProcessExecutionResult.CancellationMessage;
        }

        string detail = result.StandardError.Trim();
        return detail.Length == 0
            ? $"The symlink script exited with code {result.ExitCode}."
            : $"The symlink script exited with code {result.ExitCode}.\n{detail}";
    }
}
