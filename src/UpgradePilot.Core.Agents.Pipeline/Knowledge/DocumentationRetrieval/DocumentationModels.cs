namespace UpgradePilot.Core.Agents.Pipeline.Knowledge.DocumentationRetrieval;

public sealed record DocumentationRetrievalInput(string CorpusDirectory, string Query, int MaxResults = 5);

public sealed record DocumentPassage(string SourcePath, string Snippet, int Score);

public sealed record DocumentationBundle(IReadOnlyList<DocumentPassage> Passages);
