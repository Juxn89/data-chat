namespace DataChat.Api.Endpoints;

using DataChat.Application.Features.Questions;

/// <summary>Questions in plain language: the chat's endpoint.</summary>
internal static class QuestionEndpoints
{
	public static void MapQuestionEndpoints(this IEndpointRouteBuilder app) =>
		app.MapPost("/v1/datasets/{id:guid}/questions", AskAsync);

	private static async Task<IResult> AskAsync(Guid id, QuestionRequest request, AskQuestion ask, CancellationToken cancellationToken) =>
		(await ask.ExecuteAsync(id, request.Text ?? string.Empty, request.ConversationId, cancellationToken)).ToHttp(static answer => Results.Ok(answer));
}

/// <summary>A question.</summary>
/// <param name="Text">What the person asked.</param>
/// <param name="ConversationId">The conversation it follows up on, from the previous answer; null to start one.</param>
internal sealed record QuestionRequest(string? Text, Guid? ConversationId);
