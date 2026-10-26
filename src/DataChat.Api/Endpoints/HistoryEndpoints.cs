namespace DataChat.Api.Endpoints;

using DataChat.Application.Features.Questions;

/// <summary>A dataset's questions, and the feedback on each answer.</summary>
internal static class HistoryEndpoints
{
	public static void MapHistoryEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapGet("/v1/datasets/{id:guid}/questions", HistoryAsync);
		app.MapPost("/v1/questions/{id:guid}/feedback", FeedbackAsync);
	}

	private static async Task<IResult> HistoryAsync(Guid id, QuestionHistory history, CancellationToken cancellationToken) =>
		(await history.GetAsync(id, cancellationToken)).ToHttp(static view => Results.Ok(view));

	private static async Task<IResult> FeedbackAsync(Guid id, FeedbackRequest request, QuestionHistory history, CancellationToken cancellationToken) =>
		(await history.GiveFeedbackAsync(id, request.Feedback, request.Note, cancellationToken)).ToHttp(static _ => Results.NoContent());
}

/// <summary>Feedback on an answer.</summary>
/// <param name="Feedback">Helpful, or Wrong ("esto no es lo que pregunté").</param>
/// <param name="Note">What the person meant, if they say.</param>
internal sealed record FeedbackRequest(Feedback Feedback, string? Note);
