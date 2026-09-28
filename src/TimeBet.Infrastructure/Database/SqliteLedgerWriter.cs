using Microsoft.Data.Sqlite;
using TimeBet.Domain.Ledger;

namespace TimeBet.Infrastructure.Database;

internal static class SqliteLedgerWriter
{
    public static async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LedgerEntry entry,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ledger_entries
                (id, entry_type, amount_seconds, source_type, source_id, note, occurred_at_utc_ms)
            VALUES
                ($id, $entryType, $amountSeconds, $sourceType, $sourceId, $note, $occurredAt);
            """;
        command.Parameters.AddWithValue("$id", entry.Id.ToString("D"));
        command.Parameters.AddWithValue("$entryType", entry.Type.ToDatabaseValue());
        command.Parameters.AddWithValue("$amountSeconds", entry.AmountSeconds);
        command.Parameters.AddWithValue("$sourceType", entry.SourceType);
        command.Parameters.AddWithValue("$sourceId", entry.SourceId);
        command.Parameters.AddWithValue("$note", (object?)entry.Note ?? DBNull.Value);
        command.Parameters.AddWithValue("$occurredAt", SqliteValueConverters.ToUnixMilliseconds(entry.OccurredAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
