namespace DataChat.Evals;

using System.Text.Json;
using DataChat.Application.Features.Questions;
using DataChat.Infrastructure;
using DataChat.Infrastructure.Ai;
using DataChat.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

/// <summary>What happened to one question in the app.</summary>
/// <param name="Answer">The rows, when it was answered.</param>
/// <param name="Query">The semantic query the translator gave, as JSON.</param>
/// <param name="Interpretation">How the question was read.</param>
/// <param name="Clarification">The question back, when there was one.</param>
/// <param name="Error">The error's code, when it failed.</param>
/// <param name="CostUsd">What the model cost, retries included.</param>
/// <param name="Calls">Calls to the model.</param>
/// <param name="InputTokens">Input tokens.</param>
/// <param name="OutputTokens">Output tokens.</param>
/// <param name="LatencyMs">How long it took.</param>
internal sealed record AppResult(
	Answer? Answer,
	string? Query,
	string Interpretation,
	string? Clarification,
	string? Error,
	decimal CostUsd,
	int Calls,
	long InputTokens,
	long OutputTokens,
	long LatencyMs);

/// <summary>
/// The app itself on the evals' database: migrated, with the sample imported and its layers confirmed exactly as at
/// startup, so a question goes through the real import, layer, translator, compiler and read-only role.
/// </summary>
internal sealed class AppUnderTest : IAsyncDisposable
{
	private readonly ServiceProvider _services;

	private readonly NpgsqlDataSource _source;

	private readonly Dictionary<string, Guid> _datasets;

	private AppUnderTest(ServiceProvider services, NpgsqlDataSource source, Dictionary<string, Guid> datasets)
	{
		_services = services;
		_source = source;
		_datasets = datasets;
	}

	/// <summary>Starts the app.</summary>
	/// <param name="connectionString">The evals' database.</param>
	/// <param name="settings">Settings over the defaults (the API key, for a paid run).</param>
	/// <param name="translator">A translator to use instead of the configured one (the gold one, for the oracle).</param>
	/// <returns>The app.</returns>
	public static async Task<AppUnderTest> StartAsync(string connectionString, IReadOnlyDictionary<string, string?>? settings = null, IQuestionTranslator? translator = null)
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[$"ConnectionStrings:{DependencyInjection.DatabaseConnectionName}"] = connectionString,
				["Demo:SamplesDirectory"] = Path.GetFullPath("samples"),
			})
			.AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
			.Build();
		var collection = new ServiceCollection().AddLogging().AddInfrastructure(configuration);
		if (translator is not null)
		{
			collection.Replace(ServiceDescriptor.Singleton(translator));
		}

		var services = collection.BuildServiceProvider();
		await DatabaseMigrator.MigrateAsync(services);
		await DemoDatasets.SeedAsync(services, Directory.GetCurrentDirectory());

		var source = NpgsqlDataSource.Create(connectionString);
		await using var command = source.CreateCommand("SELECT source_file, id FROM datasets");
		await using var reader = await command.ExecuteReaderAsync();
		var datasets = new Dictionary<string, Guid>(StringComparer.Ordinal);
		while (await reader.ReadAsync())
		{
			datasets[reader.GetString(0)] = reader.GetGuid(1);
		}

		return new AppUnderTest(services, source, datasets);
	}

	/// <summary>Asks a question on the sample in its language, as the chat would.</summary>
	/// <param name="question">The question.</param>
	/// <returns>What happened, cost included even when it failed.</returns>
	public async Task<AppResult> AskAsync(Question question)
	{
		var dataset = _datasets[question.Language == "es" ? "ventas-demo.xlsx" : "sales-demo.xlsx"];
		await using var scope = _services.CreateAsyncScope();
		var result = await scope.ServiceProvider.GetRequiredService<AskQuestion>().ExecuteAsync(dataset, question.Text, CancellationToken.None);

		// The question log has the cost whatever the outcome: a failed question was paid for too.
		await using var command = _source.CreateCommand(
			"SELECT interpretation, semantic_query::text, clarification, cost_usd, model_calls, input_tokens, output_tokens, latency_ms FROM questions ORDER BY created_at DESC LIMIT 1");
		await using var reader = await command.ExecuteReaderAsync();
		await reader.ReadAsync();
		return new AppResult(
			result.IsSuccess && result.Value.Answer is { } answer ? Answer.From(answer.Columns.Select(static c => c.Key).ToArray(), answer.Rows) : null,
			reader.IsDBNull(1) ? null : reader.GetString(1),
			reader.GetString(0),
			reader.IsDBNull(2) ? null : reader.GetString(2),
			result.IsSuccess ? null : result.Error!.Code,
			reader.GetDecimal(3),
			reader.GetInt32(4),
			reader.GetInt64(5),
			reader.GetInt64(6),
			reader.GetInt64(7));
	}

	public async ValueTask DisposeAsync()
	{
		await _source.DisposeAsync();
		await _services.DisposeAsync();
	}
}

/// <summary>
/// The translation a perfect model gives: each question's gold query. The oracle runs the whole chat path with it,
/// in CI, without a key and at no cost.
/// </summary>
/// <param name="questions">The questions, by text.</param>
internal sealed class GoldTranslator(IEnumerable<Question> questions) : IQuestionTranslator
{
	private readonly Dictionary<string, Question> _byText = questions.ToDictionary(static q => q.Text, StringComparer.Ordinal);

	public bool IsConfigured => true;

	public Task<Translation> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken) =>
		Task.FromResult(new Translation("gold", _byText[request.Question].GoldQuery(), null, TranslationUsage.None));
}

/// <summary>Compares a model's query with the gold one, as JSON with the same conventions.</summary>
internal static class Queries
{
	/// <summary>Whether two queries say the same thing.</summary>
	/// <param name="json">The model's query, as stored.</param>
	/// <param name="gold">The gold query.</param>
	/// <returns>Whether they are equal.</returns>
	public static bool Same(string? json, Question gold)
	{
		if (json is null)
		{
			return false;
		}

		var model = JsonSerializer.Deserialize<Domain.Semantics.SemanticQuery>(json, QuestionSchema.Json)!;
		return JsonSerializer.Serialize(Normal(model), QuestionSchema.Json) == JsonSerializer.Serialize(Normal(gold.GoldQuery()), QuestionSchema.Json);
	}

	// No filters is no filters, whether written as null or as an empty list.
	private static Domain.Semantics.SemanticQuery Normal(Domain.Semantics.SemanticQuery query) =>
		query with { Filters = query.FilterList.Count == 0 ? null : query.Filters };

}
