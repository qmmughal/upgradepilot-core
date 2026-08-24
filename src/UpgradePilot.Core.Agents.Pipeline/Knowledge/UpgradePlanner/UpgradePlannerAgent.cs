using UpgradePilot.Core.Agents.Discovery.FrameworkDetector;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Knowledge.UpgradePlanner;

/// <summary>
/// Agent #11 (docs/architecture/agents.md §4.11): synthesizes upstream signals into an
/// ordered plan with a confidence score. v0.1 operates only on Discovery-phase output
/// (Repository Analyzer/Version Detector/Framework Detector) since the Knowledge-phase
/// agents that would supply breaking-change/merge-conflict/dependency-risk signals
/// (#7-#13) aren't built yet — every risk item is still real and traceable to its
/// source agent, just over a reduced signal set. The confidence formula is
/// deliberately simple arithmetic (not an LLM call) so it stays reproducible from its
/// inputs, per the spec's validation rule.
/// </summary>
public sealed class UpgradePlannerAgent : IUpgradePilotAgent<UpgradePlanInput, UpgradePlan>
{
    private const int DefaultApprovalThreshold = 85;

    /// <summary>The documented pipeline order from docs/architecture/agents.md §3.</summary>
    private static readonly IReadOnlyList<string> PipelineOrder =
    [
        "repository-analyzer", "version-detector", "framework-detector", "dependency-analyzer",
        "repository-intelligence", "documentation-retrieval", "release-notes-intelligence",
        "template-downloader", "template-comparator", "semantic-merge-engine", "upgrade-planner",
        "package-upgrade-agent", "api-refactoring-agent", "database-migration-agent",
        "build-validation-agent", "test-runner-agent", "security-validation-agent",
        "documentation-generator", "pull-request-generator",
    ];

    private readonly int _approvalThreshold;

    public UpgradePlannerAgent(int approvalThreshold = DefaultApprovalThreshold)
    {
        _approvalThreshold = approvalThreshold;
    }

    public string AgentId => "upgrade-planner";
    public string Version => "0.1.0";

    public RetryPolicy RetryPolicy => RetryPolicy.None;

    public Task<AgentResult<UpgradePlan>> ExecuteAsync(
        UpgradePlanInput input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var risks = new List<RiskItem>();
        var confidence = 100;

        if (input.RepositoryMap.Projects.Count == 0)
        {
            risks.Add(new RiskItem("No projects found in repository.", "Critical", "repository-analyzer"));
            confidence = 0;
        }
        else
        {
            var unclassified = input.FrameworkProfile.Classifications.Count(c => c.Framework == DetectedFramework.Unknown);
            if (unclassified > 0)
            {
                risks.Add(new RiskItem(
                    $"{unclassified} of {input.FrameworkProfile.Classifications.Count} project(s) could not be classified to a known framework.",
                    "High", "framework-detector"));
                confidence -= 20;
            }

            var hasAbpSignal = input.VersionManifest.Signals.Any(
                s => s.Source.Contains("Abp", StringComparison.OrdinalIgnoreCase));
            if (!hasAbpSignal)
            {
                risks.Add(new RiskItem(
                    "No ABP/AspNet Zero package signals found - target framework version is unclear.",
                    "High", "version-detector"));
                confidence -= 15;
            }
        }

        confidence = Math.Clamp(confidence, 0, 100);

        var completedAgentIds = context.Facts.Select(f => f.AgentId).ToHashSet();
        var remainingSteps = PipelineOrder.Where(id => !completedAgentIds.Contains(id)).ToList();

        var stackKind = input.FrameworkProfile.StackKind;
        var strategy = StackUpgradeStrategyCatalog.Resolve(stackKind);

        var plan = new UpgradePlan(
            remainingSteps,
            risks,
            confidence,
            RequiresHumanApproval: confidence < _approvalThreshold,
            StackKind: stackKind,
            RecommendedUpgradePath: strategy.RecommendedUpgradePath);

        plan = plan with
        {
            RemainingPipelineSteps = remainingSteps,
            RiskRegister = risks,
            ConfidenceScore = confidence,
            RequiresHumanApproval = confidence < _approvalThreshold,
            StackKind = stackKind,
            RecommendedUpgradePath = strategy.RecommendedUpgradePath
        };
        context.RecordFact(AgentId, "upgrade-plan", plan);

        var explanation = plan.RequiresHumanApproval
            ? $"Confidence {confidence} is below the approval threshold ({_approvalThreshold}) - human approval required before execution."
            : $"Confidence {confidence} meets the approval threshold ({_approvalThreshold}); plan may proceed automatically.";

        var result = AgentResult<UpgradePlan>.Create(
            plan,
            confidence,
            explanation,
            citations: risks.Select(r => new Citation($"{r.SourceAgentId}: {r.Description}")).ToList());

        return Task.FromResult(result);
    }

    public Task<ValidationResult> ValidateAsync(
        UpgradePlan output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output.RiskRegister.All(r => !string.IsNullOrWhiteSpace(r.SourceAgentId))
            ? ValidationResult.Success()
            : ValidationResult.Failure("Every risk item must cite a source agent."));

}
