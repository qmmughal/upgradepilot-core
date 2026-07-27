using System.Text.Json;
using Microsoft.Data.Sqlite;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Persistence.Sqlite.Tests;

public class SqliteUpgradeContextStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteUpgradeContextStore _store;

    public SqliteUpgradeContextStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"upgradepilot-test-{Guid.NewGuid()}.db");
        _store = new SqliteUpgradeContextStore(_dbPath);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsFactMetadata()
    {
        var sessionId = Guid.NewGuid();
        var context = new UpgradeContext(sessionId);
        var fact = context.RecordFact("repository-analyzer", "repository-map", new { Projects = 3 });

        await _store.SaveFactAsync(fact);
        var loaded = await _store.LoadFactsAsync(sessionId);

        var loadedFact = Assert.Single(loaded);
        Assert.Equal(fact.FactId, loadedFact.FactId);
        Assert.Equal(fact.AgentId, loadedFact.AgentId);
        Assert.Equal(fact.FactType, loadedFact.FactType);
    }

    [Fact]
    public async Task LoadFactsAsync_RehydratesPayloadAsJsonElement_DeserializableToOriginalType()
    {
        var sessionId = Guid.NewGuid();
        var context = new UpgradeContext(sessionId);
        context.RecordFact("version-detector", "version-manifest", new SamplePayload("net10.0", 3));

        foreach (var fact in context.Facts)
        {
            await _store.SaveFactAsync(fact);
        }

        var loaded = await _store.LoadFactsAsync(sessionId);
        var payloadElement = Assert.IsType<JsonElement>(loaded[0].Payload);
        var deserialized = payloadElement.Deserialize<SamplePayload>();

        Assert.Equal("net10.0", deserialized!.TargetFramework);
        Assert.Equal(3, deserialized.ProjectCount);
    }

    [Fact]
    public async Task LoadFactsAsync_OnlyReturnsFactsForRequestedSession()
    {
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        var contextA = new UpgradeContext(sessionA);
        var contextB = new UpgradeContext(sessionB);

        await _store.SaveFactAsync(contextA.RecordFact("agent-a", "fact-a", new { }));
        await _store.SaveFactAsync(contextB.RecordFact("agent-b", "fact-b", new { }));

        var loadedA = await _store.LoadFactsAsync(sessionA);

        var fact = Assert.Single(loadedA);
        Assert.Equal("agent-a", fact.AgentId);
    }

    [Fact]
    public async Task LoadFactsAsync_ReturnsEmpty_WhenSessionUnknown()
    {
        var loaded = await _store.LoadFactsAsync(Guid.NewGuid());

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task RestoreThenSaveThenReload_RoundTripsThroughUpgradeContext()
    {
        var sessionId = Guid.NewGuid();
        var original = new UpgradeContext(sessionId);
        original.RecordFact("repository-analyzer", "repository-map", new { Projects = 2 });
        original.RecordFact("version-detector", "version-manifest", new { });

        foreach (var fact in original.Facts)
        {
            await _store.SaveFactAsync(fact);
        }

        var loadedFacts = await _store.LoadFactsAsync(sessionId);
        var restored = UpgradeContext.Restore(sessionId, loadedFacts);

        Assert.Equal(2, restored.Facts.Count);
        Assert.NotNull(restored.LatestFact("repository-map"));
    }

    private sealed record SamplePayload(string TargetFramework, int ProjectCount);

    public void Dispose()
    {
        // Microsoft.Data.Sqlite pools connections by default, which keeps a file
        // handle open past the `await using` disposal above - clear the pool first.
        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
