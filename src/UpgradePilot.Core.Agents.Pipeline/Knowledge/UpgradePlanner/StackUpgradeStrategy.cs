using UpgradePilot.Core.Agents.Discovery.FrameworkDetector;

namespace UpgradePilot.Core.Agents.Pipeline.Knowledge.UpgradePlanner;

public interface IStackUpgradeStrategy
{
    StackKind Kind { get; }

    string RecommendedUpgradePath { get; }

    IReadOnlyList<string> ExecutionSteps { get; }

    IReadOnlyList<string> ValidationSteps { get; }
}

public sealed class DotNetStackUpgradeStrategy : IStackUpgradeStrategy
{
    public StackKind Kind => StackKind.DotNet;

    public string RecommendedUpgradePath => "dotnet-upgrade";

    public IReadOnlyList<string> ExecutionSteps { get; } =
    [
        "analyze-dotnet-version-matrix",
        "upgrade-nuget-packages",
        "refactor-breaking-aspnet-core-apis",
        "apply-ef-core-migration-planning",
        "generate-upgrade-commit"
    ];

    public IReadOnlyList<string> ValidationSteps { get; } =
    [
        "dotnet-build",
        "dotnet-test",
        "ef-migration-validation",
        "security-scan"
    ];
}

public sealed class ReactStackUpgradeStrategy : IStackUpgradeStrategy
{
    public StackKind Kind => StackKind.React;

    public string RecommendedUpgradePath => "react-upgrade";

    public IReadOnlyList<string> ExecutionSteps { get; } =
    [
        "analyze-react-toolchain",
        "upgrade-react-and-dom",
        "migrate-rendering-api",
        "resolve-router-and-state-breaks",
        "update-build-config"
    ];

    public IReadOnlyList<string> ValidationSteps { get; } =
    [
        "npm-install",
        "npm-run-build",
        "npm-test",
        "type-check",
        "lint-and-security-audit"
    ];
}

public sealed class NextJsStackUpgradeStrategy : IStackUpgradeStrategy
{
    public StackKind Kind => StackKind.NextJs;

    public string RecommendedUpgradePath => "nextjs-upgrade";

    public IReadOnlyList<string> ExecutionSteps { get; } =
    [
        "analyze-nextjs-routing-mode",
        "upgrade-nextjs-dependencies",
        "migrate-app-router-or-pages-router",
        "fix-server-client-boundaries",
        "update-images-and-config"
    ];

    public IReadOnlyList<string> ValidationSteps { get; } =
    [
        "next-build",
        "route-smoke-test",
        "type-check",
        "ssr-and-isr-validation"
    ];
}

public static class StackUpgradeStrategyCatalog
{
    public static IStackUpgradeStrategy Resolve(StackKind stackKind) => stackKind switch
    {
        StackKind.DotNet => new DotNetStackUpgradeStrategy(),
        StackKind.React => new ReactStackUpgradeStrategy(),
        StackKind.NextJs => new NextJsStackUpgradeStrategy(),
        StackKind.Mixed => new MixedStackUpgradeStrategy(),
        _ => new UnknownStackUpgradeStrategy()
    };

    private sealed class MixedStackUpgradeStrategy : IStackUpgradeStrategy
    {
        public StackKind Kind => StackKind.Mixed;

        public string RecommendedUpgradePath => "mixed-upgrade";

        public IReadOnlyList<string> ExecutionSteps { get; } =
        [
            "analyze-dotnet-and-frontend-subprojects",
            "upgrade-backend-packages",
            "upgrade-frontend-packages",
            "coordinate-migration-order",
            "run-integrated-validation"
        ];

        public IReadOnlyList<string> ValidationSteps { get; } =
        [
            "dotnet-build",
            "dotnet-test",
            "npm-run-build",
            "npm-test",
            "integration-validation"
        ];
    }

    private sealed class UnknownStackUpgradeStrategy : IStackUpgradeStrategy
    {
        public StackKind Kind => StackKind.Unknown;

        public string RecommendedUpgradePath => "unknown-upgrade";

        public IReadOnlyList<string> ExecutionSteps { get; } = ["classify-stack", "collect-risk-factors", "request-human-review"];

        public IReadOnlyList<string> ValidationSteps { get; } = ["manual-validation-review"];
    }
}
