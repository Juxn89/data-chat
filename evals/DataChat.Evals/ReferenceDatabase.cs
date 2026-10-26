namespace DataChat.Evals;

using System.Text.Json.Nodes;
using DataChat.SampleData;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

/// <summary>
/// A throwaway Postgres with the sample loaded into one reference table per language, straight from the generator:
/// <c>sales_es</c> and <c>sales_en</c>, with plain English column names. The reference SQL is written against these,
/// independently of how the app names an imported dataset's columns.
/// </summary>
internal sealed class ReferenceDatabase : IAsyncDisposable
{
	/// <summary>The same image as the AppHost and the integration tests.</summary>
	private const string Image = "postgres:18.6-alpine";

	private const string TablePlaceholder = "{sales}";

	private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();

	private NpgsqlDataSource? _source;

	/// <summary>The database's connection string, for the app's own tables next to the reference ones.</summary>
	public string ConnectionString => _container.GetConnectionString();

	public static async Task<ReferenceDatabase> StartAsync()
	{
		var database = new ReferenceDatabase();
		await database._container.StartAsync();
		database._source = NpgsqlDataSource.Create(database._container.GetConnectionString());
		var lines = SalesGenerator.Generate();
		foreach (var language in Enum.GetValues<SampleLanguage>())
		{
			await database.LoadAsync(lines, language);
		}

		return database;
	}

	/// <summary>Runs a question's reference SQL against the table in its language.</summary>
	/// <param name="question">The question.</param>
	/// <returns>The answer.</returns>
	public async Task<Answer> RunAsync(Question question)
	{
		var sql = question.Sql.Replace(TablePlaceholder, Table(question.Language), StringComparison.Ordinal);
		await using var command = Source.CreateCommand(sql);
		await using var reader = await command.ExecuteReaderAsync();
		var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
		var rows = new List<IReadOnlyList<JsonNode?>>();
		while (await reader.ReadAsync())
		{
			rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => Answer.Cell(reader.IsDBNull(i) ? null : reader.GetValue(i))).ToArray());
		}

		return new Answer(columns, rows);
	}

	public async ValueTask DisposeAsync()
	{
		if (_source is not null)
		{
			await _source.DisposeAsync();
		}

		await _container.DisposeAsync();
	}

	private NpgsqlDataSource Source => _source ?? throw new InvalidOperationException("Not started.");

	private static string Table(string language) => language switch
	{
		"es" => "sales_es",
		"en" => "sales_en",
		_ => throw new InvalidOperationException($"Unknown language '{language}'."),
	};

	private async Task LoadAsync(IReadOnlyList<SaleLine> lines, SampleLanguage language)
	{
		var table = Table(language == SampleLanguage.Es ? "es" : "en");
		await using (var create = Source.CreateCommand(
			$"CREATE TABLE {table} (date date NOT NULL, invoice text NOT NULL, branch text NOT NULL, category text NOT NULL, product text NOT NULL, units integer NOT NULL, unit_price numeric(10,2) NOT NULL, total numeric(12,2) NOT NULL)"))
		{
			await create.ExecuteNonQueryAsync();
		}

		await using var connection = await Source.OpenConnectionAsync();
		await using (var copy = await connection.BeginBinaryImportAsync(
			$"COPY {table} (date, invoice, branch, category, product, units, unit_price, total) FROM STDIN (FORMAT BINARY)"))
		{
			foreach (var line in lines)
			{
				await copy.StartRowAsync();
				await copy.WriteAsync(line.Date, NpgsqlDbType.Date);
				await copy.WriteAsync(line.Invoice, NpgsqlDbType.Text);
				await copy.WriteAsync(line.Branch, NpgsqlDbType.Text);
				await copy.WriteAsync(line.Category.In(language), NpgsqlDbType.Text);
				await copy.WriteAsync(line.Product.In(language), NpgsqlDbType.Text);
				await copy.WriteAsync(line.Units, NpgsqlDbType.Integer);
				await copy.WriteAsync(line.UnitPrice, NpgsqlDbType.Numeric);
				await copy.WriteAsync(line.Total, NpgsqlDbType.Numeric);
			}

			await copy.CompleteAsync();
		}
	}
}
