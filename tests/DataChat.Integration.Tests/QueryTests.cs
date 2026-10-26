namespace DataChat.Integration.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DataChat.Application.Features.Questions;
using DataChat.Infrastructure.Persistence;
using DataChat.Infrastructure.Querying;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

/// <summary>Questions run through the compiler as a role that can only read the dataset tables (ADR 0001, layer 4).</summary>
[Collection(PostgresCollection.Name)]
public sealed class QueryTests(PostgresFixture postgres) : IAsyncLifetime
{
	private Guid _spanish;

	private string _table = string.Empty;

	public async Task InitializeAsync()
	{
		await using var services = postgres.Services(new Dictionary<string, string?> { ["Demo:SamplesDirectory"] = PostgresFixture.SamplesDirectory });
		await DemoDatasets.SeedAsync(services, Directory.GetCurrentDirectory());
		_spanish = await postgres.ScalarAsync<Guid>("SELECT id FROM datasets WHERE source_file = 'ventas-demo.xlsx' ORDER BY imported_at LIMIT 1");
		_table = await postgres.ScalarAsync<string>($"SELECT table_name FROM datasets WHERE id = '{_spanish}'");
	}

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task A_question_is_answered_with_its_sql_and_parameters()
	{
		using var client = postgres.Api.CreateClient();

		using var response = await client.PostAsJsonAsync(Query(_spanish), new
		{
			metrics = new[] { "ventas" },
			dimensions = new[] { "sucursal" },
			period = new { named = "LastQuarter" },
			sort = new { by = "ventas", direction = "Descending" },
			limit = 1,
		});
		var answer = await response.Content.ReadFromJsonAsync<JsonElement>();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("Centro", answer.GetProperty("rows")[0][0].GetString());
		Assert.Equal(12_078.19m, answer.GetProperty("rows")[0][1].GetDecimal());
		Assert.Contains("@p1", answer.GetProperty("sql").GetString(), StringComparison.Ordinal);
		Assert.Equal("2026-07-01", answer.GetProperty("parameters")[0].GetProperty("value").GetString());
		Assert.Equal("2026-09-30", answer.GetProperty("period").GetProperty("end").GetString());
	}

	[Fact]
	public async Task A_filter_value_full_of_sql_is_just_a_value_that_matches_nothing()
	{
		using var client = postgres.Api.CreateClient();

		using var response = await client.PostAsJsonAsync(Query(_spanish), new
		{
			metrics = new[] { "ventas" },
			dimensions = Array.Empty<string>(),
			filters = new[] { new { dimension = "sucursal", @operator = "In", values = new[] { "Centro'); DROP TABLE datasets; --" } } },
		});
		var answer = await response.Content.ReadFromJsonAsync<JsonElement>();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal(JsonValueKind.Null, answer.GetProperty("rows")[0][0].ValueKind);
		Assert.True(await postgres.ScalarAsync<long>("SELECT count(*) FROM datasets") > 0);
	}

	[Fact]
	public async Task An_invalid_query_is_a_400_with_its_issues()
	{
		using var client = postgres.Api.CreateClient();

		using var response = await client.PostAsJsonAsync(Query(_spanish), new { metrics = new[] { "margen" }, dimensions = Array.Empty<string>() });
		var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Equal("query.invalid", problem.GetProperty("code").GetString());
		Assert.StartsWith("query.unknown_metric", problem.GetProperty("issue1").GetString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task A_dataset_whose_layer_is_not_confirmed_cannot_be_asked_yet()
	{
		using var client = postgres.Api.CreateClient();
		var file = new ByteArrayContent(Encoding.UTF8.GetBytes("Sucursal,Total\nCentro,1\nAltamira,2\n"));
		file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
		using var form = new MultipartFormDataContent { { file, "file", "pendiente.csv" } };
		using var created = await client.PostAsync(new Uri("/v1/datasets", UriKind.Relative), form);
		var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

		using var response = await client.PostAsJsonAsync(Query(id), new { metrics = new[] { "total" }, dimensions = Array.Empty<string>() });

		Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
		Assert.Equal("semantic.not_confirmed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
	}

	[Theory]
	[InlineData("INSERT INTO {table} (c_002) VALUES ('x')")]
	[InlineData("DELETE FROM {table}")]
	[InlineData("DROP TABLE {table}")]
	[InlineData("SELECT count(*) FROM datasets")]
	[InlineData("SELECT count(*) FROM semantic_layers")]
	[InlineData("CREATE TABLE intrusa (x int)")]
	public async Task The_reader_role_cannot_write_or_read_anything_but_datasets(string sql)
	{
		var runner = Runner();

		var exception = await Assert.ThrowsAsync<PostgresException>(() => runner.ReadAsync(sql.Replace("{table}", _table, StringComparison.Ordinal), [], CancellationToken.None));

		// 25006: read-only transaction; 42501: insufficient privilege.
		Assert.Contains(exception.SqlState, new[] { "25006", "42501" });
	}

	[Fact]
	public async Task The_reader_role_reads_dataset_tables() =>
		Assert.Equal(37_636L, (await Runner().ReadAsync($"SELECT count(*) FROM {_table}", [], CancellationToken.None))[0][0]);

	[Fact]
	public async Task A_slow_query_is_cut_by_the_statement_timeout()
	{
		var runner = Runner(new Dictionary<string, string?> { ["Query:StatementTimeoutMs"] = "200" });

		var exception = await Assert.ThrowsAsync<PostgresException>(() => runner.ReadAsync("SELECT pg_sleep(5)", [], CancellationToken.None));

		Assert.Equal("57014", exception.SqlState);
	}

	private QueryRunner Runner(IReadOnlyDictionary<string, string?>? settings = null) =>
		(QueryRunner)postgres.Services(settings).CreateScope().ServiceProvider.GetRequiredService<IQueryRunner>();

	private static Uri Query(Guid id) => new($"/v1/datasets/{id}/query", UriKind.Relative);
}
