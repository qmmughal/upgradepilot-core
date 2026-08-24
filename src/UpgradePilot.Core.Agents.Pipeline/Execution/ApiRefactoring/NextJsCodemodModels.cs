namespace UpgradePilot.Core.Agents.Pipeline.Execution.ApiRefactoring;

public sealed record NextJsCodemodInput(string ProjectPath, IReadOnlyList<string> Transforms);

public sealed record NextJsCodemodRunResult(string Transform, bool Succeeded, string Output);

public sealed record NextJsCodemodReport(IReadOnlyList<NextJsCodemodRunResult> Runs);
