namespace DataChat.SampleData.Tests;

using ClosedXML.Excel;

/// <summary>The committed Excel files hold exactly what the generator produces, in each language.</summary>
public sealed class SampleFilesTests
{
	private const int FirstDataRow = 4;

	private const int TotalColumn = 8;

	private static readonly IReadOnlyList<SaleLine> Lines = SalesGenerator.Generate();

	[Theory]
	[InlineData(SampleLanguage.Es)]
	[InlineData(SampleLanguage.En)]
	public void The_committed_file_matches_the_generator(SampleLanguage language)
	{
		using var workbook = new XLWorkbook(Path.Combine(RepoPaths.Root, "samples", $"{SampleFiles.BaseName(language)}.xlsx"));
		var sheet = workbook.Worksheets.Single();
		var rows = sheet.RowsUsed().Where(static row => row.RowNumber() >= FirstDataRow).ToArray();

		Assert.Equal(Lines.Count, rows.Length);
		Assert.Equal(Lines.Sum(static line => line.Total), rows.Sum(static row => row.Cell(TotalColumn).GetValue<decimal>()));
		Assert.Equal(Lines[^1].Product.In(language), rows[^1].Cell(5).GetString());
	}
}
