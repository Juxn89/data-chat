namespace DataChat.Infrastructure.Tests;

using DataChat.Application.Features.Questions;
using NodaTime;

/// <summary>A summary is shown only when every figure in it is one of the answer's (plan §5).</summary>
public sealed class NarrationFactsTests
{
	private static readonly ResultColumn Branch = new("sucursal", "Sucursal", ResultColumnKind.Dimension);

	private static readonly ResultColumn Sales = new("ventas", "Ventas", ResultColumnKind.Metric);

	private static readonly ResultColumn PreviousSales = new("ventas", "Ventas", ResultColumnKind.PreviousMetric);

	private static readonly NarrationFacts Comparison = NarrationFacts.Of(
		"¿Cómo le fue a cada sucursal el último trimestre frente al anterior?",
		Answer([Branch, Sales, PreviousSales], ["Centro", 12_078.19m, 12_144.41m], ["Las Colinas", 3_845.06m, 6_827.83m]));

	[Theory]
	[InlineData("Las Colinas vendió 3.845,06 dólares, frente a 6.827,83.")]
	[InlineData("Las Colinas sold 3,845.06 against 6,827.83 the quarter before.")]
	[InlineData("Las Colinas cayó 43,7 % (2.982,77 menos).")]
	[InlineData("Entre las dos vendieron 15.923 dólares.")]
	[InlineData("Centro se mantuvo en 12.078 y Las Colinas bajó a 3.845.")]
	[InlineData("Las 2 sucursales: Centro aporta el 75,9 % del total.")]
	public void Figures_from_the_rows_totals_shares_and_changes_pass(string narrative) =>
		Assert.Empty(Comparison.Unsupported(narrative));

	[Theory]
	[InlineData("Las Colinas cayó un 50 %.", "50")]
	[InlineData("Las Colinas vendió 3.900 dólares.", "3.900")]
	[InlineData("Centro vendió 12.100 dólares.", "12.100")]
	[InlineData("En total vendieron 20.000.", "20.000")]
	public void A_figure_that_is_not_a_fact_is_caught(string narrative, string invented) =>
		Assert.Equal([invented], Comparison.Unsupported(narrative));

	[Fact]
	public void Years_and_counts_from_the_labels_and_the_question_pass()
	{
		var facts = NarrationFacts.Of("Los 5 productos que más vendieron en 2026", Answer([new("time", "time", ResultColumnKind.Time), Sales], ["2026-09", 11_000m]));

		Assert.Empty(facts.Unsupported("En septiembre de 2026 se vendieron 11.000 dólares; es el mes 09."));
	}

	[Fact]
	public void The_periods_dates_pass() =>
		Assert.Empty(Comparison.Unsupported("Del 1 de julio al 30 de septiembre de 2026 (2026-07-01 a 2026-09-30), Centro vendió 12078.19."));

	[Fact]
	public void The_facts_spell_out_changes_and_totals_for_the_model() =>
		Assert.Contains("change in Ventas: -2982.77 (-43.7 %)", Comparison.Text, StringComparison.Ordinal);

	private static QueryAnswer Answer(IReadOnlyList<ResultColumn> columns, params object?[][] rows) =>
		new(columns, rows, "SELECT …", [], new DateInterval(new LocalDate(2026, 7, 1), new LocalDate(2026, 9, 30)), null, false);
}
