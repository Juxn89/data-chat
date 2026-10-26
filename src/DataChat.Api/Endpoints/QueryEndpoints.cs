namespace DataChat.Api.Endpoints;

using DataChat.Application.Features.Questions;
using DataChat.Domain.Semantics;

/// <summary>
/// Runs a semantic query on a dataset. The chat (day 5) asks the model for the query; this endpoint takes one
/// directly, which is how the evals check the compiler and how the web app reruns an answer.
/// </summary>
internal static class QueryEndpoints
{
	public static void MapQueryEndpoints(this IEndpointRouteBuilder app) =>
		app.MapPost("/v1/datasets/{id:guid}/query", RunAsync);

	private static async Task<IResult> RunAsync(Guid id, SemanticQuery query, RunQuery run, CancellationToken cancellationToken) =>
		(await run.ExecuteAsync(id, query, cancellationToken)).ToHttp(static answer => Results.Ok(answer));
}
