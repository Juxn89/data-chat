namespace DataChat.Infrastructure.Persistence;

using DataChat.Application.Features.Datasets;
using DataChat.Domain.Datasets;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

/// <summary>
/// Stores a dataset in one transaction: its table, its rows (binary COPY) and its description. A failed import leaves
/// nothing behind. Every identifier in the DDL comes from the domain (<c>ds_</c> plus an id, <c>c_001</c>…), never
/// from the file.
/// </summary>
/// <param name="db">The context.</param>
internal sealed class DatasetStore(DataChatDbContext db) : IDatasetStore
{
	public async Task SaveAsync(Dataset dataset, IReadOnlyList<object?[]> rows, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(dataset);
		ArgumentNullException.ThrowIfNull(rows);

		await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
		var connection = (NpgsqlConnection)db.Database.GetDbConnection();

		var columns = string.Join(", ", dataset.Columns.Select(static column => $"{column.PhysicalName} {SqlType(column.Type)}"));
		await ExecuteAsync(connection, $"CREATE TABLE {dataset.TableName} ({columns})", cancellationToken).ConfigureAwait(false);

		var names = string.Join(", ", dataset.Columns.Select(static column => column.PhysicalName));
		await using (var copy = await connection.BeginBinaryImportAsync($"COPY {dataset.TableName} ({names}) FROM STDIN (FORMAT BINARY)", cancellationToken).ConfigureAwait(false))
		{
			foreach (var row in rows)
			{
				await copy.StartRowAsync(cancellationToken).ConfigureAwait(false);
				for (var c = 0; c < dataset.Columns.Count; c++)
				{
					if (row[c] is null)
					{
						await copy.WriteNullAsync(cancellationToken).ConfigureAwait(false);
					}
					else
					{
						await copy.WriteAsync(row[c], DbType(dataset.Columns[c].Type), cancellationToken).ConfigureAwait(false);
					}
				}
			}

			await copy.CompleteAsync(cancellationToken).ConfigureAwait(false);
		}

		// Questions filter and group by date more than anything else.
		foreach (var date in dataset.Columns.Where(static column => column.Type == ColumnType.Date))
		{
			await ExecuteAsync(connection, $"CREATE INDEX ON {dataset.TableName} ({date.PhysicalName})", cancellationToken).ConfigureAwait(false);
		}

		await ExecuteAsync(connection, $"ANALYZE {dataset.TableName}", cancellationToken).ConfigureAwait(false);

		db.Datasets.Add(ToRecord(dataset));
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<Dataset>> ListAsync(CancellationToken cancellationToken) =>
		(await db.Datasets.AsNoTracking().OrderByDescending(static d => d.ImportedAt).ToListAsync(cancellationToken).ConfigureAwait(false))
			.Select(ToDomain)
			.ToArray();

	public async Task<Dataset?> FindAsync(Guid id, CancellationToken cancellationToken) =>
		await db.Datasets.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken).ConfigureAwait(false) is { } record
			? ToDomain(record)
			: null;

	private static string SqlType(ColumnType type) => type switch
	{
		ColumnType.Date => "date",
		ColumnType.Integer => "bigint",
		ColumnType.Decimal => "numeric",
		_ => "text",
	};

	private static NpgsqlDbType DbType(ColumnType type) => type switch
	{
		ColumnType.Date => NpgsqlDbType.Date,
		ColumnType.Integer => NpgsqlDbType.Bigint,
		ColumnType.Decimal => NpgsqlDbType.Numeric,
		_ => NpgsqlDbType.Text,
	};

	private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand(sql, connection);
		await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
	}

	private static DatasetRecord ToRecord(Dataset dataset) => new()
	{
		Id = dataset.Id,
		Name = dataset.Name,
		SourceFile = dataset.SourceFile,
		TableName = dataset.TableName,
		RowCount = dataset.RowCount,
		ImportedAt = dataset.ImportedAt,
		Columns = dataset.Columns
			.Select(static column => new DatasetColumnRecord
			{
				Ordinal = column.Ordinal,
				PhysicalName = column.PhysicalName,
				Name = column.Name,
				Type = column.Type,
				DistinctCount = column.Profile.DistinctCount,
				Samples = [.. column.Profile.Samples],
			})
			.ToList(),
	};

	private static Dataset ToDomain(DatasetRecord record) => Dataset.Restore(
		record.Id,
		record.Name,
		record.SourceFile,
		record.Columns.OrderBy(static c => c.Ordinal).Select(static c => new DatasetColumn(c.Ordinal, c.PhysicalName, c.Name, c.Type, new ColumnProfile(c.DistinctCount, c.Samples))).ToArray(),
		record.RowCount,
		record.ImportedAt);
}
