using System.ComponentModel;
using ModelContextProtocol.Server;
using UpgradePilot.Core.Agents.Pipeline.Shared;

namespace UpgradePilot.Core.Mcp.Tools;

/// <summary>The `build.*`/`test.*` MCP tools, backing Build Validation Agent (§4.15) and Test Runner Agent (§4.16).</summary>
[McpServerToolType]
public static class BuildTools
{
    private static readonly IProcessRunner ProcessRunner = new SystemProcessRunner();

    [McpServerTool(Name = "build_compile"), Description("Run `dotnet build` against a .csproj/.sln/.slnx path and return exit code plus output.")]
    public static async Task<string> Compile([Description("Path to a .csproj, .sln, or .slnx file")] string projectPath)
    {
        var workingDirectory = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
        var result = await ProcessRunner.RunAsync("dotnet", $"build \"{projectPath}\" --nologo", workingDirectory);
        return $"Exit code: {result.ExitCode}\n{result.StandardOutput}{result.StandardError}";
    }

    [McpServerTool(Name = "test_run"), Description("Run `dotnet test` against a test project path and return exit code plus output.")]
    public static async Task<string> Run([Description("Path to a test .csproj file")] string testProjectPath)
    {
        var workingDirectory = Path.GetDirectoryName(testProjectPath) ?? Directory.GetCurrentDirectory();
        var result = await ProcessRunner.RunAsync("dotnet", $"test \"{testProjectPath}\" --nologo", workingDirectory);
        return $"Exit code: {result.ExitCode}\n{result.StandardOutput}{result.StandardError}";
    }
}
