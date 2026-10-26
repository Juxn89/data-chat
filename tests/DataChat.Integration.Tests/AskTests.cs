namespace DataChat.Integration.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DataChat.Application.Features.Questions;
using DataChat.Domain.Periods;
using DataChat.Domain.Semantics;
using DataChat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>A question from its text to its rows, with a translator that plays back what it is told.</summary>
[Collection(PostgresCollection.Name)]
public sealed class AskTests(PostgresFixture postgres) : IAsyncLifetime
{
	private static readonly TranslationUsage OneCall = new("claude-haiku-5-5", 3_000, 400, 0.0005m);

	private static readonly SemanticQuery TopBranch = new(["ventas"], ["sucursal"], Period: new(NamedPeriod.LastQuarter), Sort: new("ventas", SortDirection.Descending), Limit: 1);

	private Guid _spanish;

	public async Task InitializeAsync()
	{
		await using var services = postgres.Services(new Dictionary<string, string?> { ["Demo:SamplesDirectory"] = PostgresFixture.SamplesDirectory });
		await DemoDatasets.SeedAsync(services, Directory.GetCurrentDirectory());
		_spanish = await postgres.ScalarAsync<Guid>("SELECT id FROM datasets WHERE source_file = 'ventas-demo.xlsx' ORDER BY imported_at LIMIT 1");
	}

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task A_question_is_read_run_and_recorded_with_its_cost()
	{
		var translator = new PlayBack(new Translation("Sucursal con más ventas el último trimestre.", TopBranch, null, OneCall));

		var answer = (await AskAsync(translator, "¿Qué sucursal vendió más el último trimestre?")).Value;

		Assert.Equal("Centro", answer.Answer!.Rows[0][0]);
		Assert.Equal(0.0005m, await postgres.ScalarAsync<decimal>($"SELECT cost_usd FROM questions WHERE id = '{answer.QuestionId}'"));
		Assert.Equal("Answered", await postgres.ScalarAsync<string>($"SELECT outcome FROM questions WHERE id = '{answer.QuestionId}'"));
	}

	[Fact]
	public async Task An_invalid_query_gets_one_retry_with_the_reasons()
	{
		var translator = new PlayBack(
			new Translation("…", new SemanticQuery(["ventas"], ["sucursal", "categoria"], TimeGrain.Month), null, OneCall),
			new Translation("Sucursal con más ventas.", TopBranch, null, OneCall));

		var answer = (await AskAsync(translator, "Ventas por sucursal y categoría por mes")).Value;

		Assert.Equal(2, translator.Requests.Count);
		Assert.Contains(translator.Requests[1].Feedback, static issue => issue.Code == "query.too_many_groups");
		Assert.Equal(0.0010m, answer.Usage.CostUsd);
		Assert.NotNull(answer.Answer);
	}

	[Fact]
	public async Task A_query_still_invalid_after_the_retry_is_not_run()
	{
		var wrong = new Translation("…", new SemanticQuery(["margen"], []), null, OneCall);

		var result = await AskAsync(new PlayBack(wrong, wrong), "¿Cuál es el margen?");

		Assert.Equal("question.untranslatable", result.Error?.Code);
	}

	[Fact]
	public async Task A_question_back_is_returned_and_recorded()
	{
		var translator = new PlayBack(new Translation("No hay datos de margen.", null, "Este archivo no tiene costos. ¿Te sirven las ventas o las unidades?", OneCall));

		var answer = (await AskAsync(translator, "¿Cuál fue el margen?")).Value;

		Assert.Null(answer.Answer);
		Assert.StartsWith("Este archivo", answer.Clarification, StringComparison.Ordinal);
		Assert.Equal("Clarified", await postgres.ScalarAsync<string>($"SELECT outcome FROM questions WHERE id = '{answer.QuestionId}'"));
	}

	[Fact]
	public async Task Past_the_daily_budget_the_model_is_not_called()
	{
		var translator = new PlayBack(new Translation("…", TopBranch, null, OneCall));

		var result = await AskAsync(translator, "¿Qué sucursal vendió más?", new Dictionary<string, string?> { ["Ask:DailyBudgetUsd"] = "0.01" }, alreadySpent: 0.02m);

		Assert.Equal("ai.daily_budget", result.Error?.Code);
		Assert.Empty(translator.Requests);
	}

	[Fact]
	public async Task Without_a_key_the_api_says_questions_are_unavailable()
	{
		using var client = postgres.Api.WithWebHostBuilder(static host => host.UseSetting("Anthropic:ApiKey", string.Empty)).CreateClient();

		using var response = await client.PostAsJsonAsync(new Uri($"/v1/datasets/{_spanish}/questions", UriKind.Relative), new { text = "¿Cuánto vendimos?" });

		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
		Assert.Equal("ai.not_configured", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
	}

	[Fact]
	public async Task The_scripted_translator_answers_from_its_script()
	{
		var script = Path.Combine(Path.GetTempPath(), $"script-{Guid.NewGuid():N}.json");
		await File.WriteAllTextAsync(script, """
			[{"question":"¿Qué sucursal vendió más el último trimestre?","interpretation":"La sucursal con más ventas.","clarification":null,
			  "query":{"metrics":["ventas"],"dimensions":["sucursal"],"period":{"named":"LastQuarter"},"sort":{"by":"ventas","direction":"Descending"},"limit":1}}]
			""");
		try
		{
			using var client = postgres.Api.WithWebHostBuilder(host => host.UseSetting("TranslatorScript:File", script)).CreateClient();

			using var response = await client.PostAsJsonAsync(new Uri($"/v1/datasets/{_spanish}/questions", UriKind.Relative), new { text = " ¿qué sucursal vendió más el último trimestre? " });
			var answer = await response.Content.ReadFromJsonAsync<JsonElement>();

			Assert.Equal(HttpStatusCode.OK, response.StatusCode);
			Assert.Equal("Centro", answer.GetProperty("answer").GetProperty("rows")[0][0].GetString());
		}
		finally
		{
			File.Delete(script);
		}
	}

	private async Task<Application.Common.Result<AskAnswer>> AskAsync(
		PlayBack translator,
		string question,
		IReadOnlyDictionary<string, string?>? settings = null,
		decimal alreadySpent = 0m)
	{
		await using var services = postgres.Services(settings, s => s.Replace(ServiceDescriptor.Singleton<IQuestionTranslator>(translator)));
		await using var scope = services.CreateAsyncScope();
		if (alreadySpent > 0)
		{
			await scope.ServiceProvider.GetRequiredService<IQuestionLog>().AddAsync(
				new QuestionRecord(_spanish, "earlier", string.Empty, null, null, QuestionOutcome.Answered, null, 0, OneCall with { CostUsd = alreadySpent }, 0),
				CancellationToken.None);
		}

		return await scope.ServiceProvider.GetRequiredService<AskQuestion>().ExecuteAsync(_spanish, question, CancellationToken.None);
	}

	/// <summary>Answers with the given translations in order, and keeps what it was asked.</summary>
	private sealed class PlayBack(params Translation[] translations) : IQuestionTranslator
	{
		private int _next;

		public List<TranslationRequest> Requests { get; } = [];

		public bool IsConfigured => true;

		public Task<Translation> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
		{
			Requests.Add(request);
			return Task.FromResult(translations[Math.Min(_next++, translations.Length - 1)]);
		}
	}
}
