namespace DataChat.Domain.Semantics;

using System.Text.Json.Serialization;
using DataChat.Domain.Periods;
using NodaTime;

/// <summary>How finely time is grouped.</summary>
public enum TimeGrain
{
	/// <summary>One row per day.</summary>
	Day,

	/// <summary>One row per month.</summary>
	Month,

	/// <summary>One row per quarter.</summary>
	Quarter,

	/// <summary>One row per year.</summary>
	Year,
}

/// <summary>Whether a filter keeps or drops the listed values.</summary>
public enum FilterOperator
{
	/// <summary>Only these values.</summary>
	In,

	/// <summary>Everything but these values.</summary>
	NotIn,
}

/// <summary>Sort order.</summary>
public enum SortDirection
{
	/// <summary>Smallest first.</summary>
	Ascending,

	/// <summary>Largest first.</summary>
	Descending,
}

/// <summary>Keeps or drops rows by a dimension's values.</summary>
/// <param name="Dimension">The dimension's key.</param>
/// <param name="Operator">Keep or drop.</param>
/// <param name="Values">The values, as they appear in the data.</param>
public sealed record Filter(string Dimension, FilterOperator Operator, IReadOnlyList<string> Values);

/// <summary>The dates a query covers: a named period, or two dates, both included.</summary>
/// <param name="Named">A named period, resolved by the server (ADR 0005).</param>
/// <param name="From">The first date.</param>
/// <param name="To">The last date.</param>
public sealed record PeriodSpec(NamedPeriod? Named = null, LocalDate? From = null, LocalDate? To = null)
{
	/// <summary>The dates, against the dataset's "today".</summary>
	/// <param name="today">Today.</param>
	/// <returns>The dates.</returns>
	public DateInterval Resolve(LocalDate today) =>
		Named is { } named ? PeriodCalendar.Resolve(named, today) : new DateInterval(From!.Value, To!.Value);
}

/// <summary>What the rows are sorted by.</summary>
/// <param name="By">A metric's or a dimension's key, or <see cref="SemanticQuery.TimeKey"/>.</param>
/// <param name="Direction">The direction.</param>
public sealed record Sort(string By, SortDirection Direction);

/// <summary>
/// A question in the semantic layer's terms (plan §5): what the model returns instead of SQL. It names metrics and
/// dimensions by key; only the compiler turns it into SQL, from the layer's physical columns.
/// </summary>
/// <param name="Metrics">The metrics, by key.</param>
/// <param name="Dimensions">The dimensions to group by, by key.</param>
/// <param name="Grain">The time grain to group by, if any.</param>
/// <param name="Filters">The filters.</param>
/// <param name="Period">The dates, if any.</param>
/// <param name="CompareTo">What the period is compared with, if anything.</param>
/// <param name="Sort">The order, if any.</param>
/// <param name="Limit">The most rows, if limited.</param>
public sealed record SemanticQuery(
	IReadOnlyList<string> Metrics,
	IReadOnlyList<string> Dimensions,
	TimeGrain? Grain = null,
	IReadOnlyList<Filter>? Filters = null,
	PeriodSpec? Period = null,
	Comparison? CompareTo = null,
	Sort? Sort = null,
	int? Limit = null)
{
	/// <summary>The key that sorts by the time grain.</summary>
	public const string TimeKey = "time";

	/// <summary>The most metrics in one question.</summary>
	public const int MaxMetrics = 4;

	/// <summary>The most columns to group by, time included: two is what a chart or a pivot can show.</summary>
	public const int MaxGroups = 2;

	/// <summary>The most filters.</summary>
	public const int MaxFilters = 5;

	/// <summary>The most values in one filter.</summary>
	public const int MaxFilterValues = 50;

	/// <summary>The longest filter value.</summary>
	public const int MaxValueLength = 200;

	/// <summary>The most rows a question returns; the compiler enforces it whatever the query says.</summary>
	public const int MaxRows = 1000;

	/// <summary>The filters, never null. A convenience over <see cref="Filters"/>, not part of the query's shape.</summary>
	[JsonIgnore]
	public IReadOnlyList<Filter> FilterList => Filters ?? [];

	/// <summary>
	/// The same question without groupings that cannot vary: a dimension kept to one value by a filter ("Snacks by
	/// month") would only add a column repeating that value. A sort by it goes too.
	/// </summary>
	/// <returns>The simplified query.</returns>
	public SemanticQuery WithoutConstantGroups()
	{
		var constant = FilterList
			.Where(static filter => filter.Operator == FilterOperator.In && filter.Values.Distinct(StringComparer.Ordinal).Count() == 1)
			.Select(static filter => filter.Dimension)
			.ToHashSet(StringComparer.Ordinal);
		if (!Dimensions.Any(constant.Contains))
		{
			return this;
		}

		return this with
		{
			Dimensions = Dimensions.Where(dimension => !constant.Contains(dimension)).ToArray(),
			Sort = Sort is { } sort && constant.Contains(sort.By) ? null : Sort,
		};
	}

	/// <summary>Everything wrong with this query for a layer; empty when it is valid.</summary>
	/// <param name="layer">The dataset's semantic layer.</param>
	/// <returns>The issues.</returns>
	public IReadOnlyList<SemanticIssue> Validate(SemanticLayer layer)
	{
		ArgumentNullException.ThrowIfNull(layer);
		var issues = new List<SemanticIssue>();

		if (Metrics.Count is 0 or > MaxMetrics)
		{
			issues.Add(new("query.metrics", $"A question asks for 1 to {MaxMetrics} metrics."));
		}

		issues.AddRange(Metrics.Where(key => layer.Metric(key) is null).Select(static key => new SemanticIssue("query.unknown_metric", $"There is no metric '{key}'.")));
		issues.AddRange(Dimensions.Where(key => layer.Dimension(key) is null).Select(static key => new SemanticIssue("query.unknown_dimension", $"There is no dimension '{key}'.")));
		if (Metrics.Distinct().Count() != Metrics.Count || Dimensions.Distinct().Count() != Dimensions.Count)
		{
			issues.Add(new("query.repeated", "A metric or a dimension is listed twice."));
		}

		if (Dimensions.Count + (Grain is null ? 0 : 1) > MaxGroups)
		{
			issues.Add(new("query.too_many_groups", $"A question groups by at most {MaxGroups} things, time included."));
		}

		if ((Grain is not null || Period is not null) && layer.TimeColumn is null)
		{
			issues.Add(new("query.no_time", "This dataset has no date column, so it cannot be grouped or filtered by time."));
		}

		issues.AddRange(ValidateFilters(layer));
		issues.AddRange(ValidatePeriod());

		if (Sort is { } sort && !Metrics.Contains(sort.By) && !Dimensions.Contains(sort.By) && !(sort.By == TimeKey && Grain is not null))
		{
			issues.Add(new("query.invalid_sort", $"'{sort.By}' is not one of the question's metrics, dimensions or its time grain."));
		}

		if (Limit is < 1 or > MaxRows)
		{
			issues.Add(new("query.invalid_limit", $"A limit is between 1 and {MaxRows}."));
		}

		return issues;
	}

	private IEnumerable<SemanticIssue> ValidateFilters(SemanticLayer layer)
	{
		if (FilterList.Count > MaxFilters)
		{
			yield return new("query.too_many_filters", $"At most {MaxFilters} filters.");
		}

		foreach (var filter in FilterList)
		{
			if (layer.Dimension(filter.Dimension) is null)
			{
				yield return new("query.unknown_dimension", $"There is no dimension '{filter.Dimension}' to filter by.");
			}

			if (filter.Values.Count is 0 or > MaxFilterValues
				|| filter.Values.Any(static value => string.IsNullOrWhiteSpace(value) || value.Length > MaxValueLength))
			{
				yield return new("query.invalid_filter", $"A filter has 1 to {MaxFilterValues} values of up to {MaxValueLength} characters.");
			}
		}
	}

	private IEnumerable<SemanticIssue> ValidatePeriod()
	{
		if (Period is { } period)
		{
			var named = period.Named is not null;
			var dated = period.From is not null || period.To is not null;
			if (named == dated || (dated && (period.From is null || period.To is null || period.From > period.To)))
			{
				yield return new("query.invalid_period", "A period is either a named one or two dates, the first not after the second.");
			}
		}

		if (CompareTo is not null && Period is null)
		{
			yield return new("query.compare_without_period", "A comparison needs a period to compare.");
		}

		if (CompareTo is not null && Grain is not null)
		{
			yield return new("query.compare_with_grain", "A comparison is of totals, or by dimension; not along a time grain.");
		}
	}
}
