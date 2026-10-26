namespace DataChat.SampleData.Tests;

using System.Text.Json;

/// <summary>
/// A second, independent computation of some expected answers in <c>evals/questions.json</c>: the file's answers come
/// from the reference SQL, these from LINQ over the generator's lines. A wrong date range or a wrong filter in the
/// reference SQL would score the app against a wrong answer; the two agreeing makes that unlikely.
/// </summary>
public sealed class ExpectedAnswersTests
{
	private static readonly IReadOnlyList<SaleLine> Lines = SalesGenerator.Generate();

	private static readonly DateOnly Q3Start = new(2026, 7, 1);

	private static readonly DateOnly Q3End = new(2026, 9, 30);

	private static readonly Dictionary<string, JsonElement> Expected = LoadExpected();

	[Fact]
	public void Total_sales_last_quarter() =>
		Assert.Equal(Sum(Between(Q3Start, Q3End)), Cell("q01", 0, 0));

	[Fact]
	public void Sales_by_branch_last_quarter()
	{
		var byBranch = Between(Q3Start, Q3End).GroupBy(static line => line.Branch)
			.OrderByDescending(static branch => branch.Sum(static line => line.Total))
			.ToArray();

		for (var i = 0; i < byBranch.Length; i++)
		{
			Assert.Equal(byBranch[i].Key, Text("q03", i, 0));
			Assert.Equal(Sum(byBranch[i]), Cell("q03", i, 1));
		}
	}

	[Fact]
	public void Best_month() =>
		Assert.Equal(
			Lines.GroupBy(static line => line.Date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture))
				.MaxBy(static month => month.Sum(static line => line.Total))!.Key,
			Text("q06", 0, 0));

	[Fact]
	public void Invoices_of_one_branch_in_one_month() =>
		Assert.Equal(
			Between(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)).Where(static line => line.Branch == "Centro").Select(static line => line.Invoice).Distinct().Count(),
			(int)Cell("q10", 0, 0));

	[Fact]
	public void Declining_branch_against_the_quarter_before()
	{
		var colinas = Lines.Where(static line => line.Branch == SalesCatalog.DecliningBranch).ToArray();

		Assert.Equal(Sum(colinas.Where(static line => line.Date >= Q3Start && line.Date <= Q3End)), Cell("q13", 0, 0));
		Assert.Equal(Sum(colinas.Where(static line => line.Date >= new DateOnly(2026, 4, 1) && line.Date <= new DateOnly(2026, 6, 30))), Cell("q13", 0, 1));
	}

	[Fact]
	public void Top_product_by_units_in_december()
	{
		var top = Between(new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31))
			.GroupBy(static line => line.Product.Es)
			.MaxBy(static product => product.Sum(static line => line.Units))!;

		Assert.Equal(top.Key, Text("q16", 0, 0));
		Assert.Equal(top.Sum(static line => line.Units), (int)Cell("q16", 0, 1));
	}

	/// <summary>Each English question asks what a Spanish one asks, on the same sales: the figures must be the same.</summary>
	public static TheoryData<string, string> EnglishTwins => new()
	{
		{ "en01", "q01" }, { "en02", "q02" }, { "en03", "q05" }, { "en04", "q08" }, { "en05", "q09" },
		{ "en06", "q11" }, { "en07", "q07" }, { "en08", "q04" }, { "en09", "q13" }, { "en10", "q14" },
		{ "en11", "q17" }, { "en12", "q19" }, { "en13", "q18" }, { "en14", "q16" }, { "en15", "q06" },
	};

	[Theory]
	[MemberData(nameof(EnglishTwins))]
	public void English_answers_have_the_same_figures_as_their_spanish_twins(string english, string spanish) =>
		Assert.Equal(Figures(spanish), Figures(english));

	[Fact]
	public void A_period_without_sales_answers_null() =>
		Assert.Equal(JsonValueKind.Null, Expected["q48"].GetProperty("rows")[0][0].ValueKind);

	private static decimal[] Figures(string id) =>
		Expected[id].GetProperty("rows").EnumerateArray()
			.SelectMany(static row => row.EnumerateArray())
			.Where(static cell => cell.ValueKind == JsonValueKind.Number)
			.Select(static cell => cell.GetDecimal())
			.ToArray();

	private static IEnumerable<SaleLine> Between(DateOnly from, DateOnly to) => Lines.Where(line => line.Date >= from && line.Date <= to);

	private static decimal Sum(IEnumerable<SaleLine> lines) => decimal.Round(lines.Sum(static line => line.Total), 2);

	private static decimal Cell(string id, int row, int column) => Expected[id].GetProperty("rows")[row][column].GetDecimal();

	private static string? Text(string id, int row, int column) => Expected[id].GetProperty("rows")[row][column].GetString();

	private static Dictionary<string, JsonElement> LoadExpected()
	{
		using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "evals", "questions.json")));
		return document.RootElement.GetProperty("items").EnumerateArray()
			.ToDictionary(static item => item.GetProperty("id").GetString()!, static item => item.GetProperty("expected").Clone());
	}
}
