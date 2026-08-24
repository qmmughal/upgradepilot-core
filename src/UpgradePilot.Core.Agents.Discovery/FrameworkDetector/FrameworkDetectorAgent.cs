using UpgradePilot.Core.Agents.Discovery.Ports;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Discovery.FrameworkDetector;

/// <summary>
/// Agent #3 (docs/architecture/agents.md §4.3): classifies each project by framework
/// signature. v0.1 heuristic: package-reference prefix matching plus an angular.json
/// check for a co-located Angular front end. Every project always receives a
/// classification — <see cref="DetectedFramework.Unknown"/> is the explicit
/// "could not classify" marker required by the spec, never a silent gap.
/// </summary>
public sealed class FrameworkDetectorAgent : IUpgradePilotAgent<FrameworkDetectorInput, FrameworkProfile>
{
    private readonly IRepositoryReader _reader;

    public FrameworkDetectorAgent(IRepositoryReader reader)
    {
        _reader = reader;
    }

    public string AgentId => "framework-detector";
    public string Version => "0.1.0";

    public RetryPolicy RetryPolicy => RetryPolicy.None;

    public Task<AgentResult<FrameworkProfile>> ExecuteAsync(
        FrameworkDetectorInput input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var hasAbpVNext = input.VersionManifest.Signals.Any(
            s => s.Source.StartsWith("Volo.Abp", StringComparison.OrdinalIgnoreCase));
        var hasAbpLegacy = input.VersionManifest.Signals.Any(
            s => s.Source.StartsWith("Abp", StringComparison.OrdinalIgnoreCase)
                && !s.Source.StartsWith("Volo.Abp", StringComparison.OrdinalIgnoreCase));

        var classifications = input.RepositoryMap.Projects
            .Select(project => ClassifyProject(project, hasAbpLegacy, hasAbpVNext))
            .ToList();

        var hasAngular = _reader.EnumerateFiles(input.RepositoryMap.RootPath, "angular.json").Any();
        var stackKind = DetectStackKind(input, classifications, hasAngular);
        var nextJsRoutingMode = stackKind == StackKind.NextJs
            ? DetectNextJsRoutingMode(input.RepositoryMap.RootPath)
            : NextJsRoutingMode.Unknown;

        var profile = new FrameworkProfile(classifications, hasAngular, stackKind, nextJsRoutingMode);
        context.RecordFact(AgentId, "framework-profile", profile);

        var unclassifiedCount = classifications.Count(c => c.Framework == DetectedFramework.Unknown);

        var result = AgentResult<FrameworkProfile>.Create(
            profile,
            confidence: classifications.Count == 0 ? 0 : unclassifiedCount == 0 ? 90 : 50,
            explanation: unclassifiedCount == 0
                ? $"Classified all {classifications.Count} project(s)."
                : $"{unclassifiedCount} of {classifications.Count} project(s) could not be classified.",
            citations: [new Citation("Package reference + solution structure heuristics")]);

        return Task.FromResult(result);
    }

    public Task<ValidationResult> ValidateAsync(
        FrameworkProfile output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(ValidationResult.Success());

    private static FrameworkClassification ClassifyProject(ProjectInfo project, bool hasAbpLegacy, bool hasAbpVNext)
    {
        if (hasAbpVNext)
        {
            return new FrameworkClassification(
                project.Name, DetectedFramework.AbpFrameworkVNext, Confidence: 70,
                "Repository references Volo.Abp.* packages.");
        }

        if (hasAbpLegacy)
        {
            return new FrameworkClassification(
                project.Name, DetectedFramework.AbpFrameworkLegacy, Confidence: 70,
                "Repository references Abp.* (legacy ASP.NET Boilerplate / AspNet Zero) packages.");
        }

        return new FrameworkClassification(
            project.Name, DetectedFramework.Unknown, Confidence: 30,
            "No known ABP/AspNet Zero package signature found.");
    }

    private StackKind DetectStackKind(FrameworkDetectorInput input, IReadOnlyList<FrameworkClassification> classifications, bool hasAngular)
    {
        var hasDotNet = classifications.Any(c => c.Framework is DetectedFramework.AbpFrameworkLegacy or DetectedFramework.AbpFrameworkVNext)
            || input.RepositoryMap.Projects.Any(p => p.ProjectFilePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));

        var packageFiles = input.RepositoryMap.Projects
            .Select(p => p.ProjectFilePath)
            .Where(path => path.EndsWith("package.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (packageFiles.Count == 0 && input.RepositoryMap.RootPath is not null)
        {
            var rootPackage = _reader.EnumerateFiles(input.RepositoryMap.RootPath, "package.json").FirstOrDefault();
            if (rootPackage is not null)
            {
                packageFiles.Add(rootPackage);
            }
        }

        var hasJavaScriptApp = packageFiles.Count > 0
            || hasAngular;

        if (hasDotNet && hasJavaScriptApp)
        {
            return StackKind.Mixed;
        }

        if (hasDotNet)
        {
            return StackKind.DotNet;
        }

        foreach (var packageFile in packageFiles)
        {
            var contents = _reader.ReadAllText(packageFile);
            if (contents.Contains("\"next\"", StringComparison.OrdinalIgnoreCase))
            {
                return StackKind.NextJs;
            }

            if (contents.Contains("\"react\"", StringComparison.OrdinalIgnoreCase)
                || contents.Contains("\"react-dom\"", StringComparison.OrdinalIgnoreCase)
                || hasAngular)
            {
                return StackKind.React;
            }
        }

        if (hasAngular)
        {
            return StackKind.React;
        }

        return StackKind.Unknown;
    }

    /// <summary>
    /// `layout.*` is required by every App Router route segment; `_app.*` is the Pages
    /// Router's equivalent required file - both are reliable presence markers, unlike
    /// inferring from directory names alone (a `pages/` directory can also just be a
    /// component folder in an App Router project).
    /// </summary>
    private NextJsRoutingMode DetectNextJsRoutingMode(string? rootPath)
    {
        if (rootPath is null)
        {
            return NextJsRoutingMode.Unknown;
        }

        var hasAppRouter = new[] { "layout.tsx", "layout.jsx", "layout.js", "layout.ts" }
            .Any(marker => _reader.EnumerateFiles(rootPath, marker).Any());
        var hasPagesRouter = new[] { "_app.tsx", "_app.jsx", "_app.js", "_app.ts" }
            .Any(marker => _reader.EnumerateFiles(rootPath, marker).Any());

        return (hasAppRouter, hasPagesRouter) switch
        {
            (true, true) => NextJsRoutingMode.Both,
            (true, false) => NextJsRoutingMode.App,
            (false, true) => NextJsRoutingMode.Pages,
            _ => NextJsRoutingMode.Unknown
        };
    }
}
