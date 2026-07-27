using UpgradePilot.Core.Agents.Pipeline.Execution.ApiRefactoring;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Execution;

public class ApiRefactoringAgentTests : IDisposable
{
    private readonly string _fixturePath = Path.Combine(Path.GetTempPath(), $"upgradepilot-refactor-{Guid.NewGuid()}.cs");

    private const string OriginalSource = """
        public class SampleService
        {
            public string OldMethodName()
            {
                return OldMethodName_Helper();
            }

            private string OldMethodName_Helper() => "value";

            public string CallSite()
            {
                return OldMethodName();
            }
        }
        """;

    [Fact]
    public async Task ExecuteAsync_RenamesDeclarationAndAllUsages()
    {
        await File.WriteAllTextAsync(_fixturePath, OriginalSource);

        var agent = new ApiRefactoringAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(
            new ApiRefactoringInput(_fixturePath, [new RenameRule("OldMethodName", "NewMethodName")]), context);

        Assert.DoesNotContain("OldMethodName", RemoveHelperOccurrences(result.Output.RefactoredSource));
        Assert.Contains("public string NewMethodName()", result.Output.RefactoredSource);

        var change = Assert.Single(result.Output.Changes);
        Assert.Equal(2, change.OccurrencesReplaced); // declaration + one call site
    }

    [Fact]
    public async Task ExecuteAsync_PreservesUnrelatedIdentifiers()
    {
        await File.WriteAllTextAsync(_fixturePath, OriginalSource);

        var agent = new ApiRefactoringAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(
            new ApiRefactoringInput(_fixturePath, [new RenameRule("OldMethodName", "NewMethodName")]), context);

        Assert.Contains("OldMethodName_Helper", result.Output.RefactoredSource);
    }

    [Fact]
    public async Task ValidateAsync_Succeeds_WhenRefactoredSourceParsesCleanly()
    {
        await File.WriteAllTextAsync(_fixturePath, OriginalSource);

        var agent = new ApiRefactoringAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(
            new ApiRefactoringInput(_fixturePath, [new RenameRule("OldMethodName", "NewMethodName")]), context);
        var validation = await agent.ValidateAsync(result.Output, context);

        Assert.True(validation.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsZeroOccurrences_WhenRenameTargetNotFound()
    {
        await File.WriteAllTextAsync(_fixturePath, OriginalSource);

        var agent = new ApiRefactoringAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(
            new ApiRefactoringInput(_fixturePath, [new RenameRule("NoSuchMethod", "Whatever")]), context);

        Assert.Empty(result.Output.Changes);
    }

    private static string RemoveHelperOccurrences(string source) => source.Replace("OldMethodName_Helper", "");

    public void Dispose()
    {
        if (File.Exists(_fixturePath))
        {
            File.Delete(_fixturePath);
        }
    }
}
