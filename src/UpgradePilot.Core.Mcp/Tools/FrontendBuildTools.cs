using System.ComponentModel;
using ModelContextProtocol.Server;
using UpgradePilot.Core.Agents.Pipeline.Shared;

namespace UpgradePilot.Core.Mcp.Tools;

/// <summary>
/// The npm/Node counterpart to <see cref="BuildTools"/> - backs Build Validation Agent
/// (§4.15) and Test Runner Agent (§4.16) for React/Next.js repos, i.e. the
/// `build.compileFrontend` tool the architecture doc names but never implemented.
/// </summary>
[McpServerToolType]
public static class FrontendBuildTools
{
    private static readonly IProcessRunner ProcessRunner = new SystemProcessRunner();

    [McpServerTool(Name = "build_compileFrontend"), Description("Run `npm run build` in the given project directory and return exit code plus output.")]
    public static async Task<string> CompileFrontend([Description("Directory containing the project's package.json")] string projectDirectory)
    {
        var result = await ProcessRunner.RunAsync("npm", "run build", projectDirectory);
        return $"Exit code: {result.ExitCode}\n{result.StandardOutput}{result.StandardError}";
    }

    [McpServerTool(Name = "test_runFrontend"), Description("Run `npm test` in the given project directory and return exit code plus output.")]
    public static async Task<string> RunFrontendTests([Description("Directory containing the project's package.json")] string projectDirectory)
    {
        var result = await ProcessRunner.RunAsync("npm", "test", projectDirectory);
        return $"Exit code: {result.ExitCode}\n{result.StandardOutput}{result.StandardError}";
    }

    [McpServerTool(Name = "build_npmInstall"), Description("Run `npm install` in the given project directory and return exit code plus output.")]
    public static async Task<string> NpmInstall([Description("Directory containing the project's package.json")] string projectDirectory)
    {
        var result = await ProcessRunner.RunAsync("npm", "install", projectDirectory);
        return $"Exit code: {result.ExitCode}\n{result.StandardOutput}{result.StandardError}";
    }
}
