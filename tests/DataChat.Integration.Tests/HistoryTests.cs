namespace DataChat.Integration.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DataChat.Application.Features.Questions;
using DataChat.Domain.Periods;
using DataChat.Domain.Semantics;
using DataChat.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>What was asked of a dataset, what it cost, and what people said about the answers.</summary>
[Collection(PostgresCollection.Name)]
public sealed class HistoryTests(PostgresFixture postgres) : IAsyncLifetime
{
	private Guid _english;

	public async Task InitializeAsync()
	{
		await using var services = postgres.Services(new Dictionary<string, string?> { ["Demo:SamplesDirectory"] = PostgresFixture.SamplesDirectory });
		await DemoDatasets.SeedAsync(services, Directory.GetCurrentDirectory());
		_english = await postgres.ScalarAsync<Guid>("SELECT id FROM datasets WHERE source_file = 'sales-demo.xlsx' ORDER BY imported_at LIMIT 1");
	}

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task A_question_shows_in_the_history_with_its_cost_and_the_feedback_left_on_it()
	{
		var id = await AskAsync("Which branch sold the most last quarter?");
		using var client = postgres.Api.CreateClient();

		using var feedback = await client.PostAsJsonAsync(new Uri($"/v1/questions/{id}/feedback", UriKind.Relative), new { feedback = "Wrong", note = "  I meant last month  " });
		var history = await client.GetFromJsonAsync<JsonElement>(new Uri($"/v1/datasets/{_english}/questions", UriKind.Relative));
		var item = history.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == id);

		Assert.Equal(HttpStatusCode.NoContent, feedback.StatusCode);
		Assert.Equal("Answered", item.GetProperty("outcome").GetString());
		Assert.Equal(0.0005m, item.GetProperty("costUsd").GetDecimal());
		Assert.Equal("Wrong", item.GetProperty("feedback").GetString());
		Assert.Equal("I meant last month", item.GetProperty("feedbackNote").GetString());
		Assert.True(history.GetProperty("spentTodayUsd").GetDecimal() >= 0.0005m);
		Assert.Equal(1.00m, history.GetProperty("dailyBudgetUsd").GetDecimal());
	}

	[Fact]
	public async Task Feedback_on_a_question_that_does_not_exist_is_a_404()
	{
		using var client = postgres.Api.CreateClient();

		using var response = await client.PostAsJsonAsync(new Uri($"/v1/questions/{Guid.NewGuid()}/feedback", UriKind.Relative), new { feedback = "Helpful" });

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task A_note_longer_than_the_limit_is_a_400()
	{
		var id = await AskAsync("Sales last quarter");
		using var client = postgres.Api.CreateClient();

		using var response = await client.PostAsJsonAsync(new Uri($"/v1/questions/{id}/feedback", UriKind.Relative), new { feedback = "Wrong", note = new string('x', QuestionHistory.MaxNoteLength + 1) });

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	private async Task<Guid> AskAsync(string question)
	{
		var translator = new Fixed(new Translation(
			"Sales by branch last quarter.",
			new SemanticQuery(["sales"], ["branch"], Period: new(NamedPeriod.LastQuarter)),
			null,
			new TranslationUsage("claude-haiku-5-5", 3_000, 400, 0.0005m)));
		await using var services = postgres.Services(replace: s => s.Replace(ServiceDescriptor.Singleton<IQuestionTranslator>(translator)));
		await using var scope = services.CreateAsyncScope();
		return (await scope.ServiceProvider.GetRequiredService<AskQuestion>().ExecuteAsync(_english, question, null, CancellationToken.None)).Value.QuestionId;
	}

	private sealed class Fixed(Translation translation) : IQuestionTranslator
	{
		public bool IsConfigured => true;

		public Task<Translation> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken) => Task.FromResult(translation);
	}
}
