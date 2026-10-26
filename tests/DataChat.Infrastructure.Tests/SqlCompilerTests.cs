namespace DataChat.Infrastructure.Tests;

using DataChat.Domain.Datasets;
using DataChat.Domain.Periods;
using DataChat.Domain.Semantics;
using DataChat.Infrastructure.Querying;
using NodaTime;

/// <summary>The SQL the compiler writes for typical questions, read whole, so a change to it is a visible diff.</summary>
public sealed class SqlCompilerTests
{
	private static readonly LocalDate Today = new(2026, 10, 15);

	[Fact]
	public void The_top_branch_last_quarter()
	{
		var compiled = Compile(new(["ventas"], ["sucursal"], Period: new(NamedPeriod.LastQuarter), Sort: new("ventas", SortDirection.Descending), Limit: 1));

		Assert.Equal(
			$"SELECT c_003 AS g1, round(sum(c_008), 2) AS m1 FROM {Sample.Table} WHERE c_001 BETWEEN @p1 AND @p2 GROUP BY g1 ORDER BY m1 DESC NULLS LAST, g1 ASC LIMIT 1",
			compiled.Sql);
		Assert.Equal([new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30)], compiled.Parameters.Select(static p => p.Value));
	}

	[Fact]
	public void Sales_against_the_same_quarter_last_year_scan_only_the_two_periods()
	{
		var compiled = Compile(new(["ventas"], [], Period: new(NamedPeriod.LastQuarter), CompareTo: Comparison.SamePeriodLastYear));

		Assert.Equal(
			$"SELECT round(sum(c_008) FILTER (WHERE c_001 BETWEEN @p1 AND @p2), 2) AS m1, round(sum(c_008) FILTER (WHERE c_001 BETWEEN @p3 AND @p4), 2) AS m1_previous FROM {Sample.Table} WHERE (c_001 BETWEEN @p1 AND @p2 OR c_001 BETWEEN @p3 AND @p4) LIMIT 1000",
			compiled.Sql);
		Assert.Equal(new DateInterval(new LocalDate(2025, 7, 1), new LocalDate(2025, 9, 30)), compiled.ComparedWith);
	}

	[Fact]
	public void A_ratio_by_quarter_for_one_branch_divides_totals()
	{
		var compiled = Compile(new(["ticket_promedio"], [], TimeGrain.Quarter, [new("sucursal", FilterOperator.In, ["Centro"])]));

		Assert.Equal(
			$"SELECT to_char(c_001, 'YYYY-\"Q\"Q') AS g1, round(sum(c_008)::numeric / NULLIF(count(DISTINCT c_002), 0), 2) AS m1 FROM {Sample.Table} WHERE c_003 = ANY(@p1) GROUP BY g1 ORDER BY g1 ASC LIMIT 1000",
			compiled.Sql);
		Assert.Equal(["Centro"], (string[])compiled.Parameters[0].Value);
	}

	[Fact]
	public void Units_stay_whole_and_excluded_values_go_through_all()
	{
		var compiled = Compile(new(["unidades"], ["categoria", "sucursal"], Filters: [new("categoria", FilterOperator.NotIn, ["Bebidas", "Lácteos"])], Sort: new("sucursal", SortDirection.Ascending)));

		Assert.Equal(
			$"SELECT c_004 AS g1, c_003 AS g2, sum(c_006)::bigint AS m1 FROM {Sample.Table} WHERE c_004 <> ALL(@p1) GROUP BY g1, g2 ORDER BY g2 ASC NULLS LAST, g1 ASC LIMIT 1000",
			compiled.Sql);
	}

	[Fact]
	public void The_result_columns_name_what_they_hold()
	{
		var compiled = Compile(new(["ventas", "facturas"], ["sucursal"], Period: new(NamedPeriod.LastMonth), CompareTo: Comparison.PreviousPeriod));

		Assert.Equal(
			["sucursal:Dimension", "ventas:Metric", "ventas:PreviousMetric", "facturas:Metric", "facturas:PreviousMetric"],
			compiled.Columns.Select(static c => $"{c.Key}:{c.Kind}"));
	}

	[Fact]
	public void An_invalid_query_is_never_compiled() =>
		Assert.Throws<ArgumentException>(() => Compile(new(["margen"], [])));

	private static CompiledQuery Compile(SemanticQuery query) => SqlCompiler.Compile(Sample.Dataset, Sample.Layer, query, Today);
}
