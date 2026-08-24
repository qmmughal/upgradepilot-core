using UpgradePilot.Core.Agents.Discovery.FrameworkDetector;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Execution.ApiRefactoring;
using UpgradePilot.Core.Agents.Pipeline.Knowledge.UpgradePlanner;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Execution.StackAdapters;

/// <summary>
/// Lives in the Pipeline project (not Discovery) deliberately: a stack adapter's
/// <see cref="ApplyAsync"/> must orchestrate the real per-stack execution agents
/// (Package Upgrade, API Refactoring, Database Migration, ...), all of which live in
/// Pipeline. Discovery has no reference to Pipeline (Pipeline depends on Discovery, not
/// the reverse), so this type cannot live there without a circular dependency.
/// </summary>
public interface IStackUpgradeAdapter
{
    StackKind Kind { get; }

    bool CanHandle(FrameworkProfile profile);

    Task<StackUpgradePlan> BuildUpgradePlanAsync(
        RepositoryMap repo,
        FrameworkProfile profile,
        UpgradeContext context,
        CancellationToken cancellationToken = default);

    Task<AgentResult<StackUpgradeArtifacts>> ApplyAsync(
        StackUpgradePlan plan,
        UpgradeContext context,
        CancellationToken cancellationToken = default);

    Task<ValidationResult> ValidateAsync(
        StackUpgradePlan plan,
        UpgradeContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="ProjectPath"/>/<see cref="PackageTargetVersions"/>/<see cref="CodemodTransforms"/>
/// are optional by design: v0.1 stack adapters can plan and (partially) apply without
/// them, degrading gracefully rather than failing, because the upstream agents that
/// would supply them (Dependency Analyzer's resolved target versions, Release Notes
/// Intelligence's breaking-change-driven codemod selection) aren't wired end-to-end into
/// this path yet - the same documented scope limit as <see cref="UpgradePilot.Core.Agents.Pipeline.Knowledge.UpgradePlanner.UpgradePlannerAgent"/>.
/// Once wired, no interface change is needed here - just populate these fields.
/// </summary>
public sealed record StackUpgradePlan(
    StackKind Kind,
    string Summary,
    IReadOnlyList<string> Steps,
    int Confidence,
    string? ProjectPath = null,
    IReadOnlyDictionary<string, string>? PackageTargetVersions = null,
    IReadOnlyList<string>? CodemodTransforms = null,
    /// <summary>All .csproj files in the repo, for solutions with more than one project - <see cref="ProjectPath"/> alone only covers the first.</summary>
    IReadOnlyList<string>? DotNetProjectFilePaths = null,
    /// <summary>Target `&lt;TargetFramework&gt;` moniker (e.g. "net8.0"), independent of any package version bump.</summary>
    string? DotNetTargetFramework = null,
    /// <summary>Rename codemods to apply, grouped by source file - <see cref="ApiRefactoringAgent"/> operates one file at a time.</summary>
    IReadOnlyList<DotNetRenameTarget>? DotNetRenameTargets = null,
    /// <summary>When set, runs `dotnet ef migrations add` with this name against <see cref="ProjectPath"/> (optionally <see cref="DotNetMigrationStartupProjectPath"/> for the AspNet-Zero-style split EF-Core/Web.Host layout).</summary>
    string? DotNetMigrationName = null,
    string? DotNetMigrationStartupProjectPath = null,
    /// <summary>Mixed-repo only: path to the frontend's package.json - <see cref="ProjectPath"/> is reserved for the backend project in a Mixed plan.</summary>
    string? MixedFrontendProjectPath = null,
    /// <summary>Mixed-repo only: which frontend adapter (React or NextJs) the frontend half resolved to.</summary>
    StackKind? MixedFrontendKind = null,
    /// <summary>Mixed-repo only: npm target versions for the frontend half - kept separate from <see cref="PackageTargetVersions"/> (NuGet) since a Mixed repo has both ecosystems at once.</summary>
    IReadOnlyDictionary<string, string>? FrontendPackageTargetVersions = null);

public sealed record DotNetRenameTarget(string SourcePath, IReadOnlyList<RenameRule> Renames);

public sealed record StackUpgradeArtifacts(
    StackKind Kind,
    string Summary,
    IReadOnlyList<string> ChangedFiles);

/// <summary>
/// Step sequencing for every stack lives in one place -
/// <see cref="StackUpgradeStrategyCatalog"/> (used by both the Upgrade Planner for its
/// recommended-path label and here for the plan's step list) - so the two no longer
/// drift out of sync as they did when each kept its own hardcoded copy.
/// </summary>
public abstract class BaseStackUpgradeAdapter : IStackUpgradeAdapter
{
    protected BaseStackUpgradeAdapter(StackKind kind) => Kind = kind;

    public StackKind Kind { get; }

    public virtual bool CanHandle(FrameworkProfile profile) => profile.StackKind == Kind;

    public virtual Task<StackUpgradePlan> BuildUpgradePlanAsync(
        RepositoryMap repo,
        FrameworkProfile profile,
        UpgradeContext context,
        CancellationToken cancellationToken = default)
    {
        var strategy = StackUpgradeStrategyCatalog.Resolve(Kind);
        var plan = new StackUpgradePlan(
            Kind, $"Upgrade path for {Kind} stack.", strategy.ExecutionSteps, 80, ResolveProjectPath(repo));
        context.RecordFact("stack-adapter", $"{Kind}-plan", plan);
        return Task.FromResult(plan);
    }

    /// <summary>Directory to execute the stack's tooling in. Override per-stack manifest convention; falls back to the repo root.</summary>
    protected virtual string? ResolveProjectPath(RepositoryMap repo) => repo.RootPath;

    public virtual Task<AgentResult<StackUpgradeArtifacts>> ApplyAsync(
        StackUpgradePlan plan,
        UpgradeContext context,
        CancellationToken cancellationToken = default)
    {
        var artifacts = new StackUpgradeArtifacts(plan.Kind, $"Applied {plan.Kind} upgrade plan.", []);

        var result = AgentResult<StackUpgradeArtifacts>.Create(
            artifacts,
            plan.Confidence,
            $"Applied the {plan.Kind} upgrade strategy.");

        context.RecordFact("stack-adapter", $"{plan.Kind}-artifacts", artifacts);
        return Task.FromResult(result);
    }

    public virtual Task<ValidationResult> ValidateAsync(
        StackUpgradePlan plan,
        UpgradeContext context,
        CancellationToken cancellationToken = default)
    {
        context.RecordFact("stack-adapter", $"{plan.Kind}-validation", ValidationResult.Success());
        return Task.FromResult(ValidationResult.Success());
    }
}

public sealed class StackUpgradeRegistry
{
    private readonly IReadOnlyList<IStackUpgradeAdapter> _adapters;

    public StackUpgradeRegistry(IEnumerable<IStackUpgradeAdapter> adapters)
    {
        _adapters = adapters.ToList();
    }

    public IStackUpgradeAdapter Resolve(FrameworkProfile profile)
    {
        var match = _adapters.FirstOrDefault(adapter => adapter.CanHandle(profile));
        return match ?? throw new InvalidOperationException($"No stack adapter available for {profile.StackKind}.");
    }
}

public sealed class StackUpgradeCoordinator
{
    private readonly StackUpgradeRegistry _registry;

    public StackUpgradeCoordinator(StackUpgradeRegistry registry)
    {
        _registry = registry;
    }

    public IStackUpgradeAdapter Resolve(FrameworkProfile profile) => _registry.Resolve(profile);
}
