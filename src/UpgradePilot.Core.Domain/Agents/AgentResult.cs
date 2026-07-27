namespace UpgradePilot.Core.Domain.Agents;

/// <summary>
/// The result every UpgradePilot agent returns: not just an output, but the confidence
/// behind it, a human-readable explanation, and the citations that justify it. This
/// shape is what makes "every automated decision must be explainable" enforceable at
/// the type level rather than a convention agents can skip.
/// </summary>
public sealed record AgentResult<TOutput>
{
    public required TOutput Output { get; init; }

    /// <summary>0-100. Drives the Upgrade Planner's human-approval gate.</summary>
    public required int Confidence { get; init; }

    public required string Explanation { get; init; }

    public IReadOnlyList<Citation> Citations { get; init; } = [];

    public static AgentResult<TOutput> Create(
        TOutput output,
        int confidence,
        string explanation,
        IReadOnlyList<Citation>? citations = null)
    {
        if (confidence is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence), confidence, "Confidence must be between 0 and 100.");
        }

        return new AgentResult<TOutput>
        {
            Output = output,
            Confidence = confidence,
            Explanation = explanation,
            Citations = citations ?? []
        };
    }
}
