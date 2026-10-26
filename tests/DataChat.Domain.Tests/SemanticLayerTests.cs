namespace DataChat.Domain.Tests;

using DataChat.Domain.Semantics;

public sealed class SemanticLayerTests
{
	private static readonly SemanticLayer Valid = SampleDataset.Layer;

	[Fact]
	public void The_demo_layer_is_valid() =>
		Assert.Empty(Valid.Validate(SampleDataset.Dataset));

	[Fact]
	public void A_layer_without_metrics_answers_nothing() =>
		Assert.Contains(Codes(Valid with { Metrics = [] }), static code => code == "semantic.no_metrics");

	[Fact]
	public void Summing_a_text_column_is_rejected() =>
		Assert.Contains("semantic.invalid_aggregation", Codes(WithMetric(new Metric("x", "X", new Aggregate(Aggregation.Sum, "c_003")))));

	[Fact]
	public void A_column_the_dataset_lacks_is_rejected() =>
		Assert.Contains("semantic.unknown_column", Codes(WithMetric(new Metric("x", "X", new Aggregate(Aggregation.Sum, "c_099")))));

	[Fact]
	public void A_ratio_of_a_ratio_is_rejected() =>
		Assert.Contains("semantic.invalid_ratio", Codes(WithMetric(new Metric("x", "X", new Ratio("ticket_promedio", "unidades")))));

	[Fact]
	public void A_metric_divided_by_itself_is_rejected() =>
		Assert.Contains("semantic.invalid_ratio", Codes(WithMetric(new Metric("x", "X", new Ratio("ventas", "ventas")))));

	[Fact]
	public void A_count_of_rows_needs_no_column() =>
		Assert.Empty(WithMetric(new Metric("lineas", "Líneas", new Aggregate(Aggregation.Count, null))).Validate(SampleDataset.Dataset));

	[Fact]
	public void Keys_are_plain_and_unique_across_metrics_and_dimensions()
	{
		Assert.Contains("semantic.invalid_key", Codes(WithMetric(new Metric("Ventas Netas", "Ventas netas", new Aggregate(Aggregation.Sum, "c_008")))));
		Assert.Contains("semantic.duplicate_key", Codes(WithMetric(new Metric("sucursal", "Otra", new Aggregate(Aggregation.Sum, "c_008")))));
	}

	[Fact]
	public void Names_are_unique_whatever_their_case() =>
		Assert.Contains("semantic.duplicate_name", Codes(WithMetric(new Metric("otra", "VENTAS", new Aggregate(Aggregation.Sum, "c_008")))));

	[Fact]
	public void A_date_column_is_time_not_a_dimension() =>
		Assert.Contains("semantic.invalid_dimension", Codes(Valid with { Dimensions = [.. Valid.Dimensions, new Dimension("fecha", "Fecha", "c_001")] }));

	[Fact]
	public void The_time_column_must_be_a_date() =>
		Assert.Contains("semantic.invalid_time_column", Codes(Valid with { TimeColumn = "c_003" }));

	private static SemanticLayer WithMetric(Metric metric) => Valid with { Metrics = [.. Valid.Metrics, metric] };

	private static IEnumerable<string> Codes(SemanticLayer layer) => layer.Validate(SampleDataset.Dataset).Select(static issue => issue.Code);
}
