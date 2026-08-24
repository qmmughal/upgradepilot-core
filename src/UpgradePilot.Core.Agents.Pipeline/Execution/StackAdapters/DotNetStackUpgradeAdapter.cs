using UpgradePilot.Core.Agents.Discovery.FrameworkDetector;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Discovery.DependencyAnalyzer;
using UpgradePilot.Core.Agents.Pipeline.Execution.ApiRefactoring;
using UpgradePilot.Core.Agents.Pipeline.Execution.DatabaseMigration;
using UpgradePilot.Core.Agents.Pipeline.Execution.PackageUpgrade;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Validation.BuildValidation;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Execution.StackAdapters;

/// <summary>
/// Orchestrates the real .NET execution agents across every .csproj in the repo (not
/// just the first): <see cref="DotNetTargetFrameworkUpgradeAgent"/> (TFM bump),
/// <see cref="PackageUpgradeAgent"/> or - when the repo uses Central Package Management -
/// <see cref="CentralPackageVersionUpgradeAgent"/> (package version bump), then
/// <see cref="BuildValidationAgent"/> (dotnet build) per project. Also runs
/// <see cref="ApiRefactoringAgent"/> (rename codemods) and <see cref="DatabaseMigrationAgent"/>
/// (EF Core migration) when <see cref="StackUpgradePlan.DotNetRenameTargets"/>/
/// <see cref="StackUpgradePlan.DotNetMigrationName"/> are supplied - both still require
/// the caller to resolve the rename map / migration name; unlike package target
/// versions, there's no way to derive "rename Foo to Bar" or "this migration is named X"
/// from dependency/release-note data alone.
/// <see cref="BuildUpgradePlanAsync"/> now derives <see cref="StackUpgradePlan.PackageTargetVersions"/>
/// itself via a real <see cref="DependencyAnalyzerAgent"/> run (`dotnet list package
/// --outdated`) - direct package references with a known newer version, only. A caller
/// can still override the field afterwards (e.g. to pin a specific target version) since
/// it's a plain record `with` expression.
/// </summary>
public sealed class DotNetStackUpgradeAdapter : BaseStackUpgradeAdapter
{
    private readonly DependencyAnalyzerAgent _dependencyAnalyzerAgent;
    private readonly DotNetTargetFrameworkUpgradeAgent _targetFrameworkUpgradeAgent;
    private readonly PackageUpgradeAgent _packageUpgradeAgent;
    private readonly CentralPackageVersionUpgradeAgent _centralPackageVersionUpgradeAgent;
    private readonly ApiRefactoringAgent _apiRefactoringAgent;
    private readonly DatabaseMigrationAgent _databaseMigrationAgent;
    private readonly BuildValidationAgent _buildValidationAgent;

    public DotNetStackUpgradeAdapter(IProcessRunner processRunner) : base(StackKind.DotNet)
    {
        _dependencyAnalyzerAgent = new DependencyAnalyzerAgent(processRunner);
        _targetFrameworkUpgradeAgent = new DotNetTargetFrameworkUpgradeAgent(processRunner);
        _packageUpgradeAgent = new PackageUpgradeAgent(processRunner);
        _centralPackageVersionUpgradeAgent = new CentralPackageVersionUpgradeAgent(processRunner);
        _apiRefactoringAgent = new ApiRefactoringAgent();
        _databaseMigrationAgent = new DatabaseMigrationAgent(processRunner);
        _buildValidationAgent = new BuildValidationAgent(processRunner);
    }

    protected override string? ResolveProjectPath(RepositoryMap repo) =>
        repo.Projects.FirstOrDefault(p => p.ProjectFilePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            ?.ProjectFilePath;

    public override async Task<StackUpgradePlan> BuildUpgradePlanAsync(
        RepositoryMap repo, FrameworkProfile profile, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var basePlan = await base.BuildUpgradePlanAsync(repo, profile, context, cancellationToken);

        var csprojPaths = repo.Projects
            .Select(p => p.ProjectFilePath)
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var packageTargetVersions = await ResolveOutdatedDirectPackagesAsync(repo, context, cancellationToken);

        var plan = basePlan with
        {
            DotNetProjectFilePaths = csprojPaths.Count > 0 ? csprojPaths : null,
            PackageTargetVersions = packageTargetVersions
        };
        context.RecordFact("stack-adapter", $"{Kind}-plan", plan);
        return plan;
    }

    /// <summary>Only direct (top-level) package references - bumping a transitive reference directly would fight the dependency graph rather than let restore resolve it.</summary>
    private async Task<IReadOnlyDictionary<string, string>?> ResolveOutdatedDirectPackagesAsync(
        RepositoryMap repo, UpgradeContext context, CancellationToken cancellationToken)
    {
        var dependencyResult = await _dependencyAnalyzerAgent.ExecuteAsync(repo, context, cancellationToken);

        var targetVersions = dependencyResult.Output.Graph.Projects
            .SelectMany(p => p.Packages)
            .Where(p => p.IsDirect && p.LatestVersion is not null && p.LatestVersion != p.ResolvedVersion)
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().LatestVersion!, StringComparer.OrdinalIgnoreCase);

        return targetVersions.Count > 0 ? targetVersions : null;
    }

    public override async Task<AgentResult<StackUpgradeArtifacts>> ApplyAsync(
        StackUpgradePlan plan, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var projectPaths = plan.DotNetProjectFilePaths
            ?? (plan.ProjectPath is null ? [] : [plan.ProjectPath]);

        if (projectPaths.Count == 0)
        {
            var skipped = new StackUpgradeArtifacts(Kind, "No .csproj path resolved - nothing applied.", []);
            return AgentResult<StackUpgradeArtifacts>.Create(skipped, 0, skipped.Summary);
        }

        var changedFiles = new List<string>();
        var confidences = new List<int>();
        var notes = new List<string>();

        var directoryPackagesPropsPath = FindDirectoryPackagesProps(Path.GetDirectoryName(projectPaths[0]));
        var usesCentralPackageManagement = directoryPackagesPropsPath is not null && plan.PackageTargetVersions is { Count: > 0 };

        if (usesCentralPackageManagement)
        {
            var centralResult = await _centralPackageVersionUpgradeAgent.ExecuteAsync(
                new CentralPackageVersionInput(directoryPackagesPropsPath!, projectPaths[0], plan.PackageTargetVersions!),
                context, cancellationToken);

            confidences.Add(centralResult.Confidence);
            notes.Add($"{Path.GetFileName(directoryPackagesPropsPath)}: {centralResult.Explanation}");
            if (centralResult.Output.Updates.Count > 0)
            {
                changedFiles.Add(directoryPackagesPropsPath!);
            }
        }

        var anyPerProjectStepRequested = plan.DotNetTargetFramework is not null
            || (plan.PackageTargetVersions is { Count: > 0 } && !usesCentralPackageManagement);

        foreach (var projectPath in projectPaths)
        {
            if (plan.DotNetTargetFramework is not null)
            {
                var tfmResult = await _targetFrameworkUpgradeAgent.ExecuteAsync(
                    new DotNetTargetFrameworkInput(projectPath, plan.DotNetTargetFramework), context, cancellationToken);

                confidences.Add(tfmResult.Confidence);
                notes.Add($"{Path.GetFileName(projectPath)}: {tfmResult.Explanation}");
                if (tfmResult.Output.Changed)
                {
                    changedFiles.Add(projectPath);
                }
            }

            if (plan.PackageTargetVersions is { Count: > 0 } && !usesCentralPackageManagement)
            {
                var packageResult = await _packageUpgradeAgent.ExecuteAsync(
                    new PackageUpgradeInput(projectPath, plan.PackageTargetVersions), context, cancellationToken);

                confidences.Add(packageResult.Confidence);
                notes.Add($"{Path.GetFileName(projectPath)}: {packageResult.Explanation}");
                if (packageResult.Output.Updates.Count > 0 && !changedFiles.Contains(projectPath))
                {
                    changedFiles.Add(projectPath);
                }
            }

            if (anyPerProjectStepRequested || usesCentralPackageManagement)
            {
                var buildResult = await _buildValidationAgent.ExecuteAsync(projectPath, context, cancellationToken);
                confidences.Add(buildResult.Confidence);
                notes.Add($"{Path.GetFileName(projectPath)}: {buildResult.Explanation}");
            }
        }

        if (plan.DotNetRenameTargets is { Count: > 0 })
        {
            foreach (var target in plan.DotNetRenameTargets)
            {
                var renameResult = await _apiRefactoringAgent.ExecuteAsync(
                    new ApiRefactoringInput(target.SourcePath, target.Renames), context, cancellationToken);

                confidences.Add(renameResult.Confidence);
                notes.Add($"{Path.GetFileName(target.SourcePath)}: {renameResult.Explanation}");
                if (renameResult.Output.Changes.Count > 0)
                {
                    changedFiles.Add(target.SourcePath);
                }
            }
        }

        if (plan.DotNetMigrationName is not null && plan.ProjectPath is not null)
        {
            var migrationResult = await _databaseMigrationAgent.ExecuteAsync(
                new MigrationInput(plan.ProjectPath, plan.DotNetMigrationName, plan.DotNetMigrationStartupProjectPath),
                context, cancellationToken);

            confidences.Add(migrationResult.Confidence);
            notes.Add(migrationResult.Explanation);
        }

        if (plan.DotNetRenameTargets is { Count: > 0 } || plan.DotNetMigrationName is not null)
        {
            var postChangeBuildResult = await _buildValidationAgent.ExecuteAsync(projectPaths[0], context, cancellationToken);
            confidences.Add(postChangeBuildResult.Confidence);
            notes.Add($"Post-refactor/migration build: {postChangeBuildResult.Explanation}");
        }

        if (confidences.Count == 0)
        {
            notes.Add("No target framework, package target versions, renames, or migration were supplied; nothing to apply.");
        }

        var overallConfidence = confidences.Count == 0 ? 0 : confidences.Min();
        var artifacts = new StackUpgradeArtifacts(Kind, string.Join(" ", notes), changedFiles);

        context.RecordFact("stack-adapter", $"{Kind}-artifacts", artifacts);
        return AgentResult<StackUpgradeArtifacts>.Create(artifacts, overallConfidence, artifacts.Summary);
    }

    /// <summary>Walks up from a project's directory looking for a shared Directory.Packages.props - the standard single-file-at-solution-root Central Package Management layout.</summary>
    private static string? FindDirectoryPackagesProps(string? startDirectory)
    {
        var dir = startDirectory is null ? null : new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Directory.Packages.props");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
