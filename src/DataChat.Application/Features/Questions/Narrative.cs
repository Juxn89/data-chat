namespace DataChat.Application.Features.Questions;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>What the narrator is given: the question, how it was read, and the rows with the figures derived from them.</summary>
/// <param name="Question">The question.</param>
/// <param name="Interpretation">How it was read.</param>
/// <param name="Facts">The rows and their totals and changes, as text.</param>
public sealed record NarrationRequest(string Question, string Interpretation, string Facts);

/// <summary>A short summary of an answer, and what it cost.</summary>
/// <param name="Text">The summary, or null when there is none.</param>
/// <param name="Usage">What it cost.</param>
public sealed record Narration(string? Text, TranslationUsage Usage);

/// <summary>Summarizes an answer in a sentence or two.</summary>
public interface INarrator
{
	/// <summary>Whether it can narrate at all.</summary>
	bool IsConfigured { get; }

	/// <summary>Summarizes.</summary>
	/// <param name="request">The answer to summarize.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The summary.</returns>
	Task<Narration> NarrateAsync(NarrationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The figures a narrative may quote (plan §5, "narrativa verificada"): every number in the rows, the totals of each
/// metric column, each row's share of that total, and, for comparisons, each row's change and percentage change.
/// The narrator is given them already computed, so it never has to do arithmetic; anything else is not a fact.
/// </summary>
public sealed partial class NarrationFacts
{
	/// <summary>Past this many rows an answer is a table to read, not something to summarize.</summary>
	public const int MaxRows = 50;

	/// <summary>Numbers this small are words ("los 3", "two branches") more than figures; they are never checked.</summary>
	private const decimal SmallNumber = 10m;

	private const int PercentScale = 1;

	private readonly List<decimal> _allowed = [];

	private NarrationFacts(string text) => Text = text;

	/// <summary>The facts as the narrator reads them.</summary>
	public string Text { get; private set; }

	/// <summary>Whether any metric has a value: a period with no sales has nothing to summarize, the empty table says it.</summary>
	/// <param name="answer">The answer.</param>
	/// <returns>Whether there is a figure to talk about.</returns>
	public static bool HasFigures(QueryAnswer answer)
	{
		ArgumentNullException.ThrowIfNull(answer);
		var metrics = answer.Columns.Select(static (column, i) => (column, i)).Where(static c => c.column.Kind is ResultColumnKind.Metric or ResultColumnKind.PreviousMetric).Select(static c => c.i).ToArray();
		return answer.Rows.Any(row => metrics.Any(i => row[i] is not null));
	}

	/// <summary>The facts of an answer.</summary>
	/// <param name="question">The question, whose own numbers ("los 5", "2026") may be repeated.</param>
	/// <param name="answer">The answer.</param>
	/// <param name="limit">The number of rows the question asked for (a top N), so a ranking is not read as everything.</param>
	/// <returns>The facts.</returns>
	public static NarrationFacts Of(string question, QueryAnswer answer, int? limit = null)
	{
		ArgumentNullException.ThrowIfNull(question);
		ArgumentNullException.ThrowIfNull(answer);
		var facts = new NarrationFacts(string.Empty);
		var text = new StringBuilder();
		var columns = answer.Columns;

		text.Append("Columns: ").AppendJoin(" | ", columns.Select(static c => c.Kind == ResultColumnKind.PreviousMetric ? $"{c.Name} (compared period)" : c.Name)).Append('\n');
		if (answer.Period is { } period)
		{
			text.Append(CultureInfo.InvariantCulture, $"Period: {period.Start:uuuu-MM-dd} to {period.End:uuuu-MM-dd}\n");
			facts.AllowDates(period);
		}

		if (answer.ComparedWith is { } compared)
		{
			text.Append(CultureInfo.InvariantCulture, $"Compared with: {compared.Start:uuuu-MM-dd} to {compared.End:uuuu-MM-dd}\n");
			facts.AllowDates(compared);
		}

		if (limit is { } top)
		{
			text.Append(CultureInfo.InvariantCulture, $"These are only the first {top} of a ranking: other values exist and are not shown, so the totals and shares below cover these rows only.\n");
		}

		text.Append("Rows:\n");
		foreach (var row in answer.Rows)
		{
			text.Append("- ").AppendJoin(" | ", row.Select(static cell => Show(cell))).Append('\n');
			foreach (var cell in row)
			{
				facts.AllowNumbersIn(cell);
			}

			for (var c = 0; c + 1 < columns.Count; c++)
			{
				if (columns[c].Kind == ResultColumnKind.Metric && columns[c + 1].Kind == ResultColumnKind.PreviousMetric
					&& row[c] is decimal or long && row[c + 1] is decimal or long)
				{
					var current = Convert.ToDecimal(row[c], CultureInfo.InvariantCulture);
					var previous = Convert.ToDecimal(row[c + 1], CultureInfo.InvariantCulture);
					var change = current - previous;

					// "cayó 2.982,77" states a fall as a positive amount: both signs are facts.
					facts._allowed.Add(change);
					facts._allowed.Add(Math.Abs(change));
					text.Append(CultureInfo.InvariantCulture, $"  change in {columns[c].Name}: {Show(change)}");
					if (previous != 0)
					{
						var percent = decimal.Round(change / previous * 100m, PercentScale);
						facts._allowed.Add(percent);
						facts._allowed.Add(Math.Abs(percent));
						text.Append(CultureInfo.InvariantCulture, $" ({Show(percent)} %)");
					}

					text.Append('\n');
				}
			}
		}

		if (answer.Rows.Count > 1)
		{
			for (var c = 0; c < columns.Count; c++)
			{
				var values = answer.Rows.Select(row => row[c]).OfType<object>().Where(static v => v is decimal or long).Select(static v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)).ToArray();
				if (columns[c].Kind is ResultColumnKind.Metric or ResultColumnKind.PreviousMetric && values.Length == answer.Rows.Count)
				{
					var total = values.Sum();
					facts._allowed.Add(total);
					text.Append(CultureInfo.InvariantCulture, $"Total of {columns[c].Name}: {Show(total)}");
					if (total != 0 && columns[c].Kind == ResultColumnKind.Metric)
					{
						var shares = values.Select(v => decimal.Round(v / total * 100m, PercentScale)).ToArray();
						facts._allowed.AddRange(shares);
						text.Append(" (shares by row, %: ").AppendJoin(", ", shares.Select(static s => Show(s))).Append(')');
					}

					text.Append('\n');
				}
			}
		}

		facts.AllowNumbersIn(question);
		facts.Text = text.ToString().TrimEnd();
		return facts;
	}

	/// <summary>
	/// Whether every number in a narrative is one of the facts, at the precision it is written with: "12.078" or
	/// "12,078" for 12078.19 reads as 12078 and matches; "12.100" does not. Numbers up to ten are words and pass.
	/// </summary>
	/// <param name="narrative">The narrative.</param>
	/// <returns>The numbers that are not facts; empty when the narrative is faithful.</returns>
	public IReadOnlyList<string> Unsupported(string narrative)
	{
		ArgumentNullException.ThrowIfNull(narrative);
		return Number().Matches(narrative)
			.Select(static m => m.Value)
			.Where(token => !Readings(token).Any(reading => IsWord(token, reading) || _allowed.Any(fact => decimal.Round(fact, reading.Scale, MidpointRounding.AwayFromZero) == reading.Value)))
			.ToArray();
	}

	/// <summary>A whole number up to ten, written without separators: "los 3", "2 sucursales".</summary>
	private static bool IsWord(string token, (decimal Value, int Scale) reading) =>
		reading.Scale == 0 && token.All(char.IsAsciiDigit) && reading.Value <= SmallNumber;

	/// <summary>The values a written number can mean: with a point or a comma as the decimal mark, and its decimals.</summary>
	private static IEnumerable<(decimal Value, int Scale)> Readings(string token)
	{
		foreach (var (group, mark) in new[] { (",", "."), (".", ",") })
		{
			var lastMark = token.LastIndexOf(mark, StringComparison.Ordinal);
			var integer = lastMark < 0 ? token : token[..lastMark];
			var fraction = lastMark < 0 ? string.Empty : token[(lastMark + 1)..];
			if (integer.Contains(mark, StringComparison.Ordinal) || fraction.Contains(group, StringComparison.Ordinal))
			{
				continue;
			}

			var digits = integer.Replace(group, string.Empty, StringComparison.Ordinal) + (fraction.Length > 0 ? "." + fraction : string.Empty);
			if (decimal.TryParse(digits, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
			{
				yield return (value, fraction.Length);
			}
		}
	}

	/// <summary>"Del 1 de julio al 30 de septiembre de 2026": a period's years, months and days are facts too.</summary>
	private void AllowDates(NodaTime.DateInterval dates)
	{
		foreach (var date in new[] { dates.Start, dates.End })
		{
			_allowed.AddRange([date.Year, date.Month, date.Day]);
		}
	}

	private void AllowNumbersIn(object? cell)
	{
		switch (cell)
		{
			case decimal number:
				_allowed.Add(number);
				break;
			case long number:
				_allowed.Add(number);
				break;
			case string text:
				foreach (Match match in Number().Matches(text))
				{
					_allowed.AddRange(Readings(match.Value).Select(static r => r.Value));
				}

				// Labels like 2026-09 or 2026-Q3 hold their year and month as separate numbers.
				foreach (Match match in Digits().Matches(text))
				{
					_allowed.Add(decimal.Parse(match.Value, CultureInfo.InvariantCulture));
				}

				break;
			default:
				break;
		}
	}

	private static string Show(object? value) => value switch
	{
		null => "(none)",
		decimal number => number.ToString("0.##", CultureInfo.InvariantCulture),
		long number => number.ToString(CultureInfo.InvariantCulture),
		_ => value.ToString() ?? string.Empty,
	};

	// A number as people write it: digits, optionally grouped by three with one separator, optionally with decimals
	// after the other one. Not part of a word or a code (Q3, c_001).
	[GeneratedRegex(@"(?<![\p{L}\p{N}_])-?\d+(?:[.,]\d+)*(?![\p{L}\p{N}_])")]
	private static partial Regex Number();

	[GeneratedRegex(@"\d+")]
	private static partial Regex Digits();
}
