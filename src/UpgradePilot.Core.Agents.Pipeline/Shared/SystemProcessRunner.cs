using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UpgradePilot.Core.Agents.Pipeline.Shared;

public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(
        string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken = default)
    {
        // On Windows, tools installed as npm/Corepack shims (npm, npx, yarn, pnpm, ...)
        // are .cmd/.ps1 files, not .exe - Process.Start with UseShellExecute=false won't
        // resolve those via PATH the way it resolves a real .exe like dotnet.exe. Routing
        // through cmd.exe /c fixes that uniformly without needing per-tool special-casing,
        // and is a no-op behavior-wise for real .exe tools.
        (fileName, arguments) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("cmd.exe", $"/c \"{fileName} {arguments}\"")
            : (fileName, arguments);

        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };

        var stdOut = new System.Text.StringBuilder();
        var stdErr = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stdErr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        return new ProcessRunResult(process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }
}
