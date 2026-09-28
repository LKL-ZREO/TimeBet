using System.Globalization;
using Microsoft.Data.Sqlite;
using TimeBet.Application.Abstractions;
using TimeBet.Domain.Ledger;

namespace TimeBet.Infrastructure.Database;

public sealed class SqliteLedgerRepository : ILedgerRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteLedgerRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task AppendAsync(LedgerEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await SqliteLedgerWriter.InsertAsync(connection, transaction, entry, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<long> GetBalanceSecondsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(SUM(amount_seconds), 0) FROM ledger_entries;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }
}
