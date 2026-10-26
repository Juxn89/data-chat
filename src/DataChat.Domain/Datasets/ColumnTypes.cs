namespace DataChat.Domain.Datasets;

using System.Globalization;
using System.Text.RegularExpressions;
using NodaTime;

/// <summary>What a column holds, and so the Postgres type of its physical column.</summary>
public enum ColumnType
{
	/// <summary>Free text: <c>text</c>.</summary>
	Text,

	/// <summary>Whole numbers: <c>bigint</c>.</summary>
	Integer,

	/// <summary>Numbers with decimals, money included: <c>numeric</c>.</summary>
	Decimal,

	/// <summary>Calendar dates: <c>date</c>.</summary>
	Date,
}

/// <summary>How a column's text dates are written, when they are written as text.</summary>
public enum DateOrder
{
	/// <summary>2026-09-30.</summary>
	Iso,

	/// <summary>30/09/2026, as Latin America and Spain write it.</summary>
	DayFirst,

	/// <summary>09/30/2026, as the United States writes it.</summary>
	MonthFirst,
}

/// <summary>How a column's text numbers are written.</summary>
public enum NumberStyle
{
	/// <summary>1,234.56.</summary>
	PointDecimal,

	/// <summary>1.234,56.</summary>
	CommaDecimal,
}

/// <summary>A column's inferred type and how to read its text cells.</summary>
/// <param name="Type">The type.</param>
/// <param name="DateOrder">For text dates, their order.</param>
/// <param name="NumberStyle">For text numbers, their separators.</param>
/// <param name="AmbiguousDates">Every text date fit both day-first and month-first; day-first was assumed.</param>
public sealed record InferredType(ColumnType Type, DateOrder? DateOrder = null, NumberStyle? NumberStyle = null, bool AmbiguousDates = false);

/// <summary>
/// Decides a column's type from all of its cells. A type is chosen only when every non-empty cell fits it, so a
/// column with one "N/A" among its amounts stays text instead of losing that cell silently.
/// </summary>
public static partial class ColumnTypes
{
	private const int MaxMonth = 12;

	private static readonly NumberFormatInfo PointDecimal = new() { NumberDecimalSeparator = ".", NumberGroupSeparator = "," };

	private static readonly NumberFormatInfo CommaDecimal = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };

	/// <summary>Infers a column's type.</summary>
	/// <param name="cells">The column's cells, header excluded.</param>
	/// <param name="preferCommaDecimal">
	/// The file suggests comma decimals (a CSV separated by semicolons) for numbers that fit both styles, like "1.234".
	/// </param>
	/// <returns>The type.</returns>
	public static InferredType Infer(IReadOnlyCollection<RawCell> cells, bool preferCommaDecimal = false)
	{
		ArgumentNullException.ThrowIfNull(cells);
		var values = cells.Where(static cell => !cell.IsEmpty).ToArray();
		if (values.Length == 0)
		{
			return new InferredType(ColumnType.Text);
		}

		if (values.All(static cell => cell.Date is not null || cell.Text is not null) && InferDates(values) is { } dates)
		{
			return dates;
		}

		if (values.All(static cell => cell.Number is not null || cell.Text is not null) && InferNumbers(values, preferCommaDecimal) is { } numbers)
		{
			return numbers;
		}

		return new InferredType(ColumnType.Text);
	}

	/// <summary>Reads a cell as its column's type; an empty cell is null.</summary>
	/// <param name="cell">The cell.</param>
	/// <param name="type">The column's type.</param>
	/// <returns>A <see cref="LocalDate"/>, <see cref="long"/>, <see cref="decimal"/>, <see cref="string"/> or null.</returns>
	/// <exception cref="FormatException">The cell does not fit the type, which inference rules out.</exception>
	public static object? Read(RawCell cell, InferredType type)
	{
		ArgumentNullException.ThrowIfNull(type);
		if (cell.IsEmpty)
		{
			return null;
		}

		return type.Type switch
		{
			ColumnType.Date => cell.Date ?? ParseDate(cell.Text!, type.DateOrder!.Value) ?? throw new FormatException($"'{cell.Text}' is not a date."),
			ColumnType.Integer => (long)(cell.Number ?? ParseNumber(cell.Text!, type.NumberStyle!.Value) ?? throw new FormatException($"'{cell.Text}' is not a number.")),
			ColumnType.Decimal => cell.Number ?? ParseNumber(cell.Text!, type.NumberStyle!.Value) ?? throw new FormatException($"'{cell.Text}' is not a number."),
			_ => cell.Text ?? (object?)cell.Number?.ToString(CultureInfo.InvariantCulture) ?? cell.Date?.ToString("uuuu-MM-dd", CultureInfo.InvariantCulture),
		};
	}

	private static InferredType? InferDates(RawCell[] values)
	{
		var texts = values.Where(static cell => cell.Date is null).Select(static cell => cell.Text!).ToArray();
		if (texts.Length == 0)
		{
			return new InferredType(ColumnType.Date, DateOrder.Iso);
		}

		if (texts.All(static text => IsoDate().IsMatch(text)))
		{
			return texts.All(static text => ParseDate(text, DateOrder.Iso) is not null) ? new InferredType(ColumnType.Date, DateOrder.Iso) : null;
		}

		var parts = texts.Select(static text => SlashDate().Match(text)).ToArray();
		if (parts.Any(static match => !match.Success))
		{
			return null;
		}

		var firstOverTwelve = parts.Any(static match => int.Parse(match.Groups["a"].Value, CultureInfo.InvariantCulture) > MaxMonth);
		var secondOverTwelve = parts.Any(static match => int.Parse(match.Groups["b"].Value, CultureInfo.InvariantCulture) > MaxMonth);
		if (firstOverTwelve && secondOverTwelve)
		{
			return null;
		}

		var order = secondOverTwelve ? DateOrder.MonthFirst : DateOrder.DayFirst;
		return texts.All(text => ParseDate(text, order) is not null)
			? new InferredType(ColumnType.Date, order, AmbiguousDates: !firstOverTwelve && !secondOverTwelve)
			: null;
	}

	private static InferredType? InferNumbers(RawCell[] values, bool preferCommaDecimal)
	{
		var texts = values.Where(static cell => cell.Number is null).Select(static cell => cell.Text!).ToArray();
		var fitsPoint = texts.All(static text => PointNumber().IsMatch(text));
		var fitsComma = texts.All(static text => CommaNumber().IsMatch(text));
		if (!fitsPoint && !fitsComma)
		{
			return null;
		}

		var style = fitsPoint && fitsComma
			? (preferCommaDecimal ? NumberStyle.CommaDecimal : NumberStyle.PointDecimal)
			: (fitsPoint ? NumberStyle.PointDecimal : NumberStyle.CommaDecimal);
		var numbers = values.Select(cell => cell.Number ?? ParseNumber(cell.Text!, style)).ToArray();
		if (numbers.Any(static number => number is null))
		{
			return null;
		}

		var whole = numbers.All(static number => number!.Value == decimal.Truncate(number.Value) && number.Value is >= long.MinValue and <= long.MaxValue);
		return new InferredType(whole ? ColumnType.Integer : ColumnType.Decimal, NumberStyle: style);
	}

	private static LocalDate? ParseDate(string text, DateOrder order)
	{
		int year, month, day;
		if (order == DateOrder.Iso)
		{
			var iso = IsoDate().Match(text);
			if (!iso.Success)
			{
				return null;
			}

			(year, month, day) = (Int(iso.Groups["y"]), Int(iso.Groups["m"]), Int(iso.Groups["d"]));
		}
		else
		{
			var slash = SlashDate().Match(text);
			if (!slash.Success)
			{
				return null;
			}

			var (a, b) = (Int(slash.Groups["a"]), Int(slash.Groups["b"]));
			(year, month, day) = order == DateOrder.DayFirst ? (Int(slash.Groups["y"]), b, a) : (Int(slash.Groups["y"]), a, b);
		}

		return month is >= 1 and <= MaxMonth && day >= 1 && day <= CalendarSystem.Iso.GetDaysInMonth(year, month)
			? new LocalDate(year, month, day)
			: null;
	}

	private static decimal? ParseNumber(string text, NumberStyle style)
	{
		var bare = text.Replace("$", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
		return decimal.TryParse(bare, NumberStyles.Number, style == NumberStyle.PointDecimal ? PointDecimal : CommaDecimal, out var number)
			? number
			: null;
	}

	private static int Int(Group group) => int.Parse(group.Value, CultureInfo.InvariantCulture);

	[GeneratedRegex(@"^(?<y>\d{4})-(?<m>\d{1,2})-(?<d>\d{1,2})$")]
	private static partial Regex IsoDate();

	[GeneratedRegex(@"^(?<a>\d{1,2})[/.-](?<b>\d{1,2})[/.-](?<y>\d{4})$")]
	private static partial Regex SlashDate();

	// Optional sign and "$"; thousands grouped by three or not grouped at all; decimals after the other separator.
	[GeneratedRegex(@"^-?\$?\s?(\d{1,3}(,\d{3})+|\d+)(\.\d+)?$")]
	private static partial Regex PointNumber();

	[GeneratedRegex(@"^-?\$?\s?(\d{1,3}(\.\d{3})+|\d+)(,\d+)?$")]
	private static partial Regex CommaNumber();
}
