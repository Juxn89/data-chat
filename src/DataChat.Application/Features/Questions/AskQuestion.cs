namespace DataChat.Application.Features.Questions;

using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using DataChat.Application.Common;
using DataChat.Application.Features.Datasets;
using DataChat.Application.Features.Semantics;
using DataChat.Domain.Datasets;
using DataChat.Domain.Semantics;
using Microsoft.Extensions.Options;
using NodaTime;

/// <summary>The question before this one in a conversation, and how it was read: what "¿y en abril?" refers to.</summary>
/// <param name="Text">What was asked.</param>
/// <param name="Query">How it was read, when it was read into a query.</param>
public sealed record PreviousQuestion(string Text, SemanticQuery? Query);

/// <summary>What the model was asked: the question and everything it may know about the dataset.</summary>
/// <param name="Question">The question, as the person wrote it.</param>
/// <param name="Dataset">The dataset.</param>
/// <param name="Layer">Its confirmed layer: the only names and keys the model sees.</param>
/// <param name="Today">The "today" named periods resolve against.</param>
/// <param name="Feedback">On a retry: why the previous query was rejected.</param>
/// <param name="Previous">The question before, in a conversation.</param>
public sealed record TranslationRequest(string Question, Dataset Dataset, SemanticLayer Layer, LocalDate Today, IReadOnlyList<SemanticIssue> Feedback, PreviousQuestion? Previous = null);

/// <summary>What a call to the model cost.</summary>
/// <param name="Model">The model.</param>
/// <param name="InputTokens">Input tokens.</param>
/// <param name="OutputTokens">Output tokens, thinking included.</param>
/// <param name="CostUsd">USD.</param>
/// <param name="Calls">How many calls to the model.</param>
public sealed record TranslationUsage(string Model, long InputTokens, long OutputTokens, decimal CostUsd, int Calls = 1)
{
	/// <summary>Nothing spent, no call made.</summary>
	public static TranslationUsage None { get; } = new(string.Empty, 0, 0, 0m, 0);

	/// <summary>Two calls' worth.</summary>
	/// <param name="other">The other call.</param>
	/// <returns>The sum.</returns>
	public TranslationUsage Plus(TranslationUsage other)
	{
		ArgumentNullException.ThrowIfNull(other);
		return new(other.Model.Length > 0 ? other.Model : Model, InputTokens + other.InputTokens, OutputTokens + other.OutputTokens, CostUsd + other.CostUsd, Calls + other.Calls);
	}
}

/// <summary>
/// The model's reading of a question: a semantic query, or a question back when guessing would be worse than asking
/// (plan §5). Never SQL.
/// </summary>
/// <param name="Interpretation">One sentence, in the question's language, of how it was read.</param>
/// <param name="Query">The query, when the question can be answered.</param>
/// <param name="Clarification">The question back, when it cannot be answered as asked.</param>
/// <param name="Usage">What it cost.</param>
/// <param name="Failure">Why there is neither: a refusal, a cut-off or malformed answer.</param>
public sealed record Translation(string Interpretation, SemanticQuery? Query, string? Clarification, TranslationUsage Usage, string? Failure = null);

/// <summary>Turns a question into a semantic query.</summary>
public interface IQuestionTranslator
{
	/// <summary>Whether it can translate at all (a model needs a key).</summary>
	bool IsConfigured { get; }

	/// <summary>Translates a question.</summary>
	/// <param name="request">The question and its context.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The translation.</returns>
	Task<Translation> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
}

/// <summary>How a question ended.</summary>
public enum QuestionOutcome
{
	/// <summary>It was answered with rows.</summary>
	Answered,

	/// <summary>The model asked back.</summary>
	Clarified,

	/// <summary>No answer: the model failed, or its query was invalid twice, or the query failed.</summary>
	Failed,
}

/// <summary>A question as it is recorded: what was asked, how it was read and what it cost.</summary>
/// <param name="DatasetId">The dataset.</param>
/// <param name="ConversationId">The conversation it belongs to.</param>
/// <param name="Text">The question.</param>
/// <param name="Interpretation">How it was read.</param>
/// <param name="Query">The query, when there was one.</param>
/// <param name="Clarification">The question back, when there was one.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="ErrorCode">When it failed, why.</param>
/// <param name="RowCount">The answer's rows.</param>
/// <param name="Usage">What the model cost, narration included.</param>
/// <param name="LatencyMs">How long it took, model and query.</param>
/// <param name="Narrative">The summary the model wrote; shown only when it passed the check.</param>
/// <param name="NarrativeDiscarded">The summary quoted a number that is not in the answer, so it was not shown.</param>
public sealed record QuestionRecord(
	Guid DatasetId,
	Guid ConversationId,
	string Text,
	string Interpretation,
	SemanticQuery? Query,
	string? Clarification,
	QuestionOutcome Outcome,
	string? ErrorCode,
	int RowCount,
	TranslationUsage Usage,
	long LatencyMs,
	string? Narrative = null,
	bool NarrativeDiscarded = false);

/// <summary>Where questions are recorded; their costs are what the daily budget adds up.</summary>
public interface IQuestionLog
{
	/// <summary>Records a question.</summary>
	/// <param name="question">The question.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>Its id.</returns>
	Task<Guid> AddAsync(QuestionRecord question, CancellationToken cancellationToken);

	/// <summary>What the model has cost since an instant.</summary>
	/// <param name="since">The instant.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>USD.</returns>
	Task<decimal> SpentSinceAsync(Instant since, CancellationToken cancellationToken);

	/// <summary>The last question of a conversation on a dataset.</summary>
	/// <param name="conversationId">The conversation.</param>
	/// <param name="datasetId">The dataset it must be about.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The question, or null when there is no such conversation on that dataset.</returns>
	Task<PreviousQuestion?> LastInConversationAsync(Guid conversationId, Guid datasetId, CancellationToken cancellationToken);
}

/// <summary>The question's limits and the model's daily budget.</summary>
public sealed class AskOptions
{
	/// <summary>The configuration section.</summary>
	public const string SectionName = "Ask";

	/// <summary>The longest question: a question, not a document.</summary>
	[Range(10, 5000)]
	public int MaxQuestionLength { get; set; } = 500;

	/// <summary>What all questions together may spend per UTC day; past it, questions wait until tomorrow.</summary>
	[Range(0.01, 1000)]
	public decimal DailyBudgetUsd { get; set; } = 1.00m;

	/// <summary>A question that already cost this much gets no retry and no summary (plan §5: about USD 0.001 per question).</summary>
	[Range(0.001, 10)]
	public decimal QuestionBudgetUsd { get; set; } = 0.01m;
}

/// <summary>A question's answer: how it was read, and the rows or the question back.</summary>
/// <param name="QuestionId">The recorded question's id.</param>
/// <param name="ConversationId">The conversation, to send with a follow-up.</param>
/// <param name="Interpretation">How it was read.</param>
/// <param name="Query">The semantic query, when there was one.</param>
/// <param name="Clarification">The question back, when there was one.</param>
/// <param name="Answer">The rows and their SQL, when it was answered.</param>
/// <param name="Narrative">A sentence or two about the rows, every figure in it checked against them.</param>
/// <param name="Usage">What the model cost.</param>
public sealed record AskAnswer(
	Guid QuestionId,
	Guid ConversationId,
	string Interpretation,
	SemanticQuery? Query,
	string? Clarification,
	QueryAnswer? Answer,
	string? Narrative,
	TranslationUsage Usage);

/// <summary>
/// Answers a question in plain language (plan §3, steps 1-7): the model reads it, in the light of the question before
/// it in the same conversation, into a semantic query against the confirmed layer; an invalid query gets one retry
/// with the reasons; the query runs read-only; the model then summarizes the rows, and the summary is shown only if
/// every number in it is one of the answer's figures. Every question is recorded with its cost, and the day's total
/// caps what the model may spend.
/// </summary>
/// <param name="datasets">The datasets.</param>
/// <param name="layers">The confirmed layers.</param>
/// <param name="translator">The model reading questions.</param>
/// <param name="narrator">The model summarizing answers.</param>
/// <param name="runner">The query runner.</param>
/// <param name="log">The question log.</param>
/// <param name="options">The limits.</param>
/// <param name="clock">The clock.</param>
public sealed class AskQuestion(
	IDatasetStore datasets,
	ISemanticLayerStore layers,
	IQuestionTranslator translator,
	INarrator narrator,
	IQueryRunner runner,
	IQuestionLog log,
	IOptions<AskOptions> options,
	IClock clock)
{
	/// <summary>No model is configured.</summary>
	public static readonly Error NotConfigured = Error.Unavailable("ai.not_configured", "Questions need the model, and no API key is configured.");

	/// <summary>The day's budget is spent.</summary>
	public static readonly Error DailyBudgetSpent = Error.Unavailable("ai.daily_budget", "Today's budget for questions is spent; ask again tomorrow.");

	/// <summary>A follow-up to a conversation that does not exist on this dataset.</summary>
	public static readonly Error UnknownConversation = Error.NotFound("conversation.not_found", "There is no such conversation about this dataset; ask without one to start a new one.");

	/// <summary>Asks a question.</summary>
	/// <param name="datasetId">The dataset.</param>
	/// <param name="question">The question.</param>
	/// <param name="conversationId">The conversation it follows up on, or null to start one.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The answer, or why there is none.</returns>
	public async Task<Result<AskAnswer>> ExecuteAsync(Guid datasetId, string question, Guid? conversationId, CancellationToken cancellationToken)
	{
		var limits = options.Value;
		if (string.IsNullOrWhiteSpace(question) || question.Length > limits.MaxQuestionLength)
		{
			return Error.Validation("question.invalid", $"A question has 1 to {limits.MaxQuestionLength} characters.");
		}

		if (await datasets.FindAsync(datasetId, cancellationToken).ConfigureAwait(false) is not { } dataset)
		{
			return DatasetQueries.NotFound;
		}

		if (await layers.FindAsync(datasetId, cancellationToken).ConfigureAwait(false) is not { } layer)
		{
			return RunQuery.NotConfirmed;
		}

		PreviousQuestion? previous = null;
		if (conversationId is { } existing
			&& (previous = await log.LastInConversationAsync(existing, datasetId, cancellationToken).ConfigureAwait(false)) is null)
		{
			return UnknownConversation;
		}

		if (!translator.IsConfigured)
		{
			return NotConfigured;
		}

		var now = clock.GetCurrentInstant();
		if (await log.SpentSinceAsync(now.InUtc().Date.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant(), cancellationToken).ConfigureAwait(false) >= limits.DailyBudgetUsd)
		{
			return DailyBudgetSpent;
		}

		var started = Stopwatch.GetTimestamp();
		var today = layer.AsOf ?? now.InUtc().Date;
		var text = question.Trim();
		var translation = await translator.TranslateAsync(new TranslationRequest(text, dataset, layer, today, [], previous), cancellationToken).ConfigureAwait(false);
		var usage = translation.Usage;
		var issues = translation.Query?.Validate(layer) ?? [];
		if (issues.Count > 0 && usage.CostUsd < limits.QuestionBudgetUsd)
		{
			translation = await translator.TranslateAsync(new TranslationRequest(text, dataset, layer, today, issues, previous), cancellationToken).ConfigureAwait(false);
			usage = usage.Plus(translation.Usage);
			issues = translation.Query?.Validate(layer) ?? [];
		}

		if (translation.Query is { } read && issues.Count == 0)
		{
			translation = translation with { Query = read.WithoutConstantGroups() };
		}

		QueryAnswer? answer = null;
		Error? error = null;
		if (translation.Failure is not null)
		{
			error = Error.Unavailable("ai.failed", "The model could not read the question; try rephrasing it.");
		}
		else if (translation.Query is { } query && issues.Count == 0)
		{
			var result = await runner.RunAsync(dataset, layer, query, today, cancellationToken).ConfigureAwait(false);
			answer = result.IsSuccess ? result.Value : null;
			error = result.Error;
		}
		else if (translation.Query is not null)
		{
			error = Error.Validation("question.untranslatable", "The question could not be turned into a valid query; try rephrasing it.");
		}

		// What the model wrote is kept in the log even when discarded, so a discard can be looked into; only a summary
		// that passed the check reaches the person.
		string? written = null;
		var discarded = false;
		if (answer is { Rows.Count: > 0 and <= NarrationFacts.MaxRows } && NarrationFacts.HasFigures(answer) && narrator.IsConfigured && usage.CostUsd < limits.QuestionBudgetUsd)
		{
			var facts = NarrationFacts.Of(text, answer, translation.Query?.Limit);
			var narration = await narrator.NarrateAsync(new NarrationRequest(text, translation.Interpretation, facts.Text), cancellationToken).ConfigureAwait(false);
			usage = usage.Plus(narration.Usage);
			if (narration.Text is { Length: > 0 } summary)
			{
				written = summary;
				discarded = facts.Unsupported(summary).Count > 0;
			}
		}

		var narrative = discarded ? null : written;

		var conversation = conversationId ?? Guid.CreateVersion7();
		var outcome = error is not null ? QuestionOutcome.Failed : answer is not null ? QuestionOutcome.Answered : QuestionOutcome.Clarified;
		var id = await log.AddAsync(
			new QuestionRecord(
				datasetId,
				conversation,
				text,
				translation.Interpretation,
				translation.Query,
				translation.Clarification,
				outcome,
				error?.Code,
				answer?.Rows.Count ?? 0,
				usage,
				(long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
				written,
				discarded),
			cancellationToken).ConfigureAwait(false);

		if (error is not null)
		{
			return error;
		}

		if (answer is null && translation.Clarification is null)
		{
			return Error.Unavailable("ai.failed", "The model returned neither a query nor a question back.");
		}

		return new AskAnswer(id, conversation, translation.Interpretation, translation.Query, translation.Clarification, answer, narrative, usage);
	}
}
