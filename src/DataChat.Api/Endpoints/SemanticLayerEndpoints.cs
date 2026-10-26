namespace DataChat.Api.Endpoints;

using DataChat.Application.Features.Semantics;
using DataChat.Domain.Datasets;
using DataChat.Domain.Semantics;
using NodaTime;

/// <summary>A dataset's semantic layer: the proposal to review, and the confirmation.</summary>
internal static class SemanticLayerEndpoints
{
	public static void MapSemanticLayerEndpoints(this IEndpointRouteBuilder app)
	{
		var layer = app.MapGroup("/v1/datasets/{id:guid}/semantic-layer");
		layer.MapGet("/", GetAsync);
		layer.MapPut("/", ConfirmAsync);
	}

	private static async Task<IResult> GetAsync(Guid id, SemanticLayers layers, CancellationToken cancellationToken) =>
		(await layers.GetAsync(id, cancellationToken)).ToHttp(static view => Results.Ok(SemanticLayerResponse.From(view)));

	private static async Task<IResult> ConfirmAsync(Guid id, SemanticLayerRequest request, SemanticLayers layers, CancellationToken cancellationToken)
	{
		var current = await layers.GetAsync(id, cancellationToken);
		if (!current.IsSuccess)
		{
			return current.Error!.ToProblem();
		}

		if (request.ToDomain(current.Value.Dataset) is not { } layer)
		{
			return SemanticLayers.Invalid([new SemanticIssue("semantic.unknown_column", "A metric, dimension or the time refers to a column this dataset does not have.")]).ToProblem();
		}

		return (await layers.ConfirmAsync(id, layer, cancellationToken)).ToHttp(static view => Results.Ok(SemanticLayerResponse.From(view)));
	}
}

/// <summary>
/// A metric as the API shows it: an aggregate has an aggregation and a column (by ordinal; none for a row count), a
/// ratio the keys of the two metrics it divides.
/// </summary>
/// <param name="Key">Its key.</param>
/// <param name="Name">Its name.</param>
/// <param name="Aggregation">For an aggregate.</param>
/// <param name="Column">For an aggregate: the column's ordinal.</param>
/// <param name="Numerator">For a ratio.</param>
/// <param name="Denominator">For a ratio.</param>
internal sealed record MetricDto(string Key, string Name, Aggregation? Aggregation = null, int? Column = null, string? Numerator = null, string? Denominator = null);

/// <summary>A dimension; its column by ordinal.</summary>
/// <param name="Key">Its key.</param>
/// <param name="Name">Its name.</param>
/// <param name="Column">The column's ordinal.</param>
internal sealed record DimensionDto(string Key, string Name, int Column);

/// <summary>A layer to confirm. Columns are referred to by ordinal: physical names stay inside the server.</summary>
/// <param name="TimeColumn">The date column's ordinal, or null.</param>
/// <param name="AsOf">The "today" for named periods, or null for the real one.</param>
/// <param name="Metrics">The metrics.</param>
/// <param name="Dimensions">The dimensions.</param>
internal sealed record SemanticLayerRequest(int? TimeColumn, LocalDate? AsOf, IReadOnlyList<MetricDto> Metrics, IReadOnlyList<DimensionDto> Dimensions)
{
	/// <summary>The layer, or null when an ordinal is not one of the dataset's columns.</summary>
	public SemanticLayer? ToDomain(Dataset dataset)
	{
		string? Physical(int? ordinal) => dataset.Columns.FirstOrDefault(column => column.Ordinal == ordinal)?.PhysicalName;

		if ((TimeColumn is not null && Physical(TimeColumn) is null)
			|| Metrics.Any(metric => metric.Column is not null && Physical(metric.Column) is null)
			|| Dimensions.Any(dimension => Physical(dimension.Column) is null))
		{
			return null;
		}

		return new SemanticLayer(
			Physical(TimeColumn),
			Metrics.Select(metric => new Metric(
				metric.Key,
				metric.Name,
				metric.Aggregation is { } aggregation
					? new Aggregate(aggregation, Physical(metric.Column))
					: new Ratio(metric.Numerator ?? string.Empty, metric.Denominator ?? string.Empty))).ToArray(),
			Dimensions.Select(dimension => new Dimension(dimension.Key, dimension.Name, Physical(dimension.Column)!)).ToArray(),
			AsOf);
	}
}

/// <summary>A dataset's layer, with its columns for the editor.</summary>
/// <param name="DatasetId">The dataset.</param>
/// <param name="Confirmed">False while it is the app's proposal.</param>
/// <param name="TimeColumn">The date column's ordinal.</param>
/// <param name="AsOf">The "today" for named periods, when set.</param>
/// <param name="Metrics">The metrics.</param>
/// <param name="Dimensions">The dimensions.</param>
/// <param name="Columns">The dataset's columns.</param>
internal sealed record SemanticLayerResponse(
	Guid DatasetId,
	bool Confirmed,
	int? TimeColumn,
	LocalDate? AsOf,
	IReadOnlyList<MetricDto> Metrics,
	IReadOnlyList<DimensionDto> Dimensions,
	IReadOnlyList<ColumnResponse> Columns)
{
	public static SemanticLayerResponse From(SemanticLayerView view)
	{
		var dataset = view.Dataset;
		int Ordinal(string physical) => dataset.Column(physical)!.Ordinal;

		return new SemanticLayerResponse(
			dataset.Id,
			view.Confirmed,
			view.Layer.TimeColumn is { } time ? Ordinal(time) : null,
			view.Layer.AsOf,
			view.Layer.Metrics.Select(metric => metric.Formula switch
			{
				Aggregate aggregate => new MetricDto(metric.Key, metric.Name, aggregate.Aggregation, aggregate.Column is { } column ? Ordinal(column) : null),
				Ratio ratio => new MetricDto(metric.Key, metric.Name, Numerator: ratio.Numerator, Denominator: ratio.Denominator),
				_ => throw new InvalidOperationException($"Metric {metric.Key} has an unknown formula."),
			}).ToArray(),
			view.Layer.Dimensions.Select(dimension => new DimensionDto(dimension.Key, dimension.Name, Ordinal(dimension.Column))).ToArray(),
			dataset.Columns.Select(ColumnResponse.From).ToArray());
	}
}
