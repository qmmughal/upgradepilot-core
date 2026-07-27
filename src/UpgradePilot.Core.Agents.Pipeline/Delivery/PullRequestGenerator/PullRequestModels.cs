namespace UpgradePilot.Core.Agents.Pipeline.Delivery.PullRequestGenerator;

public sealed record PullRequestInput(
    string RepositoryPath,
    string BranchName,
    string BaseBranch,
    string Title,
    string Body,
    bool SecurityBlocksProgression);

public sealed record PullRequestRecord(string Url, bool WasAlreadyOpen);
