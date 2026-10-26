namespace DataChat.Evals;

using DataChat.Application.Features.Questions;
using DataChat.Infrastructure;
using DataChat.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

/// <summary>
/// The app itself on the evals' database: migrated, with the sample imported and its layers confirmed exactly as at
/// startup, so a question goes through the real import, layer, compiler and read-only role.
/// </summary>
internal sealed class AppUnderTest : IAsyncDisposable
{
	private readonly ServiceProvider _services;

	private readonly Dictionary<string, Guid> _datasets;

	private AppUnderTest(ServiceProvider services, Dictionary<string, Guid> datasets)
	{
		_services = services;
		_datasets = datasets;
	}

	public static async Task<AppUnderTest> StartAsync(string connectionString)
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[$"ConnectionStrings:{DependencyInjection.DatabaseConnectionName}"] = connectionString,
				["Demo:SamplesDirectory"] = Path.GetFullPath("samples"),
			})
			.Build();
		var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
		await DatabaseMigrator.MigrateAsync(services);
		await DemoDatasets.SeedAsync(services, Directory.GetCurrentDirectory());

		await using var source = NpgsqlDataSource.Create(connectionString);
		await using var command = source.CreateCommand("SELECT source_file, id FROM datasets");
		await using var reader = await command.ExecuteReaderAsync();
		var datasets = new Dictionary<string, Guid>(StringComparer.Ordinal);
		while (await reader.ReadAsync())
		{
			datasets[reader.GetString(0)] = reader.GetGuid(1);
		}

		return new AppUnderTest(services, datasets);
	}

	/// <summary>Runs a question's gold query on the sample in its language.</summary>
	/// <param name="question">The question.</param>
	/// <returns>The answer, or the error's code.</returns>
	public async Task<(Answer? Answer, string? Error)> RunGoldAsync(Question question)
	{
		var dataset = _datasets[question.Language == "es" ? "ventas-demo.xlsx" : "sales-demo.xlsx"];
		await using var scope = _services.CreateAsyncScope();
		var result = await scope.ServiceProvider.GetRequiredService<RunQuery>().ExecuteAsync(dataset, question.GoldQuery(), CancellationToken.None);
		return result.IsSuccess
			? (Answer.From(result.Value.Columns.Select(static column => column.Key).ToArray(), result.Value.Rows), null)
			: (null, result.Error!.Code);
	}

	public ValueTask DisposeAsync() => _services.DisposeAsync();
}
