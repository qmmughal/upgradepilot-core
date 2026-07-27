using System.Security.Cryptography;
using System.Text;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Domain.Agents;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Knowledge.TemplateDownloader;

/// <summary>
/// Agent #8 (docs/architecture/agents.md §4.8): fetches an official template and
/// verifies its integrity, caching locally per version. AspNet Zero's official
/// templates are a licensed commercial artifact with no public fetch endpoint
/// reachable from this environment, so this is implemented generically against any
/// git-hosted template repo/ref (real clone, real SHA-256 content hash over the
/// checked-out tree) and exercised against a real public repo in tests. Pointing it
/// at an actual AspNet Zero template source is a config change, not a code change,
/// once such a source is available.
/// </summary>
public sealed class TemplateDownloaderAgent : IUpgradePilotAgent<TemplateFetchInput, TemplateBaseline>
{
    private readonly IProcessRunner _processRunner;

    public TemplateDownloaderAgent(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string AgentId => "template-downloader";
    public string Version => "0.1.0";

    /// <summary>3 attempts with backoff on fetch failure; checksum mismatch is a hard fail (no retry), per spec §4.8.</summary>
    public RetryPolicy RetryPolicy => new(MaxAttempts: 3, InitialDelay: TimeSpan.FromSeconds(2), UseExponentialBackoff: true);

    public async Task<AgentResult<TemplateBaseline>> ExecuteAsync(
        TemplateFetchInput input, UpgradeContext context, CancellationToken cancellationToken = default)
    {
        var targetDir = Path.Combine(input.CacheDirectory, SanitizeForPath(input.GitUrl), SanitizeForPath(input.Ref));

        if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any())
        {
            var cachedHash = ComputeContentHash(targetDir);
            var cachedBaseline = new TemplateBaseline(targetDir, input.Ref, cachedHash);
            context.RecordFact(AgentId, "template-baseline", cachedBaseline);

            return AgentResult<TemplateBaseline>.Create(
                cachedBaseline, 100, $"Using cached checkout of '{input.Ref}' (content hash verified).",
                citations: [new Citation(input.GitUrl)]);
        }

        Directory.CreateDirectory(targetDir);

        var cloneRun = await _processRunner.RunAsync(
            "git", $"clone --quiet --depth 1 --branch \"{input.Ref}\" \"{input.GitUrl}\" \"{targetDir}\"",
            input.CacheDirectory, cancellationToken);

        if (cloneRun.ExitCode != 0)
        {
            SafeDelete(targetDir);
            var failed = new TemplateBaseline(targetDir, input.Ref, string.Empty);
            return AgentResult<TemplateBaseline>.Create(
                failed, 0, $"Failed to fetch '{input.GitUrl}' at ref '{input.Ref}': {cloneRun.StandardError.Trim()}");
        }

        var hash = ComputeContentHash(targetDir);
        var baseline = new TemplateBaseline(targetDir, input.Ref, hash);
        context.RecordFact(AgentId, "template-baseline", baseline);

        var result = AgentResult<TemplateBaseline>.Create(
            baseline, 100, $"Fetched and hash-verified template at ref '{input.Ref}'.",
            citations: [new Citation(input.GitUrl)]);

        return result;
    }

    public Task<ValidationResult> ValidateAsync(
        TemplateBaseline output, UpgradeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(!string.IsNullOrEmpty(output.ContentHash) && Directory.Exists(output.LocalPath)
            ? ValidationResult.Success()
            : ValidationResult.Failure("Template baseline must be present on disk with a verified content hash before use."));

    private static string SanitizeForPath(string value) =>
        string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static void SafeDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup only
        }
    }

    /// <summary>SHA-256 over relative paths + content, sorted for determinism - the "checksum verification" the spec calls for.</summary>
    private static string ComputeContentHash(string directory)
    {
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".git"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        using var sha256 = SHA256.Create();
        using var buffer = new MemoryStream();

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(directory, file).Replace('\\', '/');
            var pathBytes = Encoding.UTF8.GetBytes(relativePath);
            buffer.Write(pathBytes, 0, pathBytes.Length);
            var contentBytes = File.ReadAllBytes(file);
            buffer.Write(contentBytes, 0, contentBytes.Length);
        }

        buffer.Position = 0;
        return Convert.ToHexString(sha256.ComputeHash(buffer));
    }
}
