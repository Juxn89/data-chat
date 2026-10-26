namespace DataChat.Infrastructure.Querying;

using System.Globalization;
using System.Text;
using DataChat.Application.Features.Questions;
using DataChat.Domain.Datasets;
using DataChat.Domain.Periods;
using DataChat.Domain.Semantics;
using NodaTime;

/// <summary>The Postgres type a parameter is sent as.</summary>
internal enum ParameterType
{
	/// <summary><c>date</c>.</summary>
	Date,

	/// <summary><c>text[]</c>.</summary>
	TextArray,

	/// <summary><c>bigint[]</c>.</summary>
	BigintArray,
}

/// <summary>A parameter of a compiled query.</summary>
/// <param name="Name">Its name, without the <c>@</c>.</param>
/// <param name="Value">A <see cref="DateOnly"/>, <c>string[]</c> or <c>long[]</c>.</param>
/// <param name="Type">How it is sent.</param>
internal sealed record SqlParameter(string Name, object Value, ParameterType Type);

/// <summary>SQL ready to run, its parameters, and what its columns are.</summary>
/// <param name="Sql">The SQL.</param>
/// <param name="Parameters">The parameters.</param>
/// <param name="Columns">The result's columns, in order.</param>
/// <param name="Limit">The row limit it carries.</param>
/// <param name="Period">The dates asked about.</param>
/// <param name="ComparedWith">The dates compared with.</param>
internal sealed record CompiledQuery(
	string Sql,
	IReadOnlyList<SqlParameter> Parameters,
	IReadOnlyList<ResultColumn> Columns,
	int Limit,
	DateInterval? Period,
	DateInterval? ComparedWith);

/// <summary>
/// The only place SQL is built from a question (ADR 0001). Every identifier comes from the dataset and its layer as
/// the domain named them (<c>ds_</c> plus an id, <c>c_001</c>…) and every value is a parameter, so nothing a person
/// or a spreadsheet wrote reaches the SQL text. Result columns are aliased by position (<c>g1</c>, <c>m1</c>…), not by
/// key, for the same reason.
/// </summary>
internal static class SqlCompiler
{
	private const int MoneyScale = 2;

	/// <summary>Compiles a query that is valid for the layer.</summary>
	/// <param name="dataset">The dataset.</param>
	/// <param name="layer">Its layer.</param>
	/// <param name="query">The query, already validated.</param>
	/// <param name="today">The "today" named periods resolve against.</param>
	/// <returns>The SQL and its parameters.</returns>
	/// <exception cref="ArgumentException">The query is not valid for the layer.</exception>
	public static CompiledQuery Compile(Dataset dataset, SemanticLayer layer, SemanticQuery query, LocalDate today)
	{
		ArgumentNullException.ThrowIfNull(dataset);
		ArgumentNullException.ThrowIfNull(layer);
		ArgumentNullException.ThrowIfNull(query);
		if (query.Validate(layer) is { Count: > 0 } issues)
		{
			throw new ArgumentException($"The query is not valid: {issues[0].Message}", nameof(query));
		}

		var parameters = new List<SqlParameter>();
		string Parameter(object value, ParameterType type)
		{
			var name = $"p{parameters.Count + 1}";
			parameters.Add(new SqlParameter(name, value, type));
			return $"@{name}";
		}

		var period = query.Period?.Resolve(today);
		var compared = period is not null && query.CompareTo is { } comparison ? PeriodCalendar.CompareWith(period, comparison) : null;

		var select = new List<string>();
		var columns = new List<ResultColumn>();
		var groupAliases = new List<string>();
		if (query.Grain is { } grain)
		{
			select.Add($"to_char({layer.TimeColumn}, '{TimeFormat(grain)}') AS g1");
			columns.Add(new ResultColumn(SemanticQuery.TimeKey, SemanticQuery.TimeKey, ResultColumnKind.Time));
			groupAliases.Add("g1");
		}

		foreach (var key in query.Dimensions)
		{
			var dimension = layer.Dimension(key)!;
			var alias = $"g{groupAliases.Count + 1}";
			select.Add($"{dimension.Column} AS {alias}");
			columns.Add(new ResultColumn(dimension.Key, dimension.Name, ResultColumnKind.Dimension));
			groupAliases.Add(alias);
		}

		string? currentFilter = null;
		string? previousFilter = null;
		if (compared is not null)
		{
			currentFilter = Between(layer.TimeColumn!, period!, Parameter);
			previousFilter = Between(layer.TimeColumn!, compared, Parameter);
		}

		var metricAliases = new Dictionary<string, string>(StringComparer.Ordinal);
		for (var i = 0; i < query.Metrics.Count; i++)
		{
			var metric = layer.Metric(query.Metrics[i])!;
			var alias = $"m{i + 1}";
			metricAliases[metric.Key] = alias;
			select.Add($"{Measure(metric, layer, dataset, currentFilter)} AS {alias}");
			columns.Add(new ResultColumn(metric.Key, metric.Name, ResultColumnKind.Metric));
			if (previousFilter is not null)
			{
				select.Add($"{Measure(metric, layer, dataset, previousFilter)} AS {alias}_previous");
				columns.Add(new ResultColumn(metric.Key, metric.Name, ResultColumnKind.PreviousMetric));
			}
		}

		var where = new List<string>();
		if (period is not null)
		{
			where.Add(compared is null ? Between(layer.TimeColumn!, period, Parameter) : $"({currentFilter} OR {previousFilter})");
		}

		foreach (var filter in query.FilterList)
		{
			var dimension = layer.Dimension(filter.Dimension)!;
			var integer = dataset.Column(dimension.Column)!.Type == ColumnType.Integer;
			var values = integer
				? Parameter(filter.Values.Select(static value => long.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)).ToArray(), ParameterType.BigintArray)
				: Parameter(filter.Values.ToArray(), ParameterType.TextArray);
			where.Add(filter.Operator == FilterOperator.In ? $"{dimension.Column} = ANY({values})" : $"{dimension.Column} <> ALL({values})");
		}

		var limit = Math.Min(query.Limit ?? SemanticQuery.MaxRows, SemanticQuery.MaxRows);
		var sql = new StringBuilder()
			.Append("SELECT ").AppendJoin(", ", select)
			.Append(" FROM ").Append(dataset.TableName);
		if (where.Count > 0)
		{
			sql.Append(" WHERE ").AppendJoin(" AND ", where);
		}

		if (groupAliases.Count > 0)
		{
			sql.Append(" GROUP BY ").AppendJoin(", ", groupAliases);
		}

		if (OrderBy(query, groupAliases, metricAliases) is { Count: > 0 } order)
		{
			sql.Append(" ORDER BY ").AppendJoin(", ", order);
		}

		sql.Append(" LIMIT ").Append(limit.ToString(CultureInfo.InvariantCulture));
		return new CompiledQuery(sql.ToString(), parameters, columns, limit, period, compared);
	}

	/// <summary>Validates that a filter value fits an integer dimension; the compiler parses it.</summary>
	/// <param name="value">The value.</param>
	/// <returns>Whether it is a whole number.</returns>
	public static bool IsWholeNumber(string value) =>
		long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

	private static string TimeFormat(TimeGrain grain) => grain switch
	{
		TimeGrain.Day => "YYYY-MM-DD",
		TimeGrain.Month => "YYYY-MM",
		TimeGrain.Quarter => "YYYY-\"Q\"Q",
		_ => "YYYY",
	};

	private static string Between(string column, DateInterval dates, Func<object, ParameterType, string> parameter) =>
		$"{column} BETWEEN {parameter(ToDateOnly(dates.Start), ParameterType.Date)} AND {parameter(ToDateOnly(dates.End), ParameterType.Date)}";

	private static DateOnly ToDateOnly(LocalDate date) => new(date.Year, date.Month, date.Day);

	/// <summary>
	/// A metric as people read it: money and averages rounded to cents, sums of whole numbers kept whole, and a ratio
	/// computed over totals (sales over invoices), never as an average of per-row ratios.
	/// </summary>
	private static string Measure(Metric metric, SemanticLayer layer, Dataset dataset, string? filter) => metric.Formula switch
	{
		Ratio ratio => $"round({Raw((Aggregate)layer.Metric(ratio.Numerator)!.Formula, filter)}::numeric / NULLIF({Raw((Aggregate)layer.Metric(ratio.Denominator)!.Formula, filter)}, 0), {MoneyScale})",
		Aggregate { Aggregation: Aggregation.Sum, Column: { } column } aggregate when dataset.Column(column)!.Type == ColumnType.Integer =>
			$"{Raw(aggregate, filter)}::bigint",
		Aggregate { Aggregation: Aggregation.Sum or Aggregation.Average } aggregate => $"round({Raw(aggregate, filter)}, {MoneyScale})",
		Aggregate aggregate => Raw(aggregate, filter),
		_ => throw new InvalidOperationException($"Metric {metric.Key} has an unknown formula."),
	};

	private static string Raw(Aggregate aggregate, string? filter)
	{
		var expression = aggregate.Aggregation switch
		{
			Aggregation.Sum => $"sum({aggregate.Column})",
			Aggregation.Average => $"avg({aggregate.Column})",
			Aggregation.Min => $"min({aggregate.Column})",
			Aggregation.Max => $"max({aggregate.Column})",
			Aggregation.Count => "count(*)",
			_ => $"count(DISTINCT {aggregate.Column})",
		};
		return filter is null ? expression : $"{expression} FILTER (WHERE {filter})";
	}

	/// <summary>
	/// The sort asked for, then every grouping in order so the rows always come out the same way. Without a sort, time
	/// runs forward, and groups without time come largest first by the first metric.
	/// </summary>
	private static List<string> OrderBy(SemanticQuery query, List<string> groupAliases, Dictionary<string, string> metricAliases)
	{
		var order = new List<string>();
		string? primary = null;
		if (query.Sort is { } sort)
		{
			primary = sort.By == SemanticQuery.TimeKey
				? "g1"
				: metricAliases.TryGetValue(sort.By, out var metric) ? metric : groupAliases[query.Dimensions.ToList().IndexOf(sort.By) + (query.Grain is null ? 0 : 1)];
			order.Add($"{primary} {(sort.Direction == SortDirection.Descending ? "DESC NULLS LAST" : "ASC NULLS LAST")}");
		}
		else if (query.Grain is null && groupAliases.Count > 0)
		{
			primary = "m1";
			order.Add("m1 DESC NULLS LAST");
		}

		order.AddRange(groupAliases.Where(alias => alias != primary).Select(static alias => $"{alias} ASC"));
		return order;
	}
}
