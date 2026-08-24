namespace UpgradePilot.Core.Agents.Pipeline.Validation.TestRunner;

/// <summary>
/// <see cref="Total"/>/<see cref="Passed"/>/<see cref="Failed"/>/<see cref="Skipped"/>
/// are null when the runner's summary line doesn't match a recognized format (Jest,
/// Vitest) - <see cref="Succeeded"/> (from the process exit code) is always reliable
/// regardless, so callers that only need pass/fail are unaffected either way.
/// </summary>
public sealed record FrontendTestResult(
    bool Succeeded,
    string RawOutput,
    int? Total = null,
    int? Passed = null,
    int? Failed = null,
    int? Skipped = null);
