namespace UpgradePilot.Core.Agents.Pipeline.Validation.SecurityValidation;

public sealed record SecurityFinding(string ProjectName, string PackageId, string Severity, string? AdvisoryUrl);

public sealed record SecurityReport(IReadOnlyList<SecurityFinding> Findings, bool BlocksProgression);
