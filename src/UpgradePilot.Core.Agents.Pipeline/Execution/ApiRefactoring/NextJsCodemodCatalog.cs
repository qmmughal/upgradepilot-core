namespace UpgradePilot.Core.Agents.Pipeline.Execution.ApiRefactoring;

/// <summary>
/// Maps keywords found in Next.js's own release notes to the real transform names
/// shipped in vercel/next.js's packages/next-codemod/transforms directory (verified
/// against that directory listing, not guessed). Same conservative-matching rationale
/// as <see cref="ReactCodemodCatalog"/> - a specific API/feature name only, not generic
/// breaking-change wording.
/// </summary>
public static class NextJsCodemodCatalog
{
    private static readonly (string Keyword, string Transform)[] KeywordToTransform =
    [
        ("cookies()", "next-async-request-api"),
        ("headers()", "next-async-request-api"),
        ("searchparams", "next-async-request-api"),
        ("middleware", "middleware-to-proxy"),
        ("next/image", "next-image-to-legacy-image"),
        ("next/link", "new-link"),
        ("legacybehavior", "new-link"),
        ("next/font", "built-in-next-font"),
        ("viewport", "metadata-to-viewport-export"),
        ("geo", "next-request-geo-ip"),
        ("turbopack", "next-experimental-turbo-to-turbopack"),
        ("next lint", "next-lint-to-eslint-cli"),
        ("partial prerendering", "remove-experimental-ppr"),
        ("ppr", "remove-experimental-ppr"),
        ("partial prefetch", "remove-partial-prefetch"),
        ("unstable_", "remove-unstable-prefix"),
        ("withrouter", "url-to-withrouter"),
    ];

    /// <summary>Case-insensitive substring match against each description; returns distinct transform names in first-seen order.</summary>
    public static IReadOnlyList<string> ResolveTransforms(IEnumerable<string> breakingChangeDescriptions)
    {
        var matched = new List<string>();

        foreach (var description in breakingChangeDescriptions)
        {
            var lower = description.ToLowerInvariant();
            foreach (var (keyword, transform) in KeywordToTransform)
            {
                if (lower.Contains(keyword) && !matched.Contains(transform))
                {
                    matched.Add(transform);
                }
            }
        }

        return matched;
    }
}
