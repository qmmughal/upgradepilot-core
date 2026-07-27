namespace UpgradePilot.Core.Agents.Pipeline.Validation.BuildValidation;

public sealed record BuildResult(bool Succeeded, IReadOnlyList<BuildDiagnostic> Errors, string RawOutput);
