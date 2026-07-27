using System.Diagnostics;

namespace UpgradePilot.Core.Agents.Pipeline.Shared;

public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(
        string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken = default)
    {
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
