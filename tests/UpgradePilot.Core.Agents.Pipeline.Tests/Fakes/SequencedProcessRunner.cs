using UpgradePilot.Core.Agents.Pipeline.Shared;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;

/// <summary>Returns each given result in order, one per RunAsync call - for agents that shell out more than once per execution.</summary>
public sealed class SequencedProcessRunner(params ProcessRunResult[] results) : IProcessRunner
{
    private int _index;
    public List<(string FileName, string Arguments)> Calls { get; } = [];

    public Task<ProcessRunResult> RunAsync(
        string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken = default)
    {
        Calls.Add((fileName, arguments));
        return Task.FromResult(results[_index++]);
    }
}
