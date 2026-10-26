namespace DataChat.Domain.Tests;

using DataChat.Domain.Semantics;

public sealed class ProposalTests
{
	private static readonly SemanticLayer Proposed = Proposal.For(SampleDataset.Dataset);

	[Fact]
	public void The_proposal_for_the_sample_is_valid() =>
		Assert.Empty(Proposed.Validate(SampleDataset.Dataset));

	[Fact]
	public void The_first_date_column_is_the_time() =>
		Assert.Equal("c_001", Proposed.TimeColumn);

	[Fact]
	public void Amounts_add_up_prices_average_and_invoice_numbers_are_counted()
	{
		Assert.Equal(new Aggregate(Aggregation.Sum, "c_008"), Formula("Total"));
		Assert.Equal(new Aggregate(Aggregation.Sum, "c_006"), Formula("Unidades"));
		Assert.Equal(new Aggregate(Aggregation.Average, "c_007"), Formula("Precio unitario"));
		Assert.Equal(new Aggregate(Aggregation.CountDistinct, "c_002"), Formula("Factura"));
	}

	[Fact]
	public void Text_with_few_values_is_a_dimension() =>
		Assert.Equal(["sucursal", "categoria", "producto"], Proposed.Dimensions.Select(static d => d.Key));

	[Theory]
	[InlineData("Categoría", "categoria")]
	[InlineData("Precio unitario", "precio_unitario")]
	[InlineData("AÑO FISCAL", "ano_fiscal")]
	[InlineData("% Margen", "margen")]
	[InlineData("2025", "m_2025")]
	[InlineData("monto\"); DROP TABLE datasets; --", "monto_drop_table_datasets")]
	public void Keys_are_folded_to_plain_ascii(string name, string key) =>
		Assert.Equal(key, Proposal.Key(name, new HashSet<string>()));

	[Fact]
	public void A_taken_key_gets_a_number()
	{
		var taken = new HashSet<string>();

		Assert.Equal("total", Proposal.Key("Total", taken));
		Assert.Equal("total_2", Proposal.Key("total", taken));
	}

	private static MetricFormula Formula(string name) => Proposed.Metrics.Single(metric => metric.Name == name).Formula;
}
