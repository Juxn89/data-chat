namespace DataChat.Infrastructure.Querying;

using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using DataChat.Application.Common;
using DataChat.Application.Features.Questions;
using DataChat.Domain.Datasets;
using DataChat.Domain.Semantics;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using NodaTime;

/// <summary>How questions run.</summary>
public sealed class QueryOptions
{
	/// <summary>The configuration section.</summary>
	public const string SectionName = "Query";

	/// <summary>The longest a question may run (plan §3: 3 s).</summary>
	[Range(100, 60_000)]
	public int StatementTimeoutMs { get; set; } = 3_000;
}

/// <summary>
/// Runs compiled questions as <see cref="ReaderRole"/>: a role that can only <c>SELECT</c> the dataset tables, in a
/// read-only transaction with a statement timeout. Even a query the compiler should never have produced could not
/// write, read the app's own tables, or run for long (ADR 0001, layer 4).
/// </summary>
/// <param name="source">The app's data source; each question switches to the reader role for its own transaction.</param>
/// <param name="options">The options.</param>
internal sealed class QueryRunner(NpgsqlDataSource source, IOptions<QueryOptions> options) : IQueryRunner
{
	/// <summary>The role questions run as; created by the <c>ReaderRole</c> migration, granted each dataset table at import.</summary>
	public const string ReaderRole = "datachat_reader";

	/// <summary>Postgres' code for a statement cancelled by its timeout.</summary>
	private const string QueryCanceled = "57014";

	public async Task<Result<QueryAnswer>> RunAsync(Dataset dataset, SemanticLayer layer, SemanticQuery query, LocalDate today, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(dataset);
		ArgumentNullException.ThrowIfNull(layer);
		ArgumentNullException.ThrowIfNull(query);
		if (query.FilterList.FirstOrDefault(filter => dataset.Column(layer.Dimension(filter.Dimension)!.Column)!.Type == ColumnType.Integer
			&& !filter.Values.All(SqlCompiler.IsWholeNumber)) is { } bad)
		{
			return Error.Validation("query.invalid_filter", $"'{bad.Dimension}' holds whole numbers; a filter on it needs whole numbers.");
		}

		var compiled = SqlCompiler.Compile(dataset, layer, query, today);
		try
		{
			var rows = await ReadAsync(compiled.Sql, compiled.Parameters, cancellationToken).ConfigureAwait(false);
			return new QueryAnswer(
				compiled.Columns,
				rows,
				compiled.Sql,
				compiled.Parameters.Select(static p => new ShownParameter($"@{p.Name}", Show(p.Value))).ToArray(),
				compiled.Period,
				compiled.ComparedWith,
				Truncated: rows.Count == compiled.Limit);
		}
		catch (PostgresException exception) when (exception.SqlState == QueryCanceled)
		{
			return RunQuery.TimedOut;
		}
	}

	/// <summary>Runs SQL the way every question runs. The integration tests use it to prove what the role cannot do.</summary>
	/// <param name="sql">The SQL.</param>
	/// <param name="parameters">Its parameters.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The rows.</returns>
	internal async Task<IReadOnlyList<IReadOnlyList<object?>>> ReadAsync(string sql, IReadOnlyList<SqlParameter> parameters, CancellationToken cancellationToken)
	{
		await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
		await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
		var timeout = options.Value.StatementTimeoutMs.ToString(CultureInfo.InvariantCulture);
		await using (var setup = new NpgsqlCommand($"SET TRANSACTION READ ONLY; SET LOCAL ROLE {ReaderRole}; SET LOCAL statement_timeout = {timeout}", connection, transaction))
		{
			await setup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
		}

		await using var command = new NpgsqlCommand(sql, connection, transaction);
		foreach (var parameter in parameters)
		{
			command.Parameters.Add(new NpgsqlParameter(parameter.Name, Type(parameter.Type)) { Value = parameter.Value });
		}

		var rows = new List<IReadOnlyList<object?>>();
		await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
		{
			while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
			{
				var row = new object?[reader.FieldCount];
				for (var i = 0; i < reader.FieldCount; i++)
				{
					row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
				}

				rows.Add(row);
			}
		}

		await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
		return rows;
	}

	private static NpgsqlDbType Type(ParameterType type) => type switch
	{
		ParameterType.Date => NpgsqlDbType.Date,
		ParameterType.BigintArray => NpgsqlDbType.Array | NpgsqlDbType.Bigint,
		_ => NpgsqlDbType.Array | NpgsqlDbType.Text,
	};

	private static string Show(object value) => value switch
	{
		DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
		string[] texts => $"[{string.Join(", ", texts.Select(static t => $"'{t}'"))}]",
		long[] numbers => $"[{string.Join(", ", numbers.Select(static n => n.ToString(CultureInfo.InvariantCulture)))}]",
		_ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
	};
}
