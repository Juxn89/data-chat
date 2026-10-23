namespace DataChat.Domain.Periods;

using NodaTime;

/// <summary>
/// The periods a question can name relative to today. The model picks one of these names and the server works out
/// the dates (ADR 0005): a model that does calendar arithmetic gets "last quarter" wrong in exactly the cases nobody
/// checks, like the first days of a quarter.
/// </summary>
public enum NamedPeriod
{
	/// <summary>The current month, up to today.</summary>
	ThisMonth,

	/// <summary>The last complete month.</summary>
	LastMonth,

	/// <summary>The current quarter, up to today.</summary>
	ThisQuarter,

	/// <summary>The last complete quarter.</summary>
	LastQuarter,

	/// <summary>The six complete months before the current one.</summary>
	LastSixMonths,

	/// <summary>The twelve complete months before the current one.</summary>
	LastTwelveMonths,

	/// <summary>The current year, up to today.</summary>
	ThisYear,

	/// <summary>The last complete year.</summary>
	LastYear,
}

/// <summary>What a period is compared with.</summary>
public enum Comparison
{
	/// <summary>The period of the same length right before it: last quarter against the one before.</summary>
	PreviousPeriod,

	/// <summary>The same dates one year earlier.</summary>
	SamePeriodLastYear,
}

/// <summary>Turns named periods and comparisons into dates. Pure calendar arithmetic, no clock of its own.</summary>
public static class PeriodCalendar
{
	private const int MonthsPerQuarter = 3;

	private const int MonthsInHalfYear = 6;

	private const int MonthsInYear = 12;

	/// <summary>The dates a named period covers, both ends included.</summary>
	/// <param name="period">The period.</param>
	/// <param name="today">Today, on the business's calendar.</param>
	/// <returns>The dates.</returns>
	public static DateInterval Resolve(NamedPeriod period, LocalDate today)
	{
		var monthStart = today.With(DateAdjusters.StartOfMonth);
		var quarterStart = new LocalDate(today.Year, (((today.Month - 1) / MonthsPerQuarter) * MonthsPerQuarter) + 1, 1);
		var yearStart = new LocalDate(today.Year, 1, 1);
		return period switch
		{
			NamedPeriod.ThisMonth => new DateInterval(monthStart, today),
			NamedPeriod.LastMonth => Months(monthStart.PlusMonths(-1), 1),
			NamedPeriod.ThisQuarter => new DateInterval(quarterStart, today),
			NamedPeriod.LastQuarter => Months(quarterStart.PlusMonths(-MonthsPerQuarter), MonthsPerQuarter),
			NamedPeriod.LastSixMonths => Months(monthStart.PlusMonths(-MonthsInHalfYear), MonthsInHalfYear),
			NamedPeriod.LastTwelveMonths => Months(monthStart.PlusMonths(-MonthsInYear), MonthsInYear),
			NamedPeriod.ThisYear => new DateInterval(yearStart, today),
			NamedPeriod.LastYear => Months(yearStart.PlusYears(-1), MonthsInYear),
			_ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unknown period."),
		};
	}

	/// <summary>The period a period is compared with.</summary>
	/// <param name="period">The period asked about.</param>
	/// <param name="comparison">What it is compared with.</param>
	/// <returns>The other period's dates.</returns>
	public static DateInterval CompareWith(DateInterval period, Comparison comparison)
	{
		ArgumentNullException.ThrowIfNull(period);
		return comparison switch
		{
			Comparison.SamePeriodLastYear => Shift(period, months: -MonthsInYear),
			Comparison.PreviousPeriod when WholeMonths(period) is { } months => Shift(period, months: -months),
			Comparison.PreviousPeriod => new DateInterval(period.Start.PlusDays(-period.Length), period.Start.PlusDays(-1)),
			_ => throw new ArgumentOutOfRangeException(nameof(comparison), comparison, "Unknown comparison."),
		};
	}

	private static DateInterval Months(LocalDate start, int months) => new(start, start.PlusMonths(months).PlusDays(-1));

	/// <summary>The number of months a period spans when it starts on a 1st and ends on a month's last day.</summary>
	private static int? WholeMonths(DateInterval period)
	{
		if (period.Start.Day != 1 || period.End != period.End.With(DateAdjusters.EndOfMonth))
		{
			return null;
		}

		return ((period.End.Year - period.Start.Year) * MonthsInYear) + period.End.Month - period.Start.Month + 1;
	}

	/// <summary>Moves a period by whole months; a period that ends on a month's last day still does after moving.</summary>
	private static DateInterval Shift(DateInterval period, int months)
	{
		var end = period.End.PlusMonths(months);
		if (period.End == period.End.With(DateAdjusters.EndOfMonth))
		{
			end = end.With(DateAdjusters.EndOfMonth);
		}

		return new DateInterval(period.Start.PlusMonths(months), end);
	}
}
