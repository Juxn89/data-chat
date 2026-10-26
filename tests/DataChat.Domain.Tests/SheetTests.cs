namespace DataChat.Domain.Tests;

using DataChat.Domain.Datasets;

public sealed class SheetTests
{
	[Fact]
	public void The_header_under_a_title_and_a_blank_row_is_found() =>
		Assert.Equal(2, Sheet.FindHeader(
		[
			Row("Distribuidora Demo · Ventas"),
			Row(),
			Row("Fecha", "Sucursal", "Total"),
			Row("2026-09-30", "Centro", "12.50"),
		]));

	[Fact]
	public void A_first_row_of_data_is_not_a_header() =>
		Assert.Null(Sheet.FindHeader(
		[
			Row("2026-09-30", "Centro", "12.50"),
			Row("2026-09-30", "Altamira", "8.00"),
		]));

	[Fact]
	public void A_header_with_nothing_under_it_is_not_one() =>
		Assert.Null(Sheet.FindHeader([Row("Fecha", "Sucursal", "Total")]));

	[Fact]
	public void Names_are_cleaned_numbered_when_blank_and_unique()
	{
		var names = Sheet.ColumnNames([Text("  Sucursal\t\n"), RawCell.Empty, Text("Total"), Text("total")]);

		Assert.Equal(["Sucursal", "Columna 2", "Total", "total (2)"], names);
	}

	[Fact]
	public void A_malicious_header_is_only_a_name()
	{
		var name = Sheet.ColumnNames([Text("monto\"; DROP TABLE datasets; --")])[0];

		Assert.Equal("monto\"; DROP TABLE datasets; --", name);
		Assert.Equal("c_001", Sheet.PhysicalName(1));
	}

	[Fact]
	public void Long_names_are_cut() =>
		Assert.Equal(Sheet.MaxNameLength, Sheet.ColumnNames([Text(new string('x', 500))])[0].Length);

	private static RawCell[] Row(params string[] cells) => cells.Select(RawCell.FromText).ToArray();

	private static RawCell Text(string text) => RawCell.FromText(text);
}
