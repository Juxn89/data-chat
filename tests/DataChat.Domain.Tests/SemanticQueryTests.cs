namespace DataChat.Domain.Tests;

using DataChat.Domain.Periods;
using DataChat.Domain.Semantics;
using NodaTime;

/// <summary>The query shapes the evals' questions need are valid; the ones a chart or the compiler cannot honour are not.</summary>
public sealed class SemanticQueryTests
{
	private static readonly SemanticLayer Layer = SampleDataset.Layer;

	private static readonly PeriodSpec LastQuarter = new(NamedPeriod.LastQuarter);

	public static TheoryData<string, SemanticQuery> EvalShapes => new()
	{
		// q01: a total for a named period.
		{ "q01", new(["ventas"], [], Period: LastQuarter) },
		// q02: the top branch.
		{ "q02", new(["ventas"], ["sucursal"], Period: LastQuarter, Sort: new("ventas", SortDirection.Descending), Limit: 1) },
		// q04: a total against the same period last year.
		{ "q04", new(["ventas"], [], Period: LastQuarter, CompareTo: Comparison.SamePeriodLastYear) },
		// q29: a ratio along time, filtered.
		{ "q29", new(["ticket_promedio"], [], TimeGrain.Quarter, [new("sucursal", FilterOperator.In, ["Centro"])], Sort: new(SemanticQuery.TimeKey, SortDirection.Ascending)) },
		// q35: by branch, against the month before.
		{ "q35", new(["ventas"], ["sucursal"], Period: new(NamedPeriod.LastMonth), CompareTo: Comparison.PreviousPeriod) },
		// q37: days between two dates.
		{ "q37", new(["ventas"], [], TimeGrain.Day, Period: new(From: new LocalDate(2026, 9, 24), To: new LocalDate(2026, 9, 30))) },
		// q44: everything but two categories.
		{ "q44", new(["ventas"], ["categoria"], Filters: [new("categoria", FilterOperator.NotIn, ["Bebidas", "Lácteos"])], Period: new(NamedPeriod.LastSixMonths)) },
		// q56: two branches by month.
		{ "q56", new(["ventas"], ["sucursal"], TimeGrain.Month, [new("sucursal", FilterOperator.In, ["Centro", "Altamira"])], LastQuarter) },
	};

	[Theory]
	[MemberData(nameof(EvalShapes))]
	public void The_evals_shapes_are_valid(string id, SemanticQuery query) =>
		Assert.True(query.Validate(Layer).Count == 0, $"{id}: {string.Join("; ", query.Validate(Layer).Select(static i => i.Message))}");

	public static TheoryData<string, SemanticQuery> Invalid => new()
	{
		{ "query.metrics", new([], []) },
		{ "query.unknown_metric", new(["margen"], []) },
		{ "query.unknown_dimension", new(["ventas"], ["vendedor"]) },
		{ "query.repeated", new(["ventas", "ventas"], []) },
		{ "query.too_many_groups", new(["ventas"], ["sucursal", "categoria"], TimeGrain.Month) },
		{ "query.invalid_filter", new(["ventas"], [], Filters: [new("sucursal", FilterOperator.In, [])]) },
		{ "query.unknown_dimension", new(["ventas"], [], Filters: [new("vendedor", FilterOperator.In, ["Ana"])]) },
		{ "query.invalid_period", new(["ventas"], [], Period: new(NamedPeriod.LastMonth, new LocalDate(2026, 1, 1), new LocalDate(2026, 1, 31))) },
		{ "query.invalid_period", new(["ventas"], [], Period: new(From: new LocalDate(2026, 2, 1), To: new LocalDate(2026, 1, 1))) },
		{ "query.compare_without_period", new(["ventas"], [], CompareTo: Comparison.PreviousPeriod) },
		{ "query.compare_with_grain", new(["ventas"], [], TimeGrain.Month, Period: LastQuarter, CompareTo: Comparison.PreviousPeriod) },
		{ "query.invalid_sort", new(["ventas"], ["sucursal"], Sort: new("unidades", SortDirection.Descending)) },
		{ "query.invalid_sort", new(["ventas"], ["sucursal"], Sort: new(SemanticQuery.TimeKey, SortDirection.Ascending)) },
		{ "query.invalid_limit", new(["ventas"], [], Limit: 0) },
		{ "query.invalid_limit", new(["ventas"], [], Limit: SemanticQuery.MaxRows + 1) },
	};

	[Theory]
	[MemberData(nameof(Invalid))]
	public void Shapes_that_cannot_be_answered_say_why(string code, SemanticQuery query) =>
		Assert.Contains(code, query.Validate(Layer).Select(static issue => issue.Code));

	[Fact]
	public void Time_needs_a_date_column() =>
		Assert.Contains("query.no_time", new SemanticQuery(["ventas"], [], TimeGrain.Month).Validate(Layer with { TimeColumn = null }).Select(static i => i.Code));

	[Fact]
	public void A_named_period_resolves_against_the_layers_today() =>
		Assert.Equal(
			new DateInterval(new LocalDate(2026, 7, 1), new LocalDate(2026, 9, 30)),
			LastQuarter.Resolve(Layer.AsOf!.Value));
}
