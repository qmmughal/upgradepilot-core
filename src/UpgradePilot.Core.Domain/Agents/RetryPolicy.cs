namespace UpgradePilot.Core.Domain.Agents;

/// <summary>
/// An agent's own retry policy, declared by the agent rather than imposed by the
/// saga. Deliberately independent of any specific retry library (e.g. Polly) so the
/// Domain layer stays free of infrastructure dependencies; the saga/orchestrator
/// translates this into whatever retry mechanism it uses.
/// </summary>
public sealed record RetryPolicy(int MaxAttempts, TimeSpan InitialDelay, bool UseExponentialBackoff)
{
    /// <summary>For deterministic agents where a retry can't change the outcome.</summary>
    public static RetryPolicy None { get; } = new(MaxAttempts: 1, InitialDelay: TimeSpan.Zero, UseExponentialBackoff: false);
}
