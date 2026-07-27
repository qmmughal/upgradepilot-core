using UpgradePilot.Core.Agents.Discovery.Ports;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Knowledge.DocumentationRetrieval;

/// <summary>
/// Agent #6 (docs/architecture/agents.md §4.6): version-aware documentation retrieval.
/// Full scope is RAG over Qdrant with embeddings from an LLM provider - neither is
/// available in this environment. This implements exactly the fallback mode the spec
/// itself allows ("fall back to keyword search if embedding service is degraded"): a
/// real term-frequency search over a local markdown/text corpus, not a mock of the
/// real thing. Swapping in vector search later means adding a new
/// IDocumentationSource implementation, not touching this agent's contract.
/// </summary>
public sealed class DocumentationRetrievalAgent : IUpgradePilotAgent<DocumentationRetrievalInput, DocumentationBundle>
{
    private readonly IRepositoryReader _reader;

    public DocumentationRetrievalAgent(IRepositoryReader reader)
    {
        _reader = reader;
    }

    public string AgentId => "documentation-retrieval";
    public string Version => "0.1.0";

    /// <summary>3 attempts on vector-store timeout per spec §4.6 - not applicable to this fallback path, kept for interface parity.</summary>
    public RetryPolicy RetryPolicy => new(MaxAttempts: 3, InitialDelay: TimeSpan.FromSeconds(1), UseExponentialBackoff: true);

    public Task<AgentResult<DocumentationBundle>> ExecuteAsync(
        DocumentationRetrievalInput input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var terms = Tokenize(input.Query);
        var passages = new List<DocumentPassage>();

        foreach (var file in _reader.EnumerateFiles(input.CorpusDirectory, "*.md"))
        {
            var text = _reader.ReadAllText(file);
            var (bestParagraph, score) = BestScoringParagraph(text, terms);

            if (score > 0)
            {
                passages.Add(new DocumentPassage(file, bestParagraph, score));
            }
        }

        var ranked = passages
            .OrderByDescending(p => p.Score)
            .Take(input.MaxResults)
            .ToList();

        var bundle = new DocumentationBundle(ranked);
        context.RecordFact(AgentId, "documentation-bundle", bundle);

        var result = ranked.Count > 0
            ? AgentResult<DocumentationBundle>.Create(
                bundle, 60,
                $"Keyword search found {ranked.Count} relevant passage(s) for '{input.Query}' (fallback mode - no vector/embedding search available).",
                citations: ranked.Select(p => new Citation(p.SourcePath)).ToList())
            : AgentResult<DocumentationBundle>.Create(
                bundle, 0, $"No passages matched '{input.Query}' in '{input.CorpusDirectory}'.");

        return Task.FromResult(result);
    }

    public Task<ValidationResult> ValidateAsync(
        DocumentationBundle output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(output.Passages.All(p => !string.IsNullOrWhiteSpace(p.SourcePath))
            ? ValidationResult.Success()
            : ValidationResult.Failure("Every passage must carry a resolvable source citation."));

    private static string[] Tokenize(string query) =>
        query.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static (string Paragraph, int Score) BestScoringParagraph(string text, string[] terms)
    {
        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        var best = string.Empty;
        var bestScore = 0;

        foreach (var paragraph in paragraphs)
        {
            var score = terms.Sum(term => CountOccurrences(paragraph, term));
            if (score > bestScore)
            {
                bestScore = score;
                best = paragraph.Trim();
            }
        }

        return (best, bestScore);
    }

    private static int CountOccurrences(string haystack, string term)
    {
        if (term.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += term.Length;
        }

        return count;
    }
}
