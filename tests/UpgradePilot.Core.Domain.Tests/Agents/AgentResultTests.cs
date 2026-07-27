using UpgradePilot.Core.Domain.Agents;

namespace UpgradePilot.Core.Domain.Tests.Agents;

public class AgentResultTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Create_Throws_WhenConfidenceOutOfRange(int confidence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AgentResult<string>.Create("output", confidence, "explanation"));
    }

    [Fact]
    public void Create_DefaultsCitations_ToEmpty_WhenNotProvided()
    {
        var result = AgentResult<string>.Create("output", 90, "explanation");

        Assert.Empty(result.Citations);
    }

    [Fact]
    public void Create_PreservesCitations_WhenProvided()
    {
        var citation = new Citation("AspNet Zero docs", "https://aspnetzero.com/documents");

        var result = AgentResult<string>.Create("output", 95, "explanation", [citation]);

        Assert.Single(result.Citations);
        Assert.Equal("AspNet Zero docs", result.Citations[0].Source);
    }
}
