namespace DataChat.SampleData;

/// <summary>One line of an invoice, in both languages at once.</summary>
/// <param name="Date">The day of the sale.</param>
/// <param name="Invoice">The invoice number.</param>
/// <param name="Branch">The branch.</param>
/// <param name="Category">The product's category.</param>
/// <param name="Product">The product.</param>
/// <param name="Units">Units sold.</param>
/// <param name="UnitPrice">The price per unit, in USD.</param>
/// <param name="Total">Units times price, in USD.</param>
public sealed record SaleLine(
	DateOnly Date,
	string Invoice,
	string Branch,
	Label Category,
	Label Product,
	int Units,
	decimal UnitPrice,
	decimal Total);

/// <summary>
/// The sample sales, generated rather than hand-made so they are large enough to be worth asking about, and fully
/// deterministic: the same seed gives the same lines on any machine, so the evals' expected answers never move.
/// Three patterns are planted for the demo's questions (plan §0): a December peak, a category that grows every
/// month, and a branch that falls in the last quarter.
/// </summary>
public static class SalesGenerator
{
	/// <summary>Eighteen months of sales, April 2025 to September 2026.</summary>
	public static readonly DateOnly FirstDay = new(2025, 4, 1);

	/// <summary>The last day with sales; the app's "today" for the sample sits a couple of weeks later.</summary>
	public static readonly DateOnly LastDay = new(2026, 9, 30);

	/// <summary>The first day of the quarter in which <see cref="SalesCatalog.DecliningBranch"/> falls.</summary>
	public static readonly DateOnly DeclineStarts = new(2026, 7, 1);

	/// <summary>The day the list prices went up.</summary>
	public static readonly DateOnly PriceIncrease = new(2026, 1, 1);

	/// <summary>The seed. Changing it changes every expected answer in <c>evals/questions.json</c>.</summary>
	public const ulong Seed = 20261023;

	/// <summary>What the declining branch keeps of its usual business from <see cref="DeclineStarts"/>.</summary>
	private const double DeclineFactor = 0.65;

	/// <summary>How much the growing category's share rises each month, compounded.</summary>
	private const double MonthlyGrowth = 1.04;

	/// <summary>The price increase of January 2026.</summary>
	private const decimal PriceIncreaseFactor = 1.05m;

	/// <summary>Most lines an invoice carries.</summary>
	private const int MaxLinesPerInvoice = 4;

	/// <summary>Sales per month relative to an ordinary month: a December peak, a quieter January.</summary>
	private static readonly double[] MonthFactor = [0.85, 0.90, 1.00, 1.00, 1.00, 0.95, 1.00, 1.00, 1.05, 1.00, 1.15, 1.60];

	/// <summary>Sales per weekday, Monday first: Saturdays are busy, Sundays are short.</summary>
	private static readonly double[] WeekdayFactor = [1.00, 0.95, 0.95, 1.00, 1.10, 1.30, 0.70];

	/// <summary>The sale lines, ordered by date, branch and invoice.</summary>
	/// <returns>The lines.</returns>
	public static IReadOnlyList<SaleLine> Generate()
	{
		var random = new SplitMix64(Seed);
		var lines = new List<SaleLine>();
		var invoiceNumbers = SalesCatalog.Branches.ToDictionary(static branch => branch.Name, static _ => 0);

		for (var day = FirstDay; day <= LastDay; day = day.AddDays(1))
		{
			var weights = CategoryWeights(day);
			foreach (var branch in SalesCatalog.Branches)
			{
				var invoices = random.NextPoisson(branch.InvoicesPerDay * DayFactor(day, branch));
				for (var i = 0; i < invoices; i++)
				{
					var invoice = $"{branch.InvoicePrefix}-{++invoiceNumbers[branch.Name]:D6}";
					var lineCount = 1 + random.NextInt(MaxLinesPerInvoice);
					for (var l = 0; l < lineCount; l++)
					{
						var category = SalesCatalog.Categories[random.NextWeighted(weights)];
						var product = category.Products[random.NextInt(category.Products.Count)];
						var units = 1 + random.NextInt(category.MaxUnits);
						var price = day >= PriceIncrease ? decimal.Round(product.Price * PriceIncreaseFactor, 2) : product.Price;
						lines.Add(new SaleLine(day, invoice, branch.Name, category.Name, product.Name, units, price, units * price));
					}
				}
			}
		}

		return lines;
	}

	private static double DayFactor(DateOnly day, Branch branch)
	{
		var factor = MonthFactor[day.Month - 1] * WeekdayFactor[((int)day.DayOfWeek + 6) % 7];
		return branch.Name == SalesCatalog.DecliningBranch && day >= DeclineStarts ? factor * DeclineFactor : factor;
	}

	private static double[] CategoryWeights(DateOnly day)
	{
		var months = ((day.Year - FirstDay.Year) * 12) + day.Month - FirstDay.Month;
		return SalesCatalog.Categories
			.Select(category => category.Name == SalesCatalog.GrowingCategory ? category.Weight * Math.Pow(MonthlyGrowth, months) : category.Weight)
			.ToArray();
	}
}

/// <summary>
/// SplitMix64: tiny, fast and specified, so the sequence is the same everywhere. <see cref="Random"/> with a seed
/// is deterministic today but its algorithm is not part of its contract.
/// </summary>
/// <param name="state">The seed.</param>
internal sealed class SplitMix64(ulong state)
{
	private const double TwoToThe53 = 9007199254740992.0;

	/// <summary>The next 64 random bits.</summary>
	/// <returns>The bits.</returns>
	public ulong Next()
	{
		var z = state += 0x9E3779B97F4A7C15UL;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
		return z ^ (z >> 31);
	}

	/// <summary>A double in [0, 1).</summary>
	/// <returns>The number.</returns>
	public double NextDouble() => (Next() >> 11) / TwoToThe53;

	/// <summary>An integer in [0, max).</summary>
	/// <param name="max">The exclusive upper bound.</param>
	/// <returns>The number.</returns>
	public int NextInt(int max) => (int)(NextDouble() * max);

	/// <summary>An index chosen with the given weights.</summary>
	/// <param name="weights">Non-negative weights.</param>
	/// <returns>The index.</returns>
	public int NextWeighted(double[] weights)
	{
		var target = NextDouble() * weights.Sum();
		for (var i = 0; i < weights.Length; i++)
		{
			target -= weights[i];
			if (target < 0)
			{
				return i;
			}
		}

		return weights.Length - 1;
	}

	/// <summary>A Poisson-distributed count (Knuth's method; fine for the small means used here).</summary>
	/// <param name="mean">The mean.</param>
	/// <returns>The count.</returns>
	public int NextPoisson(double mean)
	{
		var limit = Math.Exp(-mean);
		var product = NextDouble();
		var count = 0;
		while (product > limit)
		{
			count++;
			product *= NextDouble();
		}

		return count;
	}
}
