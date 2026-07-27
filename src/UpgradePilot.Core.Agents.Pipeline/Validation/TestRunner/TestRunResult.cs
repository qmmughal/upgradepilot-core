namespace UpgradePilot.Core.Agents.Pipeline.Validation.TestRunner;

public sealed record TestRunResult(bool Succeeded, int Total, int Passed, int Failed, int Skipped, string RawOutput);
