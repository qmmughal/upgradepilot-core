using UpgradePilot.Core.Agents.Pipeline.Shared;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;

public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly ProcessRunResult _result;

    public FakeProcessRunner(ProcessRunResult result)
    {
        _result = result;
    }

    public string? LastFileName { get; private set; }
    public string? LastArguments { get; private set; }

    public Task<ProcessRunResult> RunAsync(
        string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken = default)
    {
        LastFileName = fileName;
        LastArguments = arguments;
        return Task.FromResult(_result);
    }
}
