using System.Text.Json;
using Microsoft.Data.Sqlite;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Persistence.Sqlite;

/// <summary>
/// The OSS/local implementation of <see cref="IUpgradeContextStore"/> (see
/// docs/architecture/open-core-boundary.md §3) - upgradepilot-cli's default store, no
/// server required. UpgradePilot Cloud implements the same port against a multi-tenant
/// Postgres cluster; that adapter isn't part of this repo.
///
/// Payload is stored as JSON and rehydrated as a <see cref="JsonElement"/> rather than
/// the original CLR type: Domain doesn't (and shouldn't) know about the concrete fact
/// payload types defined in agent projects, so full round-trip deserialization would
/// require a type registry this v0.1 doesn't have. Callers that need the original type
/// back call <c>((JsonElement)fact.Payload).Deserialize&lt;T&gt;()</c>.
/// </summary>
public sealed class SqliteUpgradeContextStore : IUpgradeContextStore
{
    private readonly string _connectionString;

    public SqliteUpgradeContextStore(string databasePath)
    {
        _connectionString = $"Data Source={databasePath}";
        Initialize();
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS upgrade_context_facts (
                fact_id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                agent_id TEXT NOT NULL,
                fact_type TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                recorded_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_upgrade_context_facts_session ON upgrade_context_facts(session_id);
            """;
        command.ExecuteNonQuery();
    }

    public async Task SaveFactAsync(UpgradeContextFact fact, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO upgrade_context_facts (fact_id, session_id, agent_id, fact_type, payload_json, recorded_at)
            VALUES ($factId, $sessionId, $agentId, $factType, $payloadJson, $recordedAt)
            """;
        command.Parameters.AddWithValue("$factId", fact.FactId.ToString());
        command.Parameters.AddWithValue("$sessionId", fact.SessionId.ToString());
        command.Parameters.AddWithValue("$agentId", fact.AgentId);
        command.Parameters.AddWithValue("$factType", fact.FactType);
        command.Parameters.AddWithValue("$payloadJson", JsonSerializer.Serialize(fact.Payload, fact.Payload.GetType()));
        command.Parameters.AddWithValue("$recordedAt", fact.RecordedAt.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UpgradeContextFact>> LoadFactsAsync(
        Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT fact_id, session_id, agent_id, fact_type, payload_json, recorded_at
            FROM upgrade_context_facts
            WHERE session_id = $sessionId
            ORDER BY recorded_at
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString());

        var results = new List<UpgradeContextFact>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var payloadJson = reader.GetString(4);
            var payload = JsonDocument.Parse(payloadJson).RootElement.Clone();

            results.Add(new UpgradeContextFact(
                FactId: Guid.Parse(reader.GetString(0)),
                SessionId: Guid.Parse(reader.GetString(1)),
                AgentId: reader.GetString(2),
                FactType: reader.GetString(3),
                Payload: payload,
                RecordedAt: DateTimeOffset.Parse(reader.GetString(5))));
        }

        return results;
    }
}
