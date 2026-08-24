namespace UpgradePilot.Core.Agents.Pipeline.Execution.ApiRefactoring;

/// <summary>
/// Maps keywords found in React's own release notes to the real transform names shipped
/// in reactjs/react-codemod (verified against that repo's README/LEGACY.md, not
/// guessed - a fabricated transform name would just fail at `npx react-codemod`
/// runtime). Deliberately conservative: only maps a description to a transform when a
/// fairly specific API name appears, not generic words like "breaking" - a false
/// positive here means running a codemod that doesn't apply, which is worse than
/// missing one.
/// </summary>
public static class ReactCodemodCatalog
{
    private static readonly (string Keyword, string Transform)[] KeywordToTransform =
    [
        ("reactdom.render", "replace-reactdom-render"),
        ("createroot", "replace-reactdom-render"),
        ("forwardref", "remove-forward-ref"),
        ("string ref", "replace-string-ref"),
        ("useformstate", "replace-use-form-state"),
        ("proptypes", "React-PropTypes-to-prop-types"),
        ("unsafe_component", "rename-unsafe-lifecycles"),
        ("componentwillmount", "rename-unsafe-lifecycles"),
        ("componentwillreceiveprops", "rename-unsafe-lifecycles"),
        ("componentwillupdate", "rename-unsafe-lifecycles"),
        ("finddomnode", "findDOMNode"),
        ("context.provider", "remove-context-provider"),
        ("usecontext", "use-context-hook"),
        ("act(", "replace-act-import"),
        ("test-utils", "replace-act-import"),
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
