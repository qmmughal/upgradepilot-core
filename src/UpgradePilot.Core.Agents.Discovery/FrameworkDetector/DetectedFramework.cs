namespace UpgradePilot.Core.Agents.Discovery.FrameworkDetector;

public enum DetectedFramework
{
    /// <summary>Explicit "could not classify" marker — never a silent gap.</summary>
    Unknown,

    /// <summary>Abp.* packages: classic ASP.NET Boilerplate / AspNet Zero lineage.</summary>
    AbpFrameworkLegacy,

    /// <summary>Volo.Abp.* packages: ABP Framework vNext.</summary>
    AbpFrameworkVNext,
}
