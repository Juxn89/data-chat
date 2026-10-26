namespace DataChat.Domain.Semantics;

using System.Text.RegularExpressions;
using DataChat.Domain.Datasets;
using NodaTime;

/// <summary>How a metric folds many rows into one number.</summary>
public enum Aggregation
{
	/// <summary>Adds the values: sales, units.</summary>
	Sum,

	/// <summary>The mean of the values: a unit price.</summary>
	Average,

	/// <summary>The smallest value.</summary>
	Min,

	/// <summary>The largest value.</summary>
	Max,

	/// <summary>How many rows; needs no column.</summary>
	Count,

	/// <summary>How many different values: invoices among invoice lines.</summary>
	CountDistinct,
}

/// <summary>How a metric is computed.</summary>
public abstract record MetricFormula;

/// <summary>An aggregation over one column (none for <see cref="Aggregation.Count"/>).</summary>
/// <param name="Aggregation">The aggregation.</param>
/// <param name="Column">The physical column, e.g. <c>c_008</c>.</param>
public sealed record Aggregate(Aggregation Aggregation, string? Column) : MetricFormula;

/// <summary>One aggregate metric divided by another: average ticket is sales over invoices.</summary>
/// <param name="Numerator">The numerator's metric key.</param>
/// <param name="Denominator">The denominator's metric key.</param>
public sealed record Ratio(string Numerator, string Denominator) : MetricFormula;

/// <summary>A number people ask about, with the name they use for it.</summary>
/// <param name="Key">Stable and plain (<c>ventas</c>): what the model names in a query.</param>
/// <param name="Name">What people call it (<c>Ventas</c>).</param>
/// <param name="Formula">How it is computed.</param>
public sealed record Metric(string Key, string Name, MetricFormula Formula);

/// <summary>Something people group or filter by.</summary>
/// <param name="Key">Stable and plain (<c>sucursal</c>).</param>
/// <param name="Name">What people call it (<c>Sucursal</c>).</param>
/// <param name="Column">The physical column.</param>
public sealed record Dimension(string Key, string Name, string Column);

/// <summary>A problem with a semantic layer or a query, with a stable code.</summary>
/// <param name="Code">E.g. <c>semantic.unknown_column</c>.</param>
/// <param name="Message">For a person.</param>
public sealed record SemanticIssue(string Code, string Message);

/// <summary>
/// A dataset in business terms (plan §2): which column is the date, which numbers are metrics and how they add up,
/// which columns are dimensions. The model only ever sees these names and keys; the physical columns stay with the
/// compiler (ADR 0001).
/// </summary>
/// <param name="TimeColumn">The date column periods and time grains use, or null when the data has none.</param>
/// <param name="Metrics">The metrics.</param>
/// <param name="Dimensions">The dimensions.</param>
/// <param name="AsOf">
/// The "today" that named periods resolve against (ADR 0005); null for the real today. A file exported some time ago
/// sets it to the export's date, so "last quarter" means the last quarter in the data.
/// </param>
public sealed partial record SemanticLayer(string? TimeColumn, IReadOnlyList<Metric> Metrics, IReadOnlyList<Dimension> Dimensions, LocalDate? AsOf = null)
{
	/// <summary>The most metrics or dimensions a layer has; each becomes an enum value in the model's schema.</summary>
	public const int MaxEntries = 30;

	/// <summary>The longest name.</summary>
	public const int MaxNameLength = Sheet.MaxNameLength;

	/// <summary>The metric with a key.</summary>
	/// <param name="key">The key.</param>
	/// <returns>The metric, or null.</returns>
	public Metric? Metric(string key) => Metrics.FirstOrDefault(metric => metric.Key == key);

	/// <summary>The dimension with a key.</summary>
	/// <param name="key">The key.</param>
	/// <returns>The dimension, or null.</returns>
	public Dimension? Dimension(string key) => Dimensions.FirstOrDefault(dimension => dimension.Key == key);

	/// <summary>Everything wrong with this layer for a dataset; empty when it is valid.</summary>
	/// <param name="dataset">The dataset.</param>
	/// <returns>The issues.</returns>
	public IReadOnlyList<SemanticIssue> Validate(Dataset dataset)
	{
		ArgumentNullException.ThrowIfNull(dataset);
		var issues = new List<SemanticIssue>();
		if (Metrics.Count == 0)
		{
			issues.Add(new("semantic.no_metrics", "A dataset needs at least one metric to answer anything."));
		}

		if (Metrics.Count > MaxEntries || Dimensions.Count > MaxEntries)
		{
			issues.Add(new("semantic.too_many", $"At most {MaxEntries} metrics and {MaxEntries} dimensions."));
		}

		var keys = Metrics.Select(static m => m.Key).Concat(Dimensions.Select(static d => d.Key)).ToArray();
		issues.AddRange(keys.Where(static key => !KeyPattern().IsMatch(key))
			.Select(static key => new SemanticIssue("semantic.invalid_key", $"'{key}' is not a valid key: lowercase letters, digits and _ only.")));
		issues.AddRange(keys.GroupBy(static key => key).Where(static g => g.Count() > 1)
			.Select(static g => new SemanticIssue("semantic.duplicate_key", $"The key '{g.Key}' is used twice.")));

		var names = Metrics.Select(static m => m.Name).Concat(Dimensions.Select(static d => d.Name)).ToArray();
		issues.AddRange(names.Where(static name => string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
			.Select(static name => new SemanticIssue("semantic.invalid_name", $"Names have 1 to {MaxNameLength} characters: '{name}'.")));
		issues.AddRange(names.GroupBy(static name => name.Trim(), StringComparer.OrdinalIgnoreCase).Where(static g => g.Count() > 1)
			.Select(static g => new SemanticIssue("semantic.duplicate_name", $"The name '{g.Key}' is used twice.")));

		if (TimeColumn is not null && dataset.Column(TimeColumn)?.Type != ColumnType.Date)
		{
			issues.Add(new("semantic.invalid_time_column", "The time column must be one of the dataset's date columns."));
		}

		foreach (var metric in Metrics)
		{
			issues.AddRange(ValidateMetric(metric, dataset));
		}

		foreach (var dimension in Dimensions)
		{
			if (dataset.Column(dimension.Column) is not { } column)
			{
				issues.Add(UnknownColumn(dimension.Name));
			}
			else if (column.Type is not (ColumnType.Text or ColumnType.Integer))
			{
				issues.Add(new("semantic.invalid_dimension", $"'{dimension.Name}' groups by a {column.Type} column; dimensions are text or whole numbers (dates go through the time column)."));
			}
		}

		return issues;
	}

	private IEnumerable<SemanticIssue> ValidateMetric(Metric metric, Dataset dataset)
	{
		switch (metric.Formula)
		{
			case Aggregate { Aggregation: Aggregation.Count, Column: null }:
				yield break;
			case Aggregate { Column: null }:
				yield return new("semantic.missing_column", $"'{metric.Name}' needs a column to aggregate.");
				yield break;
			case Aggregate aggregate when dataset.Column(aggregate.Column) is not { } column:
				yield return UnknownColumn(metric.Name);
				yield break;
			case Aggregate aggregate:
				var type = dataset.Column(aggregate.Column!)!.Type;
				var fits = aggregate.Aggregation switch
				{
					Aggregation.Sum or Aggregation.Average => type is ColumnType.Integer or ColumnType.Decimal,
					Aggregation.Min or Aggregation.Max => type is ColumnType.Integer or ColumnType.Decimal or ColumnType.Date,
					_ => true,
				};
				if (!fits)
				{
					yield return new("semantic.invalid_aggregation", $"'{metric.Name}': {aggregate.Aggregation} does not apply to a {type} column.");
				}

				yield break;
			case Ratio ratio:
				foreach (var part in new[] { ratio.Numerator, ratio.Denominator })
				{
					if (Metric(part)?.Formula is not Aggregate)
					{
						yield return new("semantic.invalid_ratio", $"'{metric.Name}' divides '{part}', which is not an aggregate metric of this layer.");
					}
				}

				if (ratio.Numerator == ratio.Denominator)
				{
					yield return new("semantic.invalid_ratio", $"'{metric.Name}' divides a metric by itself.");
				}

				yield break;
			default:
				yield return new("semantic.invalid_formula", $"'{metric.Name}' has no formula.");
				yield break;
		}
	}

	private static SemanticIssue UnknownColumn(string name) =>
		new("semantic.unknown_column", $"'{name}' refers to a column this dataset does not have.");

	[GeneratedRegex("^[a-z][a-z0-9_]{0,39}$")]
	private static partial Regex KeyPattern();
}
