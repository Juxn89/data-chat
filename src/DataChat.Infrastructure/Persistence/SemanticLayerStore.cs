namespace DataChat.Infrastructure.Persistence;

using System.Text.Json;
using DataChat.Application.Features.Semantics;
using DataChat.Domain.Semantics;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;

/// <summary>A row of <c>semantic_layers</c>: the confirmed layer of a dataset, as a JSON document.</summary>
internal sealed class SemanticLayerRecord
{
	public Guid DatasetId { get; set; }

	public required string Document { get; set; }

	public Instant UpdatedAt { get; set; }
}

/// <summary>
/// Keeps each confirmed layer as one <c>jsonb</c> document: it is read and written whole, and its shape (metrics with
/// formulas of two kinds) is a document, not rows to join.
/// </summary>
/// <param name="db">The context.</param>
/// <param name="clock">The clock.</param>
internal sealed class SemanticLayerStore(DataChatDbContext db, IClock clock) : ISemanticLayerStore
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public async Task<SemanticLayer?> FindAsync(Guid datasetId, CancellationToken cancellationToken) =>
		await db.SemanticLayers.AsNoTracking().SingleOrDefaultAsync(l => l.DatasetId == datasetId, cancellationToken).ConfigureAwait(false) is { } record
			? JsonSerializer.Deserialize<LayerDocument>(record.Document, Json)!.ToDomain()
			: null;

	public async Task SaveAsync(Guid datasetId, SemanticLayer layer, CancellationToken cancellationToken)
	{
		var document = JsonSerializer.Serialize(LayerDocument.From(layer), Json);
		if (await db.SemanticLayers.SingleOrDefaultAsync(l => l.DatasetId == datasetId, cancellationToken).ConfigureAwait(false) is { } record)
		{
			record.Document = document;
			record.UpdatedAt = clock.GetCurrentInstant();
		}
		else
		{
			db.SemanticLayers.Add(new SemanticLayerRecord { DatasetId = datasetId, Document = document, UpdatedAt = clock.GetCurrentInstant() });
		}

		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>The stored shape. Formulas are flattened: an aggregate fills aggregation and column, a ratio its two keys.</summary>
	private sealed record LayerDocument(string? TimeColumn, string? AsOf, IReadOnlyList<MetricDocument> Metrics, IReadOnlyList<Dimension> Dimensions)
	{
		public static LayerDocument From(SemanticLayer layer) => new(
			layer.TimeColumn,
			layer.AsOf is { } asOf ? LocalDatePattern.Iso.Format(asOf) : null,
			layer.Metrics.Select(static metric => metric.Formula switch
			{
				Aggregate aggregate => new MetricDocument(metric.Key, metric.Name, aggregate.Aggregation, aggregate.Column, null, null),
				Ratio ratio => new MetricDocument(metric.Key, metric.Name, null, null, ratio.Numerator, ratio.Denominator),
				_ => throw new InvalidOperationException($"Metric {metric.Key} has an unknown formula."),
			}).ToArray(),
			layer.Dimensions);

		public SemanticLayer ToDomain() => new(
			TimeColumn,
			Metrics.Select(static metric => new Metric(
				metric.Key,
				metric.Name,
				metric.Aggregation is { } aggregation ? new Aggregate(aggregation, metric.Column) : new Ratio(metric.Numerator!, metric.Denominator!))).ToArray(),
			Dimensions,
			AsOf is null ? null : LocalDatePattern.Iso.Parse(AsOf).Value);
	}

	private sealed record MetricDocument(string Key, string Name, Aggregation? Aggregation, string? Column, string? Numerator, string? Denominator);
}
