namespace UpgradePilot.Core.Domain.Agents;

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Success() => new(true, []);

    public static ValidationResult Failure(params string[] errors) => new(false, errors);
}
