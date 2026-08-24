namespace UpgradePilot.Core.Agents.Discovery.FrameworkDetector;

/// <summary>Whether a Next.js repo uses the legacy Pages Router, the App Router, or both mid-migration. Drives which `@next/codemod` transforms apply.</summary>
public enum NextJsRoutingMode
{
    Unknown,
    Pages,
    App,
    Both
}
