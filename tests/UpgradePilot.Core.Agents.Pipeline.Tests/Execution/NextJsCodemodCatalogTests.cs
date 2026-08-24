using UpgradePilot.Core.Agents.Pipeline.Execution.ApiRefactoring;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Execution;

public class NextJsCodemodCatalogTests
{
    [Fact]
    public void ResolveTransforms_MatchesMiddlewareMention_ToMiddlewareToProxyTransform()
    {
        var transforms = NextJsCodemodCatalog.ResolveTransforms(["Middleware has been renamed to proxy"]);

        Assert.Contains("middleware-to-proxy", transforms);
    }

    [Fact]
    public void ResolveTransforms_MatchesCookiesMention_ToAsyncRequestApiTransform()
    {
        var transforms = NextJsCodemodCatalog.ResolveTransforms(["cookies() is now async and must be awaited"]);

        Assert.Contains("next-async-request-api", transforms);
    }

    [Fact]
    public void ResolveTransforms_ReturnsEmpty_WhenNoKeywordsMatch()
    {
        var transforms = NextJsCodemodCatalog.ResolveTransforms(["Improved build performance for large monorepos"]);

        Assert.Empty(transforms);
    }
}
