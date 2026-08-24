namespace UpgradePilot.Core.Agents.Pipeline.Execution.ApiRefactoring;

public sealed record ReactCodemodInput(string ProjectPath, IReadOnlyList<string> Transforms);

public sealed record ReactCodemodRunResult(string Transform, bool Succeeded, string Output);

public sealed record ReactCodemodReport(IReadOnlyList<ReactCodemodRunResult> Runs);
