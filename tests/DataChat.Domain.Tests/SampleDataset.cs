namespace DataChat.Domain.Tests;

using DataChat.Domain.Datasets;
using DataChat.Domain.Semantics;
using NodaTime;

/// <summary>A dataset shaped like the sample (columns, types, distinct counts), and the layer a person would confirm.</summary>
internal static class SampleDataset
{
	public const int Rows = 37_636;

	public static Dataset Dataset { get; } = Dataset.Create(
		"Ventas",
		"ventas-demo.xlsx",
		[
			("Fecha", ColumnType.Date, Profile(548)),
			("Factura", ColumnType.Text, Profile(15_147)),
			("Sucursal", ColumnType.Text, Profile(4, "Centro", "Altamira", "Carretera a Masaya", "Las Colinas")),
			("Categoría", ColumnType.Text, Profile(6, "Bebidas", "Lácteos")),
			("Producto", ColumnType.Text, Profile(60)),
			("Unidades", ColumnType.Integer, Profile(6)),
			("Precio unitario", ColumnType.Decimal, Profile(120)),
			("Total", ColumnType.Decimal, Profile(900)),
		],
		Rows,
		Instant.FromUtc(2026, 10, 26, 12, 0));

	/// <summary>The layer the demo confirms: three aggregates, two ratios, three dimensions.</summary>
	public static SemanticLayer Layer { get; } = new(
		"c_001",
		[
			new Metric("ventas", "Ventas", new Aggregate(Aggregation.Sum, "c_008")),
			new Metric("unidades", "Unidades", new Aggregate(Aggregation.Sum, "c_006")),
			new Metric("facturas", "Facturas", new Aggregate(Aggregation.CountDistinct, "c_002")),
			new Metric("ticket_promedio", "Ticket promedio", new Ratio("ventas", "facturas")),
			new Metric("precio_promedio", "Precio promedio por unidad", new Ratio("ventas", "unidades")),
		],
		[
			new Dimension("sucursal", "Sucursal", "c_003"),
			new Dimension("categoria", "Categoría", "c_004"),
			new Dimension("producto", "Producto", "c_005"),
		],
		new LocalDate(2026, 10, 15));

	private static ColumnProfile Profile(int distinct, params string[] samples) => new(distinct, samples);
}
