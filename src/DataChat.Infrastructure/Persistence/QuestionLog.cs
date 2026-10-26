namespace DataChat.Infrastructure.Persistence;

using System.Text.Json;
using DataChat.Application.Features.Questions;
using DataChat.Infrastructure.Ai;
using Microsoft.EntityFrameworkCore;
using NodaTime;

/// <summary>A row of <c>questions</c>: what was asked, how it was read and what it cost.</summary>
internal sealed class QuestionEntity
{
	public Guid Id { get; set; }

	public Guid DatasetId { get; set; }

	public Guid ConversationId { get; set; }

	public required string Text { get; set; }

	public required string Interpretation { get; set; }

	public string? SemanticQuery { get; set; }

	public string? Clarification { get; set; }

	public QuestionOutcome Outcome { get; set; }

	public string? ErrorCode { get; set; }

	public int RowCount { get; set; }

	public required string Model { get; set; }

	public long InputTokens { get; set; }

	public long OutputTokens { get; set; }

	public decimal CostUsd { get; set; }

	public int ModelCalls { get; set; }

	public long LatencyMs { get; set; }

	public string? Narrative { get; set; }

	public bool NarrativeDiscarded { get; set; }

	public Feedback? Feedback { get; set; }

	public string? FeedbackNote { get; set; }

	public Instant? FeedbackAt { get; set; }

	public Instant CreatedAt { get; set; }
}

/// <summary>Records questions, reads them back and keeps the feedback on them; their costs are what the daily budget adds up.</summary>
/// <param name="db">The context.</param>
/// <param name="clock">The clock.</param>
internal sealed class QuestionLog(DataChatDbContext db, IClock clock) : IQuestionLog, IQuestionHistory
{
	public async Task<IReadOnlyList<QuestionSummary>> ListAsync(Guid datasetId, int limit, CancellationToken cancellationToken) =>
		await db.Questions.AsNoTracking()
			.Where(q => q.DatasetId == datasetId)
			.OrderByDescending(static q => q.CreatedAt)
			.Take(limit)
			.Select(static q => new QuestionSummary(
				q.Id,
				q.ConversationId,
				q.Text,
				q.Interpretation,
				q.Outcome,
				q.ErrorCode,
				q.Clarification,
				q.RowCount,
				q.CostUsd,
				q.LatencyMs,
				q.Narrative != null && !q.NarrativeDiscarded,
				q.NarrativeDiscarded,
				q.Feedback,
				q.FeedbackNote,
				q.CreatedAt))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task<bool> SetFeedbackAsync(Guid questionId, Feedback feedback, string? note, CancellationToken cancellationToken)
	{
		if (await db.Questions.SingleOrDefaultAsync(q => q.Id == questionId, cancellationToken).ConfigureAwait(false) is not { } question)
		{
			return false;
		}

		question.Feedback = feedback;
		question.FeedbackNote = note;
		question.FeedbackAt = clock.GetCurrentInstant();
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return true;
	}

	public async Task<Guid> AddAsync(QuestionRecord question, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(question);
		var entity = new QuestionEntity
		{
			Id = Guid.CreateVersion7(),
			DatasetId = question.DatasetId,
			ConversationId = question.ConversationId,
			Text = question.Text,
			Interpretation = question.Interpretation,
			SemanticQuery = question.Query is null ? null : JsonSerializer.Serialize(question.Query, QuestionSchema.Json),
			Clarification = question.Clarification,
			Outcome = question.Outcome,
			ErrorCode = question.ErrorCode,
			RowCount = question.RowCount,
			Model = question.Usage.Model,
			InputTokens = question.Usage.InputTokens,
			OutputTokens = question.Usage.OutputTokens,
			CostUsd = question.Usage.CostUsd,
			ModelCalls = question.Usage.Calls,
			LatencyMs = question.LatencyMs,
			Narrative = question.Narrative,
			NarrativeDiscarded = question.NarrativeDiscarded,
			CreatedAt = clock.GetCurrentInstant(),
		};
		db.Questions.Add(entity);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return entity.Id;
	}

	public async Task<PreviousQuestion?> LastInConversationAsync(Guid conversationId, Guid datasetId, CancellationToken cancellationToken)
	{
		var last = await db.Questions.AsNoTracking()
			.Where(q => q.ConversationId == conversationId && q.DatasetId == datasetId)
			.OrderByDescending(static q => q.CreatedAt)
			.Select(static q => new { q.Text, q.SemanticQuery })
			.FirstOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);
		return last is null
			? null
			: new PreviousQuestion(last.Text, last.SemanticQuery is null ? null : JsonSerializer.Deserialize<Domain.Semantics.SemanticQuery>(last.SemanticQuery, QuestionSchema.Json));
	}

	public async Task<decimal> SpentSinceAsync(Instant since, CancellationToken cancellationToken) =>
		await db.Questions.Where(q => q.CreatedAt >= since).SumAsync(static q => q.CostUsd, cancellationToken).ConfigureAwait(false);
}
