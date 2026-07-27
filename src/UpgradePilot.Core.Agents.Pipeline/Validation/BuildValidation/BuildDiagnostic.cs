namespace UpgradePilot.Core.Agents.Pipeline.Validation.BuildValidation;

public sealed record BuildDiagnostic(string Code, string Message, string? FilePath, int? Line);
