namespace UpgradePilot.Core.Agents.Pipeline.Knowledge.TemplateDownloader;

public sealed record TemplateFetchInput(string GitUrl, string Ref, string CacheDirectory);

public sealed record TemplateBaseline(string LocalPath, string Ref, string ContentHash);
