using UpgradePilot.Core.Agents.Pipeline.Execution.ApiRefactoring;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Execution;

public class ReactCodemodCatalogTests
{
    [Fact]
    public void ResolveTransforms_MatchesReactDomRenderMention_ToReplaceReactDomRenderTransform()
    {
        var transforms = ReactCodemodCatalog.ResolveTransforms(["ReactDOM.render is no longer supported in React 19"]);

        Assert.Contains("replace-reactdom-render", transforms);
    }

    [Fact]
    public void ResolveTransforms_MatchesPropTypesMention_ToPropTypesTransform()
    {
        var transforms = ReactCodemodCatalog.ResolveTransforms(["React.PropTypes has moved into a different package"]);

        Assert.Contains("React-PropTypes-to-prop-types", transforms);
    }

    [Fact]
    public void ResolveTransforms_ReturnsDistinctTransforms_ForMultipleMatchingDescriptions()
    {
        var transforms = ReactCodemodCatalog.ResolveTransforms([
            "createRoot replaces ReactDOM.render",
            "Also removed the old ReactDOM.render escape hatch",
        ]);

        Assert.Single(transforms);
        Assert.Equal("replace-reactdom-render", transforms[0]);
    }

    [Fact]
    public void ResolveTransforms_ReturnsEmpty_WhenNoKeywordsMatch()
    {
        var transforms = ReactCodemodCatalog.ResolveTransforms(["Improved error messages for suspense boundaries"]);

        Assert.Empty(transforms);
    }
}
