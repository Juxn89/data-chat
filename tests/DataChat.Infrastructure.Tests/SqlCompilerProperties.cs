namespace DataChat.Infrastructure.Tests;

using System.Text.RegularExpressions;
using DataChat.Domain.Periods;
using DataChat.Domain.Semantics;
using DataChat.Infrastructure.Querying;
using FsCheck;
using FsCheck.Fluent;
using NodaTime;

/// <summary>
/// What holds for every valid query, not only the ones someone thought of (plan §8): generated queries, filter values
/// full of SQL and quotes included, compile to SQL whose identifiers all come from an allow-list and whose text does
/// not depend on any value.
/// </summary>
public sealed partial class SqlCompilerProperties
{
	private const int Runs = 500;

	private static readonly LocalDate Today = new(2026, 10, 15);

	/// <summary>Values people and attackers write: the generator mixes these with random text.</summary>
	private static readonly string[] Nasty =
	[
		"Centro", "Lácteos", "'; DROP TABLE datasets; --", "\" OR 1=1 --", "x') UNION SELECT * FROM pg_user --",
		"$$; SELECT pg_sleep(10); $$", "c_001", "ds_00000000000000000000000000000000", "@p1", "\\", "%", "ñandú",
	];

	/// <summary>Every word the SQL may contain outside its single string literal, the time format.</summary>
	private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
	{
		"SELECT", "FROM", "WHERE", "AND", "OR", "GROUP", "BY", "ORDER", "ASC", "DESC", "NULLS", "LAST", "LIMIT", "AS",
		"BETWEEN", "ANY", "ALL", "FILTER", "DISTINCT", "round", "sum", "avg", "min", "max", "count", "to_char", "NULLIF",
		"numeric", "bigint",
	};

	private static readonly string[] TimeFormats = ["'YYYY-MM-DD'", "'YYYY-MM'", "'YYYY-\"Q\"Q'", "'YYYY'"];

	private static readonly Gen<SemanticQuery> Queries = QueryGenerator();

	[Fact]
	public void Every_identifier_comes_from_the_allow_list() =>
		Check(query =>
		{
			var sql = SqlCompiler.Compile(Sample.Dataset, Sample.Layer, query, Today).Sql;
			var literals = Literal().Matches(sql).Select(static m => m.Value).ToArray();
			var words = Word().Matches(Literal().Replace(sql, string.Empty)).Select(static m => m.Value);
			return literals.All(TimeFormats.Contains) && literals.Length <= 1 && words.All(IsAllowed);
		});

	[Fact]
	public void The_sql_text_does_not_depend_on_any_value() =>
		Check(query =>
		{
			var blank = query with { Filters = query.FilterList.Select(static f => f with { Values = f.Values.Select(static _ => "x").ToArray() }).ToArray() };
			var original = SqlCompiler.Compile(Sample.Dataset, Sample.Layer, query, Today);
			var replaced = SqlCompiler.Compile(Sample.Dataset, Sample.Layer, blank, Today);
			return original.Sql == replaced.Sql && original.Parameters.Count == replaced.Parameters.Count;
		});

	[Fact]
	public void Every_query_ends_with_a_limit_no_larger_than_the_maximum() =>
		Check(query =>
		{
			var match = TrailingLimit().Match(SqlCompiler.Compile(Sample.Dataset, Sample.Layer, query, Today).Sql);
			return match.Success && int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) is >= 1 and <= SemanticQuery.MaxRows;
		});

	[Fact]
	public void The_generator_produces_only_valid_queries() =>
		Check(query => query.Validate(Sample.Layer).Count == 0);

	private static void Check(Func<SemanticQuery, bool> property) =>
		Prop.ForAll(Arb.From(Queries), property).Check(Config.QuickThrowOnFailure.WithMaxTest(Runs));

	private static bool IsAllowed(string word) =>
		Keywords.Contains(word)
		|| word == Sample.Table
		|| Sample.Dataset.Columns.Any(column => column.PhysicalName == word)
		|| Alias().IsMatch(word);

	private static Gen<SemanticQuery> QueryGenerator()
	{
		var metricKeys = Sample.Layer.Metrics.Select(static m => m.Key).ToArray();
		var dimensionKeys = Sample.Layer.Dimensions.Select(static d => d.Key).ToArray();
		var value = Gen.OneOf(Gen.Elements(Nasty), ArbMap.Default.GeneratorFor<string>().Where(static s => !string.IsNullOrWhiteSpace(s) && s.Length <= SemanticQuery.MaxValueLength));
		var filter =
			from dimension in Gen.Elements(dimensionKeys)
			from op in Gen.Elements(FilterOperator.In, FilterOperator.NotIn)
			from values in Gen.NonEmptyListOf(value).Select(static v => v.Take(SemanticQuery.MaxFilterValues).ToArray())
			select new Filter(dimension, op, values);
		var date = Gen.Choose(0, 600).Select(static days => new LocalDate(2025, 4, 1).PlusDays(days));
		var period = Gen.OneOf(
			Gen.Elements(Enum.GetValues<NamedPeriod>()).Select(static named => new PeriodSpec(named)),
			from a in date from b in date select new PeriodSpec(From: a < b ? a : b, To: a < b ? b : a));

		return
			from metrics in Gen.Shuffle(metricKeys).Select(static k => k.Take(4)).SelectMany(static k => Gen.Choose(1, k.Count()).Select(n => k.Take(n).ToArray()))
			from grain in Gen.OneOf(Gen.Constant((TimeGrain?)null), Gen.Elements(Enum.GetValues<TimeGrain>()).Select(static g => (TimeGrain?)g))
			from dimensions in Gen.Shuffle(dimensionKeys).SelectMany(keys => Gen.Choose(0, grain is null ? 2 : 1).Select(n => keys.Take(n).ToArray()))
			from filters in Gen.ListOf(filter).Select(static f => f.Take(SemanticQuery.MaxFilters).ToArray())
			from withPeriod in Gen.Elements(true, false)
			from spec in period
			from compare in Gen.Elements((Comparison?)null, Comparison.PreviousPeriod, Comparison.SamePeriodLastYear)
			from sortIndex in Gen.Choose(-1, metrics.Length + dimensions.Length)
			from direction in Gen.Elements(SortDirection.Ascending, SortDirection.Descending)
			from limit in Gen.OneOf(Gen.Constant((int?)null), Gen.Choose(1, SemanticQuery.MaxRows).Select(static l => (int?)l))
			let sortKeys = metrics.Concat(dimensions).Concat(grain is null ? [] : [SemanticQuery.TimeKey]).ToArray()
			select new SemanticQuery(
				metrics,
				dimensions,
				grain,
				filters,
				withPeriod ? spec : null,
				withPeriod && grain is null ? compare : null,
				sortIndex >= 0 && sortIndex < sortKeys.Length ? new Sort(sortKeys[sortIndex], direction) : null,
				limit);
	}

	[GeneratedRegex("'[^']*'")]
	private static partial Regex Literal();

	[GeneratedRegex("[A-Za-z_][A-Za-z0-9_]*")]
	private static partial Regex Word();

	// Result aliases (g1, m2, m2_previous) and parameter names (p7, after the @).
	[GeneratedRegex("^(g[1-2]|m[1-4](_previous)?|p[0-9]+)$")]
	private static partial Regex Alias();

	[GeneratedRegex(@"LIMIT ([0-9]+)$")]
	private static partial Regex TrailingLimit();
}
