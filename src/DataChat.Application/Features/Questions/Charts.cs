namespace DataChat.Application.Features.Questions;

/// <summary>How an answer is drawn.</summary>
public enum ChartKind
{
	/// <summary>One figure per metric, with its comparison when there is one.</summary>
	Kpi,

	/// <summary>A line over time; one line per metric, or per value of the dimension.</summary>
	Line,

	/// <summary>Bars per value of the dimension; side by side with the compared period.</summary>
	Bar,

	/// <summary>A table with one dimension down and the other across.</summary>
	Pivot,

	/// <summary>The rows as they are.</summary>
	Table,
}

/// <summary>
/// Picks the chart from the shape of the answer, never from the model (ADR 0004): the same question always draws the
/// same way, and a chart cannot misrepresent rows it was not built for.
/// </summary>
public static class Charts
{
	/// <summary>More lines than this on one chart cannot be told apart; the answer goes to a table instead.</summary>
	public const int MaxSeries = 8;

	/// <summary>More bars than this read better as a table.</summary>
	public const int MaxBars = 30;

	/// <summary>The chart for an answer.</summary>
	/// <param name="columns">Its columns.</param>
	/// <param name="rows">Its rows.</param>
	/// <returns>The chart.</returns>
	public static ChartKind Choose(IReadOnlyList<ResultColumn> columns, IReadOnlyList<IReadOnlyList<object?>> rows)
	{
		ArgumentNullException.ThrowIfNull(columns);
		ArgumentNullException.ThrowIfNull(rows);
		var time = columns.Any(static c => c.Kind == ResultColumnKind.Time);
		var dimensions = columns.Select(static (c, i) => (c, i)).Where(static x => x.c.Kind == ResultColumnKind.Dimension).Select(static x => x.i).ToArray();

		return (rows.Count, time, dimensions.Length) switch
		{
			(0, _, _) => ChartKind.Table,
			(_, false, 0) => ChartKind.Kpi,
			(1, true, 0) => ChartKind.Kpi,
			(_, true, 0) => ChartKind.Line,
			(_, true, 1) when rows.Select(row => row[dimensions[0]]).Distinct().Count() <= MaxSeries => ChartKind.Line,
			( <= MaxBars, false, 1) => ChartKind.Bar,
			(_, false, 2) => ChartKind.Pivot,
			_ => ChartKind.Table,
		};
	}
}
