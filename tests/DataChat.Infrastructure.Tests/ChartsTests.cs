namespace DataChat.Infrastructure.Tests;

using DataChat.Application.Features.Questions;

/// <summary>The chart follows the answer's shape (ADR 0004).</summary>
public sealed class ChartsTests
{
	private static readonly ResultColumn Time = new("time", "time", ResultColumnKind.Time);

	private static readonly ResultColumn Branch = new("sucursal", "Sucursal", ResultColumnKind.Dimension);

	private static readonly ResultColumn Category = new("categoria", "Categoría", ResultColumnKind.Dimension);

	private static readonly ResultColumn Sales = new("ventas", "Ventas", ResultColumnKind.Metric);

	private static readonly ResultColumn Previous = new("ventas", "Ventas", ResultColumnKind.PreviousMetric);

	[Fact]
	public void A_total_is_a_figure() =>
		Assert.Equal(ChartKind.Kpi, Charts.Choose([Sales, Previous], [[33_716.42m, 35_429.75m]]));

	[Fact]
	public void A_series_over_time_is_a_line() =>
		Assert.Equal(ChartKind.Line, Charts.Choose([Time, Sales], [["2026-01", 1m], ["2026-02", 2m]]));

	[Fact]
	public void A_single_period_is_a_figure() =>
		Assert.Equal(ChartKind.Kpi, Charts.Choose([Time, Sales], [["2025-12", 19_939.40m]]));

	[Fact]
	public void A_few_branches_over_time_are_lines_many_are_a_table()
	{
		Assert.Equal(ChartKind.Line, Charts.Choose([Time, Branch, Sales], [["2026-07", "Centro", 1m], ["2026-07", "Altamira", 2m]]));
		Assert.Equal(
			ChartKind.Table,
			Charts.Choose([Time, Branch, Sales], [.. Enumerable.Range(0, Charts.MaxSeries + 1).Select(static i => (IReadOnlyList<object?>)["2026-07", $"P{i}", 1m])]));
	}

	[Fact]
	public void A_dimension_is_bars_and_two_are_a_pivot()
	{
		Assert.Equal(ChartKind.Bar, Charts.Choose([Branch, Sales, Previous], [["Centro", 1m, 2m]]));
		Assert.Equal(ChartKind.Pivot, Charts.Choose([Branch, Category, Sales], [["Centro", "Bebidas", 1m]]));
	}

	[Fact]
	public void Nothing_to_draw_is_a_table() =>
		Assert.Equal(ChartKind.Table, Charts.Choose([Branch, Sales], []));
}
