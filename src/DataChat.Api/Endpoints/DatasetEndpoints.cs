namespace DataChat.Api.Endpoints;

using DataChat.Application.Features.Datasets;
using DataChat.Domain.Datasets;
using Microsoft.AspNetCore.Mvc;
using NodaTime;

/// <summary>Importing datasets and reading what was imported.</summary>
internal static class DatasetEndpoints
{
	/// <summary>Room for the multipart envelope around a file at the size limit.</summary>
	private const int MultipartOverheadBytes = 64 * 1024;

	public static void MapDatasetEndpoints(this IEndpointRouteBuilder app, int maxFileBytes)
	{
		var datasets = app.MapGroup("/v1/datasets");

		// The web app posts through its own origin (next.config.ts rewrites /api), and there is no cookie or session to
		// forge a request with, so the antiforgery token would protect nothing.
		datasets.MapPost("/", ImportAsync)
			.DisableAntiforgery()
			.WithMetadata(new RequestSizeLimitAttribute(maxFileBytes + MultipartOverheadBytes));
		datasets.MapGet("/", ListAsync);
		datasets.MapGet("/{id:guid}", GetAsync);
	}

	private static async Task<IResult> ImportAsync(IFormFile? file, [FromForm] string? name, ImportDataset import, CancellationToken cancellationToken)
	{
		if (file is null)
		{
			return ImportErrors.Empty.ToProblem();
		}

		await using var content = file.OpenReadStream();
		var result = await import.ExecuteAsync(new ImportRequest(file.FileName, file.Length, content, name), cancellationToken);
		return result.ToHttp(imported => Results.Created($"/v1/datasets/{imported.Dataset.Id}", DatasetResponse.From(imported.Dataset, imported.AmbiguousDateColumns)));
	}

	private static async Task<IResult> ListAsync(DatasetQueries queries, CancellationToken cancellationToken) =>
		Results.Ok((await queries.ListAsync(cancellationToken)).Select(static dataset => DatasetSummary.From(dataset)));

	private static async Task<IResult> GetAsync(Guid id, DatasetQueries queries, CancellationToken cancellationToken) =>
		(await queries.GetAsync(id, cancellationToken)).ToHttp(static dataset => Results.Ok(DatasetResponse.From(dataset, [])));
}

/// <summary>A dataset in a list.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Name">Its name.</param>
/// <param name="SourceFile">The file it came from.</param>
/// <param name="RowCount">Its rows.</param>
/// <param name="ColumnCount">Its columns.</param>
/// <param name="ImportedAt">When it was imported.</param>
internal sealed record DatasetSummary(Guid Id, string Name, string SourceFile, int RowCount, int ColumnCount, Instant ImportedAt)
{
	public static DatasetSummary From(Dataset dataset) =>
		new(dataset.Id, dataset.Name, dataset.SourceFile, dataset.RowCount, dataset.Columns.Count, dataset.ImportedAt);
}

/// <summary>A dataset with its columns. The physical names stay inside the server.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Name">Its name.</param>
/// <param name="SourceFile">The file it came from.</param>
/// <param name="RowCount">Its rows.</param>
/// <param name="ImportedAt">When it was imported.</param>
/// <param name="Columns">Its columns.</param>
/// <param name="AmbiguousDateColumns">Right after an import: date columns read day-first because every date fit both orders.</param>
internal sealed record DatasetResponse(
	Guid Id,
	string Name,
	string SourceFile,
	int RowCount,
	Instant ImportedAt,
	IReadOnlyList<ColumnResponse> Columns,
	IReadOnlyList<string> AmbiguousDateColumns)
{
	public static DatasetResponse From(Dataset dataset, IReadOnlyList<string> ambiguousDateColumns) => new(
		dataset.Id,
		dataset.Name,
		dataset.SourceFile,
		dataset.RowCount,
		dataset.ImportedAt,
		dataset.Columns.Select(ColumnResponse.From).ToArray(),
		ambiguousDateColumns);
}

/// <summary>A column.</summary>
/// <param name="Ordinal">Its position, from 1.</param>
/// <param name="Name">Its name.</param>
/// <param name="Type">What it holds.</param>
/// <param name="DistinctCount">How many distinct values it has.</param>
/// <param name="Samples">Its most frequent values.</param>
internal sealed record ColumnResponse(int Ordinal, string Name, ColumnType Type, int DistinctCount, IReadOnlyList<string> Samples)
{
	public static ColumnResponse From(DatasetColumn column) =>
		new(column.Ordinal, column.Name, column.Type, column.Profile.DistinctCount, column.Profile.Samples);
}
