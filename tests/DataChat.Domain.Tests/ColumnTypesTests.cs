namespace DataChat.Domain.Tests;

using DataChat.Domain.Datasets;
using NodaTime;

public sealed class ColumnTypesTests
{
	[Fact]
	public void Typed_excel_dates_are_dates() =>
		Assert.Equal(ColumnType.Date, Infer(RawCell.FromDate(new LocalDate(2026, 9, 30)), RawCell.Empty).Type);

	[Fact]
	public void Iso_text_dates_are_dates()
	{
		var type = Infer(Text("2026-09-30"), Text("2026-10-01"));

		Assert.Equal(new InferredType(ColumnType.Date, DateOrder.Iso), type);
		Assert.Equal(new LocalDate(2026, 9, 30), ColumnTypes.Read(Text("2026-09-30"), type));
	}

	[Fact]
	public void A_day_over_twelve_first_means_day_first()
	{
		var type = Infer(Text("05/09/2026"), Text("30/09/2026"));

		Assert.Equal(new InferredType(ColumnType.Date, DateOrder.DayFirst), type);
		Assert.Equal(new LocalDate(2026, 9, 5), ColumnTypes.Read(Text("05/09/2026"), type));
	}

	[Fact]
	public void A_day_over_twelve_second_means_month_first()
	{
		var type = Infer(Text("09/05/2026"), Text("09/30/2026"));

		Assert.Equal(new InferredType(ColumnType.Date, DateOrder.MonthFirst), type);
		Assert.Equal(new LocalDate(2026, 9, 5), ColumnTypes.Read(Text("09/05/2026"), type));
	}

	[Fact]
	public void Dates_that_fit_both_orders_are_read_day_first_and_flagged() =>
		Assert.Equal(new InferredType(ColumnType.Date, DateOrder.DayFirst, AmbiguousDates: true), Infer(Text("05/09/2026"), Text("01/02/2026")));

	[Fact]
	public void Dates_that_contradict_each_other_are_text() =>
		Assert.Equal(ColumnType.Text, Infer(Text("30/09/2026"), Text("09/30/2026")).Type);

	[Fact]
	public void An_impossible_date_makes_the_column_text() =>
		Assert.Equal(ColumnType.Text, Infer(Text("2026-02-30")).Type);

	[Fact]
	public void Whole_numbers_are_integers() =>
		Assert.Equal(ColumnType.Integer, Infer(Text("3"), RawCell.FromNumber(12m), Text("-4")).Type);

	[Fact]
	public void Point_decimals_with_thousands_are_read()
	{
		var type = Infer(Text("1,234.56"), Text("$ 7.10"));

		Assert.Equal(new InferredType(ColumnType.Decimal, NumberStyle: NumberStyle.PointDecimal), type);
		Assert.Equal(1234.56m, ColumnTypes.Read(Text("1,234.56"), type));
	}

	[Fact]
	public void Comma_decimals_with_thousands_are_read()
	{
		var type = Infer(Text("1.234,56"), Text("7,10"));

		Assert.Equal(new InferredType(ColumnType.Decimal, NumberStyle: NumberStyle.CommaDecimal), type);
		Assert.Equal(1234.56m, ColumnTypes.Read(Text("1.234,56"), type));
	}

	[Fact]
	public void A_number_that_fits_both_styles_follows_the_files_hint()
	{
		Assert.Equal(1.234m, ColumnTypes.Read(Text("1.234"), ColumnTypes.Infer([Text("1.234")])));
		Assert.Equal(1234L, ColumnTypes.Read(Text("1.234"), ColumnTypes.Infer([Text("1.234")], preferCommaDecimal: true)));
	}

	[Fact]
	public void One_text_among_numbers_keeps_the_column_text() =>
		Assert.Equal(ColumnType.Text, Infer(Text("12.50"), Text("N/A"), Text("3")).Type);

	[Fact]
	public void An_empty_column_is_text() =>
		Assert.Equal(ColumnType.Text, Infer(RawCell.Empty, Text("  ")).Type);

	[Fact]
	public void Empty_cells_read_as_null() =>
		Assert.Null(ColumnTypes.Read(RawCell.Empty, new InferredType(ColumnType.Decimal, NumberStyle: NumberStyle.PointDecimal)));

	private static InferredType Infer(params RawCell[] cells) => ColumnTypes.Infer(cells);

	private static RawCell Text(string text) => RawCell.FromText(text);
}
