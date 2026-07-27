using UpgradePilot.Core.Agents.Pipeline.Knowledge.Shared;
using UpgradePilot.Core.Agents.Pipeline.Knowledge.TemplateComparator;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Knowledge;

public class TemplateComparatorAgentTests
{
    private const string Baseline = """
        public class SampleController
        {
            public string GetName()
            {
                return "default";
            }

            public int Count { get; set; }
        }
        """;

    // Customer added a method and changed GetName's body - a realistic AspNet Zero
    // customization pattern (adding a custom endpoint to a generated controller).
    private const string Customer = """
        public class SampleController
        {
            public string GetName()
            {
                return "customer-specific";
            }

            public int Count { get; set; }

            public string GetCustomThing()
            {
                return "custom";
            }
        }
        """;

    // Template v2 renamed/changed Count's accessor and added a new method - a
    // realistic upstream template change between AspNet Zero releases.
    private const string TemplateV2 = """
        public class SampleController
        {
            public string GetName()
            {
                return "default";
            }

            public int Count { get; set; } = 1;

            public void NewTemplateMethod()
            {
            }
        }
        """;

    [Fact]
    public async Task ExecuteAsync_CustomizationSet_DetectsCustomerAddedMethodAndModifiedMethod()
    {
        var agent = new TemplateComparatorAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(
            new TemplateComparatorInput(Customer, Baseline, TemplateV2), context);

        var added = result.Output.CustomizationSet.MemberDiffs.Single(d => d.Signature.Name == "GetCustomThing");
        Assert.Equal(MemberChangeKind.Added, added.ChangeKind);

        var modified = result.Output.CustomizationSet.MemberDiffs.Single(d => d.Signature.Name == "GetName");
        Assert.Equal(MemberChangeKind.Modified, modified.ChangeKind);

        var unchanged = result.Output.CustomizationSet.MemberDiffs.Single(d => d.Signature.Name == "Count");
        Assert.Equal(MemberChangeKind.Unchanged, unchanged.ChangeKind);
    }

    [Fact]
    public async Task ExecuteAsync_TemplateChangeSet_DetectsTemplateAddedAndModifiedMembers()
    {
        var agent = new TemplateComparatorAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(
            new TemplateComparatorInput(Customer, Baseline, TemplateV2), context);

        var added = result.Output.TemplateChangeSet.MemberDiffs.Single(d => d.Signature.Name == "NewTemplateMethod");
        Assert.Equal(MemberChangeKind.Added, added.ChangeKind);

        var modified = result.Output.TemplateChangeSet.MemberDiffs.Single(d => d.Signature.Name == "Count");
        Assert.Equal(MemberChangeKind.Modified, modified.ChangeKind);

        var unchanged = result.Output.TemplateChangeSet.MemberDiffs.Single(d => d.Signature.Name == "GetName");
        Assert.Equal(MemberChangeKind.Unchanged, unchanged.ChangeKind);
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenDiffHasDuplicateSignatures()
    {
        var agent = new TemplateComparatorAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var duplicateSignature = new MemberSignature("method", "Foo", "");
        var badDiff = new UpgradePilot.Core.Agents.Pipeline.Knowledge.Shared.AstDiffResult(
            "Sample",
            [
                new MemberDiff(duplicateSignature, MemberChangeKind.Added, null, "void Foo() {}"),
                new MemberDiff(duplicateSignature, MemberChangeKind.Added, null, "void Foo() {}"),
            ]);
        var badResult = new TemplateComparatorResult(badDiff, new AstDiffResult("Sample", []));

        var validation = await agent.ValidateAsync(badResult, context);

        Assert.False(validation.IsValid);
    }
}
