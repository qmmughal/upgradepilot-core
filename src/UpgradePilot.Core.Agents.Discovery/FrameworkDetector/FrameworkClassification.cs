namespace UpgradePilot.Core.Agents.Discovery.FrameworkDetector;

public sealed record FrameworkClassification(
    string ProjectName,
    DetectedFramework Framework,
    int Confidence,
    string Explanation);
