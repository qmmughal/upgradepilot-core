using UpgradePilot.Core.Agents.Discovery.FrameworkDetector;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Execution.StackAdapters;

/// <summary>
/// Coordinates a Mixed repo by delegating to the real per-stack adapters - never
/// bespoke Mixed-only logic. <see cref="DotNetStackUpgradeAdapter"/> runs first: the
/// "coordinate-migration-order" step from the plan means the backend (which the
/// frontend calls into) should land in an already-upgraded, already-validated state
/// before the frontend upgrade runs against it, not the reverse. The frontend half then
/// runs through whichever of <see cref="ReactStackUpgradeAdapter"/>/<see cref="NextJsStackUpgradeAdapter"/>
/// <see cref="DetectFrontendKind"/> resolves to - Mixed doesn't get its own copy of
/// framework detection; if neither is a confident match, the frontend half is skipped
/// and flagged, not guessed at. Aggregate confidence is the minimum of both halves - a
/// weak result on either side should not be hidden behind an average.
/// </summary>
public sealed class MixedStackUpgradeAdapter : BaseStackUpgradeAdapter
{
    private readonly DotNetStackUpgradeAdapter _dotNetAdapter;
    private readonly ReactStackUpgradeAdapter _reactAdapter;
    private readonly NextJsStackUpgradeAdapter _nextJsAdapter;

    public MixedStackUpgradeAdapter(IProcessRunner processRunner) : base(StackKind.Mixed)
    {
        _dotNetAdapter = new DotNetStackUpgradeAdapter(processRunner);
        _reactAdapter = new ReactStackUpgradeAdapter(processRunner);
        _nextJsAdapter = new NextJsStackUpgradeAdapter(processRunner);
    }

    public override bool CanHandle(FrameworkProfile profile) => profile.StackKind == StackKind.Mixed || profile.StackKind == StackKind.Unknown;

    public override async Task<StackUpgradePlan> BuildUpgradePlanAsync(
        RepositoryMap repo, FrameworkProfile profile, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var basePlan = await base.BuildUpgradePlanAsync(repo, profile, context, cancellationToken);

        var dotNetPlan = await _dotNetAdapter.BuildUpgradePlanAsync(repo, profile, context, cancellationToken);
        var frontendKind = DetectFrontendKind(repo);
        var frontendPlan = frontendKind is null
            ? null
            : (frontendKind == StackKind.NextJs
                ? await _nextJsAdapter.BuildUpgradePlanAsync(repo, profile, context, cancellationToken)
                : await _reactAdapter.BuildUpgradePlanAsync(repo, profile, context, cancellationToken));

        var plan = basePlan with
        {
            ProjectPath = dotNetPlan.ProjectPath,
            DotNetProjectFilePaths = dotNetPlan.DotNetProjectFilePaths,
            PackageTargetVersions = dotNetPlan.PackageTargetVersions,
            MixedFrontendKind = frontendKind,
            MixedFrontendProjectPath = frontendPlan?.ProjectPath,
            FrontendPackageTargetVersions = frontendPlan?.PackageTargetVersions,
            CodemodTransforms = frontendPlan?.CodemodTransforms
        };

        context.RecordFact("stack-adapter", $"{Kind}-plan", plan);
        return plan;
    }

    public override async Task<AgentResult<StackUpgradeArtifacts>> ApplyAsync(
        StackUpgradePlan plan, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();
        var confidences = new List<int>();
        var changedFiles = new List<string>();

        var dotNetResult = await _dotNetAdapter.ApplyAsync(plan, context, cancellationToken);
        confidences.Add(dotNetResult.Confidence);
        notes.Add($"[backend] {dotNetResult.Explanation}");
        changedFiles.AddRange(dotNetResult.Output.ChangedFiles);

        if (plan.MixedFrontendKind is not null && plan.MixedFrontendProjectPath is not null)
        {
            var frontendPlan = plan with
            {
                ProjectPath = plan.MixedFrontendProjectPath,
                PackageTargetVersions = plan.FrontendPackageTargetVersions
            };

            var frontendAdapter = plan.MixedFrontendKind == StackKind.NextJs
                ? (IStackUpgradeAdapter)_nextJsAdapter
                : _reactAdapter;

            var frontendResult = await frontendAdapter.ApplyAsync(frontendPlan, context, cancellationToken);
            confidences.Add(frontendResult.Confidence);
            notes.Add($"[frontend/{plan.MixedFrontendKind}] {frontendResult.Explanation}");
            changedFiles.AddRange(frontendResult.Output.ChangedFiles);
        }
        else
        {
            notes.Add("[frontend] Could not confidently resolve a React or Next.js frontend - frontend half skipped, flagged for manual review.");
        }

        var overallConfidence = confidences.Count == 0 ? 0 : confidences.Min();
        var artifacts = new StackUpgradeArtifacts(Kind, string.Join(" ", notes), changedFiles);

        context.RecordFact("stack-adapter", $"{Kind}-artifacts", artifacts);
        return AgentResult<StackUpgradeArtifacts>.Create(artifacts, overallConfidence, artifacts.Summary);
    }

    /// <summary>
    /// Deliberately duplicates FrameworkDetectorAgent's package.json content sniff
    /// rather than depending on it: that agent lives in Discovery and only ever
    /// classifies a whole repo's StackKind (which for a Mixed repo is just "Mixed" - it
    /// doesn't separately expose which frontend flavor the Mixed repo contains). Small
    /// and self-contained enough that duplicating it here beats a cross-project refactor
    /// for one caller.
    /// </summary>
    private static StackKind? DetectFrontendKind(RepositoryMap repo)
    {
        var packageJsonPath = repo.Projects
            .Select(p => p.ProjectFilePath)
            .FirstOrDefault(p => p.EndsWith("package.json", StringComparison.OrdinalIgnoreCase));

        if (packageJsonPath is null || !File.Exists(packageJsonPath))
        {
            return null;
        }

        var contents = File.ReadAllText(packageJsonPath);
        if (contents.Contains("\"next\"", StringComparison.OrdinalIgnoreCase))
        {
            return StackKind.NextJs;
        }

        if (contents.Contains("\"react\"", StringComparison.OrdinalIgnoreCase)
            || contents.Contains("\"react-dom\"", StringComparison.OrdinalIgnoreCase))
        {
            return StackKind.React;
        }

        return null;
    }
}
