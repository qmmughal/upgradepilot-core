using System.Xml.Linq;
using UpgradePilot.Core.Agents.Discovery.Ports;
using UpgradePilot.Core.Agents.Discovery.RepositoryAnalyzer;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Discovery.VersionDetector;

/// <summary>
/// Agent #2 (docs/architecture/agents.md §4.2): determines the exact current version
/// of every relevant framework by parsing project files. Known ABP/AspNet Zero package
/// prefixes are matched explicitly; everything else contributes only its target
/// framework moniker.
/// </summary>
public sealed class VersionDetectorAgent : IUpgradePilotAgent<RepositoryMap, VersionManifest>
{
    private static readonly string[] KnownAbpPackagePrefixes = ["Abp", "Volo.Abp"];

    private readonly IRepositoryReader _reader;

    public VersionDetectorAgent(IRepositoryReader reader)
    {
        _reader = reader;
    }

    public string AgentId => "version-detector";
    public string Version => "0.1.0";

    public RetryPolicy RetryPolicy => RetryPolicy.None;

    public Task<AgentResult<VersionManifest>> ExecuteAsync(
        RepositoryMap input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var signals = new List<FrameworkVersionSignal>();

        foreach (var project in input.Projects)
        {
            XDocument xml;
            try
            {
                xml = XDocument.Parse(_reader.ReadAllText(project.ProjectFilePath));
            }
            catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
            {
                continue;
            }

            var targetFramework = xml.Descendants("TargetFramework").FirstOrDefault()?.Value
                ?? xml.Descendants("TargetFrameworks").FirstOrDefault()?.Value;

            if (!string.IsNullOrWhiteSpace(targetFramework))
            {
                signals.Add(new FrameworkVersionSignal($"{project.Name}:TargetFramework", targetFramework, Confidence: 100));
            }

            foreach (var packageRef in xml.Descendants("PackageReference"))
            {
                var packageName = packageRef.Attribute("Include")?.Value;
                var packageVersion = packageRef.Attribute("Version")?.Value
                    ?? packageRef.Element("Version")?.Value;

                if (packageName is null || packageVersion is null)
                {
                    continue;
                }

                if (KnownAbpPackagePrefixes.Any(prefix => packageName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    signals.Add(new FrameworkVersionSignal(packageName, packageVersion, Confidence: 100));
                }
            }
        }

        var manifest = new VersionManifest(signals);
        context.RecordFact(AgentId, "version-manifest", manifest);

        var result = signals.Count > 0
            ? AgentResult<VersionManifest>.Create(
                manifest,
                confidence: 90,
                explanation: $"Resolved {signals.Count} version signal(s) from project files.",
                citations: [new Citation("Project file (.csproj) inspection")])
            : AgentResult<VersionManifest>.Create(
                manifest,
                confidence: 20,
                explanation: "No target framework or known ABP/AspNet Zero package references found.");

        return Task.FromResult(result);
    }

    public Task<ValidationResult> ValidateAsync(
        VersionManifest output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output.Signals.Count > 0
            ? ValidationResult.Success()
            : ValidationResult.Failure("No version information could be resolved from any project file."));
}
