using UpgradePilot.Core.Agents.Pipeline.Knowledge.SemanticMergeEngine;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Knowledge;

public class SemanticMergeEngineAgentTests
{
    private const string Baseline = """
        public class SampleService
        {
            public string Greet()
            {
                return "hello";
            }

            public int Compute()
            {
                return 1;
            }

            public void Untouched()
            {
            }
        }
        """;

    [Fact]
    public async Task ExecuteAsync_PropagatesTemplateChange_WhenCustomerNeverTouchedTheMember()
    {
        // Customer only touched Greet; template only touched Compute -> Compute's
        // template update should propagate cleanly, no conflict.
        const string customer = """
            public class SampleService
            {
                public string Greet()
                {
                    return "customized hello";
                }

                public int Compute()
                {
                    return 1;
                }

                public void Untouched()
                {
                }
            }
            """;

        const string templateV2 = """
            public class SampleService
            {
                public string Greet()
                {
                    return "hello";
                }

                public int Compute()
                {
                    return 2;
                }

                public void Untouched()
                {
                }
            }
            """;

        var agent = new SemanticMergeEngineAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new SemanticMergeInput(customer, Baseline, templateV2), context);

        Assert.Empty(result.Output.Conflicts);
        Assert.Contains("return 2;", result.Output.MergedSource);
        Assert.Contains("customized hello", result.Output.MergedSource);
        Assert.Equal(90, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_RaisesConflict_WhenBothTemplateAndCustomerModifySameMember()
    {
        const string customer = """
            public class SampleService
            {
                public string Greet()
                {
                    return "customer version";
                }

                public int Compute()
                {
                    return 1;
                }

                public void Untouched()
                {
                }
            }
            """;

        const string templateV2 = """
            public class SampleService
            {
                public string Greet()
                {
                    return "template version";
                }

                public int Compute()
                {
                    return 1;
                }

                public void Untouched()
                {
                }
            }
            """;

        var agent = new SemanticMergeEngineAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new SemanticMergeInput(customer, Baseline, templateV2), context);

        var conflict = Assert.Single(result.Output.Conflicts);
        Assert.Equal("Greet", conflict.Signature.Name);
        // Customer's version is preserved in the merge output pending human review - never silently overwritten.
        Assert.Contains("customer version", result.Output.MergedSource);
        Assert.DoesNotContain("template version", result.Output.MergedSource);
        Assert.Equal(40, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_AddsNewTemplateMember_WhenCustomerDidNotIndependentlyAddIt()
    {
        const string customer = Baseline;

        const string templateV2 = """
            public class SampleService
            {
                public string Greet()
                {
                    return "hello";
                }

                public int Compute()
                {
                    return 1;
                }

                public void Untouched()
                {
                }

                public void BrandNewTemplateMethod()
                {
                }
            }
            """;

        var agent = new SemanticMergeEngineAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new SemanticMergeInput(customer, Baseline, templateV2), context);

        Assert.Empty(result.Output.Conflicts);
        Assert.Contains("BrandNewTemplateMethod", result.Output.MergedSource);
    }

    [Fact]
    public async Task ExecuteAsync_RemovesMember_WhenTemplateRemovesIt_AndCustomerNeverTouchedIt()
    {
        const string customer = Baseline;

        const string templateV2 = """
            public class SampleService
            {
                public string Greet()
                {
                    return "hello";
                }

                public void Untouched()
                {
                }
            }
            """;

        var agent = new SemanticMergeEngineAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new SemanticMergeInput(customer, Baseline, templateV2), context);

        Assert.Empty(result.Output.Conflicts);
        Assert.DoesNotContain("Compute", result.Output.MergedSource);
    }

    [Fact]
    public async Task ExecuteAsync_RaisesConflict_WhenTemplateRemovesAMemberTheCustomerCustomized()
    {
        const string customer = """
            public class SampleService
            {
                public string Greet()
                {
                    return "hello";
                }

                public int Compute()
                {
                    return 999; // customer customized this
                }

                public void Untouched()
                {
                }
            }
            """;

        const string templateV2 = """
            public class SampleService
            {
                public string Greet()
                {
                    return "hello";
                }

                public void Untouched()
                {
                }
            }
            """;

        var agent = new SemanticMergeEngineAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new SemanticMergeInput(customer, Baseline, templateV2), context);

        var conflict = Assert.Single(result.Output.Conflicts);
        Assert.Equal("Compute", conflict.Signature.Name);
        Assert.Contains("999", result.Output.MergedSource); // customer's customization preserved despite template removal
    }

    [Fact]
    public async Task ValidateAsync_Succeeds_WhenMergedSourceParsesCleanly()
    {
        var agent = new SemanticMergeEngineAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new SemanticMergeInput(Baseline, Baseline, Baseline), context);
        var validation = await agent.ValidateAsync(result.Output, context);

        Assert.True(validation.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsMergeResultFact()
    {
        var agent = new SemanticMergeEngineAgent();
        var context = new UpgradeContext(Guid.NewGuid());

        await agent.ExecuteAsync(new SemanticMergeInput(Baseline, Baseline, Baseline), context);

        Assert.NotNull(context.LatestFact("merge-result"));
    }
}
