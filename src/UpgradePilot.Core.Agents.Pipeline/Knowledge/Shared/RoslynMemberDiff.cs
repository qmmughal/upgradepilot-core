using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UpgradePilot.Core.Agents.Pipeline.Knowledge.Shared;

/// <summary>
/// Identifies a type member independent of its implementation, so it survives being
/// renamed in position/formatting between versions. Field declarations combine
/// multiple variables (<c>int a, b;</c>) - each variable gets its own signature.
/// </summary>
public sealed record MemberSignature(string Kind, string Name, string ParametersKey)
{
    public override string ToString() =>
        ParametersKey.Length == 0 ? $"{Kind} {Name}" : $"{Kind} {Name}({ParametersKey})";
}

public enum MemberChangeKind
{
    Unchanged,
    Added,
    Removed,
    Modified,
}

public sealed record MemberDiff(MemberSignature Signature, MemberChangeKind ChangeKind, string? BaseText, string? OtherText);

public sealed record AstDiffResult(string TypeName, IReadOnlyList<MemberDiff> MemberDiffs);

/// <summary>
/// The real AST-level comparison mechanism backing Template Comparator (§4.9) and
/// Semantic Merge Engine (§4.10) - uses Roslyn syntax trees, never regex/text diff, per
/// the "prefer AST transformations" engineering principle. v0.1 scope: single-file,
/// direct members of the first type declaration only (no nested types, no partial
/// classes spanning files) - real for what it covers, narrower than the eventual
/// full-repo implementation.
/// </summary>
public static class RoslynMemberComparator
{
    public sealed record ExtractedType(string TypeName, IReadOnlyList<(MemberSignature Signature, string Text)> Members);

    public static ExtractedType ExtractMembers(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        var typeDecl = root.DescendantNodes().OfType<TypeDeclarationSyntax>().FirstOrDefault()
            ?? throw new InvalidOperationException("No type declaration (class/interface/struct/record) found in source.");

        var members = new List<(MemberSignature, string)>();

        foreach (var member in typeDecl.Members)
        {
            foreach (var (signature, text) in SignatureAndTextFor(member))
            {
                members.Add((signature, text));
            }
        }

        return new ExtractedType(typeDecl.Identifier.Text, members);
    }

    public static AstDiffResult Diff(string baseSource, string otherSource)
    {
        var baseType = ExtractMembers(baseSource);
        var otherType = ExtractMembers(otherSource);

        var baseBySignature = baseType.Members.ToDictionary(m => m.Signature, m => m.Text);
        var otherBySignature = otherType.Members.ToDictionary(m => m.Signature, m => m.Text);

        var allSignatures = baseBySignature.Keys.Union(otherBySignature.Keys);

        var diffs = new List<MemberDiff>();
        foreach (var signature in allSignatures)
        {
            var inBase = baseBySignature.TryGetValue(signature, out var baseText);
            var inOther = otherBySignature.TryGetValue(signature, out var otherText);

            var changeKind = (inBase, inOther) switch
            {
                (true, false) => MemberChangeKind.Removed,
                (false, true) => MemberChangeKind.Added,
                (true, true) when NormalizedEquals(baseText!, otherText!) => MemberChangeKind.Unchanged,
                (true, true) => MemberChangeKind.Modified,
                _ => throw new InvalidOperationException("Signature must exist in at least one side."),
            };

            diffs.Add(new MemberDiff(signature, changeKind, baseText, otherText));
        }

        return new AstDiffResult(otherType.TypeName, diffs);
    }

    /// <summary>Structural comparison via re-parsed, whitespace-normalized text - not a raw string diff.</summary>
    private static bool NormalizedEquals(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

    private static string Normalize(string memberText) =>
        (SyntaxFactory.ParseMemberDeclaration(memberText) as SyntaxNode ?? SyntaxFactory.ParseCompilationUnit(memberText))
            .NormalizeWhitespace()
            .ToFullString();

    private static IEnumerable<(MemberSignature Signature, string Text)> SignatureAndTextFor(MemberDeclarationSyntax member)
    {
        switch (member)
        {
            case MethodDeclarationSyntax method:
                yield return (
                    new MemberSignature("method", method.Identifier.Text, ParameterTypesKey(method.ParameterList)),
                    method.NormalizeWhitespace().ToFullString());
                break;

            case ConstructorDeclarationSyntax ctor:
                yield return (
                    new MemberSignature("constructor", ctor.Identifier.Text, ParameterTypesKey(ctor.ParameterList)),
                    ctor.NormalizeWhitespace().ToFullString());
                break;

            case PropertyDeclarationSyntax property:
                yield return (
                    new MemberSignature("property", property.Identifier.Text, string.Empty),
                    property.NormalizeWhitespace().ToFullString());
                break;

            case FieldDeclarationSyntax field:
                foreach (var variable in field.Declaration.Variables)
                {
                    var singleField = field.WithDeclaration(
                        field.Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(variable)));
                    yield return (
                        new MemberSignature("field", variable.Identifier.Text, string.Empty),
                        singleField.NormalizeWhitespace().ToFullString());
                }

                break;

            default:
                // Nested types, events, indexers, operators: out of v0.1 scope - identified by
                // their full text so they're at least treated as a single opaque unit rather
                // than silently dropped.
                yield return (
                    new MemberSignature("other", member.ToString().GetHashCode().ToString("X8"), string.Empty),
                    member.NormalizeWhitespace().ToFullString());
                break;
        }
    }

    private static string ParameterTypesKey(ParameterListSyntax parameterList) =>
        string.Join(",", parameterList.Parameters.Select(p => p.Type?.ToString() ?? "?"));
}
