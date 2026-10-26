namespace DataChat.Application.Features.Questions;

using DataChat.Application.Common;
using DataChat.Application.Features.Datasets;
using Microsoft.Extensions.Options;
using NodaTime;

/// <summary>What a person said about an answer.</summary>
public enum Feedback
{
	/// <summary>It answered the question.</summary>
	Helpful,

	/// <summary>"Esto no es lo que pregunté": it answered something else.</summary>
	Wrong,
}

/// <summary>A recorded question, as the history shows it.</summary>
/// <param name="Id">The question.</param>
/// <param name="ConversationId">Its conversation.</param>
/// <param name="Text">What was asked.</param>
/// <param name="Interpretation">How it was read.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="ErrorCode">Why it failed, when it did.</param>
/// <param name="Clarification">The question back, when there was one.</param>
/// <param name="RowCount">The answer's rows.</param>
/// <param name="CostUsd">What the model cost.</param>
/// <param name="LatencyMs">How long it took.</param>
/// <param name="SummaryShown">A checked summary was shown.</param>
/// <param name="SummaryDiscarded">A summary was written and discarded for quoting a figure not in the answer.</param>
/// <param name="Feedback">What the person said about it, if anything.</param>
/// <param name="FeedbackNote">Their note.</param>
/// <param name="AskedAt">When.</param>
public sealed record QuestionSummary(
	Guid Id,
	Guid ConversationId,
	string Text,
	string Interpretation,
	QuestionOutcome Outcome,
	string? ErrorCode,
	string? Clarification,
	int RowCount,
	decimal CostUsd,
	long LatencyMs,
	bool SummaryShown,
	bool SummaryDiscarded,
	Feedback? Feedback,
	string? FeedbackNote,
	Instant AskedAt);

/// <summary>The recorded questions, read back.</summary>
public interface IQuestionHistory
{
	/// <summary>A dataset's latest questions, newest first.</summary>
	/// <param name="datasetId">The dataset.</param>
	/// <param name="limit">How many.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The questions.</returns>
	Task<IReadOnlyList<QuestionSummary>> ListAsync(Guid datasetId, int limit, CancellationToken cancellationToken);

	/// <summary>Records what a person said about an answer, replacing what they said before.</summary>
	/// <param name="questionId">The question.</param>
	/// <param name="feedback">What they said.</param>
	/// <param name="note">Their note, if any.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>False when there is no such question.</returns>
	Task<bool> SetFeedbackAsync(Guid questionId, Feedback feedback, string? note, CancellationToken cancellationToken);
}

/// <summary>A dataset's questions and what the model has spent today against the day's budget.</summary>
/// <param name="Items">The latest questions, newest first.</param>
/// <param name="SpentTodayUsd">What all questions cost today (UTC), on every dataset: the budget is the app's.</param>
/// <param name="DailyBudgetUsd">The day's budget.</param>
public sealed record QuestionHistoryView(IReadOnlyList<QuestionSummary> Items, decimal SpentTodayUsd, decimal DailyBudgetUsd);

/// <summary>
/// What was asked of a dataset and how it went (plan §8): every question with its reading, outcome, cost and the
/// feedback people left. "Esto no es lo que pregunté" is how a misreading the evals did not cover gets found.
/// </summary>
/// <param name="datasets">The datasets.</param>
/// <param name="history">The recorded questions.</param>
/// <param name="log">The question log, for today's spend.</param>
/// <param name="options">The budget.</param>
/// <param name="clock">The clock.</param>
public sealed class QuestionHistory(IDatasetStore datasets, IQuestionHistory history, IQuestionLog log, IOptions<AskOptions> options, IClock clock)
{
	/// <summary>How many questions the history shows.</summary>
	public const int Shown = 100;

	/// <summary>The longest note.</summary>
	public const int MaxNoteLength = 500;

	/// <summary>No question with that id.</summary>
	public static readonly Error NotFound = Error.NotFound("question.not_found", "There is no question with that id.");

	/// <summary>A dataset's history.</summary>
	/// <param name="datasetId">The dataset.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The history, or <see cref="DatasetQueries.NotFound"/>.</returns>
	public async Task<Result<QuestionHistoryView>> GetAsync(Guid datasetId, CancellationToken cancellationToken)
	{
		if (await datasets.FindAsync(datasetId, cancellationToken).ConfigureAwait(false) is null)
		{
			return DatasetQueries.NotFound;
		}

		var items = await history.ListAsync(datasetId, Shown, cancellationToken).ConfigureAwait(false);
		var startOfDay = clock.GetCurrentInstant().InUtc().Date.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
		var spent = await log.SpentSinceAsync(startOfDay, cancellationToken).ConfigureAwait(false);
		return new QuestionHistoryView(items, spent, options.Value.DailyBudgetUsd);
	}

	/// <summary>Records feedback on an answer.</summary>
	/// <param name="questionId">The question.</param>
	/// <param name="feedback">What the person said.</param>
	/// <param name="note">Their note.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>Nothing, or why not.</returns>
	public async Task<Result<bool>> GiveFeedbackAsync(Guid questionId, Feedback feedback, string? note, CancellationToken cancellationToken)
	{
		var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
		if (trimmed is { Length: > MaxNoteLength })
		{
			return Error.Validation("feedback.invalid", $"A note has up to {MaxNoteLength} characters.");
		}

		return await history.SetFeedbackAsync(questionId, feedback, trimmed, cancellationToken).ConfigureAwait(false) ? true : NotFound;
	}
}
