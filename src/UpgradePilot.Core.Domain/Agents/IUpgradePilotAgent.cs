using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Domain.Agents;

/// <summary>
/// The contract every UpgradePilot agent implements — first-party or community-contributed.
/// The saga, retry pipeline, and telemetry are all written against this interface, not
/// against any individual agent, which is what lets new framework adapters plug in
/// without core changes.
/// </summary>
public interface IUpgradePilotAgent<TInput, TOutput>
{
    /// <summary>Stable identifier, e.g. "template-comparator". Used as the fact author id in <see cref="UpgradeContext"/>.</summary>
    string AgentId { get; }

    /// <summary>Semver. Agents are independently releasable.</summary>
    string Version { get; }

    RetryPolicy RetryPolicy { get; }

    Task<AgentResult<TOutput>> ExecuteAsync(
        TInput input, UpgradeContext context, CancellationToken cancellationToken = default);

    Task<ValidationResult> ValidateAsync(
        TOutput output, UpgradeContext context, CancellationToken cancellationToken = default);
}
