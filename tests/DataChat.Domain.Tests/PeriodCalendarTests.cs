namespace DataChat.Domain.Tests;

using DataChat.Domain.Periods;
using NodaTime;

public sealed class PeriodCalendarTests
{
	// The sample dataset's "today": its last sale is on 30 Sep 2026.
	private static readonly LocalDate Today = new(2026, 10, 15);

	public static TheoryData<NamedPeriod, LocalDate, LocalDate> Named => new()
	{
		{ NamedPeriod.ThisMonth, new(2026, 10, 1), Today },
		{ NamedPeriod.LastMonth, new(2026, 9, 1), new(2026, 9, 30) },
		{ NamedPeriod.ThisQuarter, new(2026, 10, 1), Today },
		{ NamedPeriod.LastQuarter, new(2026, 7, 1), new(2026, 9, 30) },
		{ NamedPeriod.LastSixMonths, new(2026, 4, 1), new(2026, 9, 30) },
		{ NamedPeriod.LastTwelveMonths, new(2025, 10, 1), new(2026, 9, 30) },
		{ NamedPeriod.ThisYear, new(2026, 1, 1), Today },
		{ NamedPeriod.LastYear, new(2025, 1, 1), new(2025, 12, 31) },
	};

	[Theory]
	[MemberData(nameof(Named))]
	public void Named_periods_resolve_against_today(NamedPeriod period, LocalDate start, LocalDate end) =>
		Assert.Equal(new DateInterval(start, end), PeriodCalendar.Resolve(period, Today));

	[Fact]
	public void Last_quarter_in_the_first_days_of_a_year_is_the_previous_years_fourth() =>
		Assert.Equal(
			new DateInterval(new LocalDate(2025, 10, 1), new LocalDate(2025, 12, 31)),
			PeriodCalendar.Resolve(NamedPeriod.LastQuarter, new LocalDate(2026, 1, 3)));

	[Fact]
	public void Last_month_in_a_leap_year_march_is_the_whole_february() =>
		Assert.Equal(
			new DateInterval(new LocalDate(2024, 2, 1), new LocalDate(2024, 2, 29)),
			PeriodCalendar.Resolve(NamedPeriod.LastMonth, new LocalDate(2024, 3, 10)));

	[Fact]
	public void A_quarter_compares_with_the_quarter_before() =>
		Assert.Equal(
			new DateInterval(new LocalDate(2026, 4, 1), new LocalDate(2026, 6, 30)),
			PeriodCalendar.CompareWith(Quarter3, Comparison.PreviousPeriod));

	[Fact]
	public void A_quarter_compares_with_the_same_quarter_last_year() =>
		Assert.Equal(
			new DateInterval(new LocalDate(2025, 7, 1), new LocalDate(2025, 9, 30)),
			PeriodCalendar.CompareWith(Quarter3, Comparison.SamePeriodLastYear));

	[Fact]
	public void A_whole_month_compares_with_the_whole_month_before_whatever_its_length() =>
		Assert.Equal(
			new DateInterval(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 31)),
			PeriodCalendar.CompareWith(new DateInterval(new LocalDate(2024, 2, 1), new LocalDate(2024, 2, 29)), Comparison.PreviousPeriod));

	[Fact]
	public void A_leap_february_last_year_ends_on_the_28th() =>
		Assert.Equal(
			new DateInterval(new LocalDate(2023, 2, 1), new LocalDate(2023, 2, 28)),
			PeriodCalendar.CompareWith(new DateInterval(new LocalDate(2024, 2, 1), new LocalDate(2024, 2, 29)), Comparison.SamePeriodLastYear));

	[Fact]
	public void A_period_to_date_compares_with_as_many_days_before_it() =>
		Assert.Equal(
			new DateInterval(new LocalDate(2026, 9, 16), new LocalDate(2026, 9, 30)),
			PeriodCalendar.CompareWith(PeriodCalendar.Resolve(NamedPeriod.ThisQuarter, Today), Comparison.PreviousPeriod));

	private static DateInterval Quarter3 => new(new LocalDate(2026, 7, 1), new LocalDate(2026, 9, 30));
}
