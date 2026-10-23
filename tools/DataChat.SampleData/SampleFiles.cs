namespace DataChat.SampleData;

using ClosedXML.Excel;

/// <summary>
/// Writes the sample in each language as an Excel file shaped like one a business would export: a title row above
/// the header, dates and money formatted. The demo uploads these; the evals load the same lines straight from
/// <see cref="SalesGenerator"/>.
/// </summary>
public static class SampleFiles
{
	/// <summary>The title above the Excel header: real exports carry one, and the importer has to find the header under it.</summary>
	private static readonly Label Title = new(
		"Distribuidora Demo · Ventas de abril de 2025 a septiembre de 2026",
		"Distribuidora Demo · Sales from April 2025 to September 2026");

	private static readonly Label Sheet = new("Ventas", "Sales");

	private static readonly Label[] Headers =
	[
		new("Fecha", "Date"),
		new("Factura", "Invoice"),
		new("Sucursal", "Branch"),
		new("Categoría", "Category"),
		new("Producto", "Product"),
		new("Unidades", "Units"),
		new("Precio unitario", "Unit price"),
		new("Total", "Total"),
	];

	/// <summary>Day first in Spanish, as a Latin American export would have it; ISO in English, so it is never ambiguous.</summary>
	private static readonly Label DateFormat = new("dd/mm/yyyy", "yyyy-mm-dd");

	private const string MoneyFormat = "#,##0.00";

	private const int TitleRow = 1;

	private const int HeaderRow = 3;

	/// <summary>The file name, without extension, of the sample in a language.</summary>
	/// <param name="language">The language.</param>
	/// <returns>The name.</returns>
	public static string BaseName(SampleLanguage language) => language == SampleLanguage.Es ? "ventas-demo" : "sales-demo";

	/// <summary>Writes the Excel file.</summary>
	/// <param name="lines">The lines.</param>
	/// <param name="language">The language.</param>
	/// <param name="path">Where.</param>
	public static void WriteExcel(IReadOnlyList<SaleLine> lines, SampleLanguage language, string path)
	{
		using var workbook = new XLWorkbook();
		var sheet = workbook.AddWorksheet(Sheet.In(language));
		sheet.Cell(TitleRow, 1).Value = Title.In(language);
		sheet.Cell(TitleRow, 1).Style.Font.Bold = true;
		for (var c = 0; c < Headers.Length; c++)
		{
			sheet.Cell(HeaderRow, c + 1).Value = Headers[c].In(language);
		}

		sheet.Row(HeaderRow).Style.Font.Bold = true;
		sheet.Cell(HeaderRow + 1, 1).InsertData(lines.Select(line => new object[]
		{
			line.Date.ToDateTime(TimeOnly.MinValue),
			line.Invoice,
			line.Branch,
			line.Category.In(language),
			line.Product.In(language),
			line.Units,
			line.UnitPrice,
			line.Total,
		}));

		var last = HeaderRow + lines.Count;
		sheet.Range(HeaderRow + 1, 1, last, 1).Style.DateFormat.Format = DateFormat.In(language);
		sheet.Range(HeaderRow + 1, 7, last, 8).Style.NumberFormat.Format = MoneyFormat;
		sheet.Columns(1, Headers.Length).AdjustToContents(HeaderRow, HeaderRow + 200);
		workbook.SaveAs(path);
	}
}
