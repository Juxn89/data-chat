namespace DataChat.SampleData.Tests;

/// <summary>
/// The sample is what every expected answer in <c>evals/questions.json</c> was computed from: it must not move, and
/// the patterns the demo asks about must really be in it.
/// </summary>
public sealed class SalesGeneratorTests
{
	// Pinned on 23 Oct 2026. A change here means the generator changed: regenerate samples/ and every expected answer.
	private const int PinnedLines = 37636;

	private const decimal PinnedTotal = 217_544.47m;

	private static readonly IReadOnlyList<SaleLine> Lines = SalesGenerator.Generate();

	[Fact]
	public void The_sample_is_the_pinned_one()
	{
		Assert.Equal(PinnedLines, Lines.Count);
		Assert.Equal(PinnedTotal, Lines.Sum(static line => line.Total));
	}

	[Fact]
	public void December_is_the_best_month() =>
		Assert.Equal(
			new DateOnly(2025, 12, 1),
			Lines.GroupBy(static line => new DateOnly(line.Date.Year, line.Date.Month, 1))
				.MaxBy(static month => month.Sum(static line => line.Total))!.Key);

	[Fact]
	public void The_growing_category_takes_a_larger_share_every_quarter()
	{
		var shares = Lines.GroupBy(static line => (line.Date.Year, Quarter: ((line.Date.Month - 1) / 3) + 1))
			.OrderBy(static quarter => quarter.Key)
			.Select(static quarter => quarter.Where(static line => line.Category == SalesCatalog.GrowingCategory).Sum(static line => line.Total) / quarter.Sum(static line => line.Total))
			.ToArray();

		Assert.All(shares.Zip(shares.Skip(1)), static pair => Assert.True(pair.Second > pair.First, $"{pair.Second:P1} after {pair.First:P1}"));
	}

	[Fact]
	public void Only_the_declining_branch_falls_in_the_last_quarter()
	{
		foreach (var branch in SalesCatalog.Branches)
		{
			var q2 = Sales(branch.Name, new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30));
			var q3 = Sales(branch.Name, new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30));
			if (branch.Name == SalesCatalog.DecliningBranch)
			{
				Assert.True(q3 < q2 * 0.8m, $"{branch.Name}: {q3} after {q2}");
			}
			else
			{
				Assert.True(q3 > q2 * 0.9m, $"{branch.Name}: {q3} after {q2}");
			}
		}
	}

	[Fact]
	public void Every_line_adds_up() =>
		Assert.All(Lines, static line => Assert.Equal(line.Units * line.UnitPrice, line.Total));

	private static decimal Sales(string branch, DateOnly from, DateOnly to) =>
		Lines.Where(line => line.Branch == branch && line.Date >= from && line.Date <= to).Sum(static line => line.Total);
}
