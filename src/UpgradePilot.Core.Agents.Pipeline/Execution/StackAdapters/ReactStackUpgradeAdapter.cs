using UpgradePilot.Core.Agents.Discovery.FrameworkDetector;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Discovery.DependencyAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Execution.ApiRefactoring;
using UpgradePilot.Core.Agents.Pipeline.Execution.PackageUpgrade;
using UpgradePilot.Core.Agents.Pipeline.Knowledge.ReleaseNotesIntelligence;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Validation.BuildValidation;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Execution.StackAdapters;

/// <summary>
/// Orchestrates the real React execution agents: <see cref="JsPackageUpgradeAgent"/>
/// (package.json + npm install), <see cref="ReactCodemodAgent"/> (react-codemod
/// transforms), <see cref="FrontendBuildValidationAgent"/> (npm run build). Composes its
/// own agents from an <see cref="IProcessRunner"/> rather than taking each agent as a
/// constructor parameter, so call sites (tests included) stay simple. <see cref="StackUpgradePlan.ProjectPath"/>
/// is the path to package.json itself (not its directory) - kept consistent with
/// <see cref="DotNetStackUpgradeAdapter"/>, where the equivalent is the .csproj path.
/// <see cref="BuildUpgradePlanAsync"/> now derives both <see cref="StackUpgradePlan.PackageTargetVersions"/>
/// (real `npm outdated` via <see cref="JsDependencyAnalyzerAgent"/>) and
/// <see cref="StackUpgradePlan.CodemodTransforms"/> (React's own real release notes via
/// <see cref="ReleaseNotesIntelligenceAgent"/>, matched against <see cref="ReactCodemodCatalog"/>) -
/// a caller can still override either field afterwards via a record `with` expression.
/// </summary>
public sealed class ReactStackUpgradeAdapter : BaseStackUpgradeAdapter
{
    private readonly JsDependencyAnalyzerAgent _dependencyAnalyzerAgent;
    private readonly ReleaseNotesIntelligenceAgent _releaseNotesIntelligenceAgent;
    private readonly JsPackageUpgradeAgent _packageUpgradeAgent;
    private readonly ReactCodemodAgent _codemodAgent;
    private readonly FrontendBuildValidationAgent _buildValidationAgent;

    public ReactStackUpgradeAdapter(IProcessRunner processRunner) : base(StackKind.React)
    {
        _dependencyAnalyzerAgent = new JsDependencyAnalyzerAgent(processRunner);
        _releaseNotesIntelligenceAgent = new ReleaseNotesIntelligenceAgent(processRunner);
        _packageUpgradeAgent = new JsPackageUpgradeAgent(processRunner);
        _codemodAgent = new ReactCodemodAgent(processRunner);
        _buildValidationAgent = new FrontendBuildValidationAgent(processRunner);
    }

    /// <summary>Prefers the discovered package.json project over the (possibly .NET-mixed) repo root.</summary>
    protected override string? ResolveProjectPath(RepositoryMap repo) =>
        repo.Projects.FirstOrDefault(p => p.ProjectFilePath.EndsWith("package.json", StringComparison.OrdinalIgnoreCase))
            ?.ProjectFilePath
        ?? (repo.RootPath is null ? null : Path.Combine(repo.RootPath, "package.json"));

    public override async Task<StackUpgradePlan> BuildUpgradePlanAsync(
        RepositoryMap repo, FrameworkProfile profile, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var basePlan = await base.BuildUpgradePlanAsync(repo, profile, context, cancellationToken);

        var packageTargetVersions = basePlan.ProjectPath is null
            ? null
            : await ResolveOutdatedPackagesAsync(basePlan.ProjectPath, context, cancellationToken);

        var codemodTransforms = await ResolveCodemodTransformsAsync(context, cancellationToken);

        var plan = basePlan with { PackageTargetVersions = packageTargetVersions, CodemodTransforms = codemodTransforms };
        context.RecordFact("stack-adapter", $"{Kind}-plan", plan);
        return plan;
    }

    /// <summary>Reads `latest`, not `wanted` - the whole point of an upgrade tool is pushing past the current package.json range, not just satisfying it.</summary>
    private async Task<IReadOnlyDictionary<string, string>?> ResolveOutdatedPackagesAsync(
        string packageJsonPath, UpgradeContext context, CancellationToken cancellationToken)
    {
        var projectDirectory = Path.GetDirectoryName(packageJsonPath) ?? Directory.GetCurrentDirectory();
        var dependencyResult = await _dependencyAnalyzerAgent.ExecuteAsync(projectDirectory, context, cancellationToken);

        var targetVersions = dependencyResult.Output.Outdated
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().LatestVersion, StringComparer.OrdinalIgnoreCase);

        return targetVersions.Count > 0 ? targetVersions : null;
    }

    /// <summary>Not yet scoped to the exact current-&gt;target version range (that needs VersionManifest wired into this path too) - matches keywords against React's most recent releases instead, documented v0.1 simplification.</summary>
    private async Task<IReadOnlyList<string>?> ResolveCodemodTransformsAsync(UpgradeContext context, CancellationToken cancellationToken)
    {
        var releaseResult = await _releaseNotesIntelligenceAgent.ExecuteAsync(
            new ReleaseNotesInput("facebook", "react", MaxReleases: 5), context, cancellationToken);

        var breakingDescriptions = releaseResult.Output.Items
            .Where(i => i.Category == ChangeCategory.Breaking)
            .Select(i => i.Description);

        var transforms = ReactCodemodCatalog.ResolveTransforms(breakingDescriptions);
        return transforms.Count > 0 ? transforms : null;
    }

    public override async Task<AgentResult<StackUpgradeArtifacts>> ApplyAsync(
        StackUpgradePlan plan, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        if (plan.ProjectPath is null)
        {
            var skipped = new StackUpgradeArtifacts(Kind, "No package.json path resolved - nothing applied.", []);
            return AgentResult<StackUpgradeArtifacts>.Create(skipped, 0, skipped.Summary);
        }

        var projectDirectory = Path.GetDirectoryName(plan.ProjectPath) ?? Directory.GetCurrentDirectory();
        var changedFiles = new List<string>();
        var confidences = new List<int>();
        var notes = new List<string>();

        if (plan.PackageTargetVersions is { Count: > 0 })
        {
            var packageResult = await _packageUpgradeAgent.ExecuteAsync(
                new JsPackageUpgradeInput(plan.ProjectPath, plan.PackageTargetVersions), context, cancellationToken);

            confidences.Add(packageResult.Confidence);
            notes.Add(packageResult.Explanation);
            if (packageResult.Output.Updates.Count > 0)
            {
                changedFiles.Add(plan.ProjectPath);
            }
        }

        if (plan.CodemodTransforms is { Count: > 0 })
        {
            var codemodResult = await _codemodAgent.ExecuteAsync(
                new ReactCodemodInput(projectDirectory, plan.CodemodTransforms), context, cancellationToken);

            confidences.Add(codemodResult.Confidence);
            notes.Add(codemodResult.Explanation);
        }

        if (confidences.Count > 0)
        {
            var buildResult = await _buildValidationAgent.ExecuteAsync(projectDirectory, context, cancellationToken);
            confidences.Add(buildResult.Confidence);
            notes.Add(buildResult.Explanation);
        }
        else
        {
            notes.Add("No package target versions or codemod transforms were supplied; nothing to apply.");
        }

        var overallConfidence = confidences.Count == 0 ? 0 : confidences.Min();
        var artifacts = new StackUpgradeArtifacts(Kind, string.Join(" ", notes), changedFiles);

        context.RecordFact("stack-adapter", $"{Kind}-artifacts", artifacts);
        return AgentResult<StackUpgradeArtifacts>.Create(artifacts, overallConfidence, artifacts.Summary);
    }
}
