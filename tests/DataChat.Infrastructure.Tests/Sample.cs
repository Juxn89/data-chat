namespace DataChat.Infrastructure.Tests;

using DataChat.Domain.Datasets;
using DataChat.Domain.Semantics;
using NodaTime;

/// <summary>A dataset shaped like the sample, and the layer the demo confirms for it.</summary>
internal static class Sample
{
	public static Dataset Dataset { get; } = Dataset.Create(
		"Ventas",
		"ventas-demo.xlsx",
		[
			("Fecha", ColumnType.Date, new ColumnProfile(548, [])),
			("Factura", ColumnType.Text, new ColumnProfile(15_147, [])),
			("Sucursal", ColumnType.Text, new ColumnProfile(4, ["Centro"])),
			("Categoría", ColumnType.Text, new ColumnProfile(6, ["Bebidas"])),
			("Producto", ColumnType.Text, new ColumnProfile(60, [])),
			("Unidades", ColumnType.Integer, new ColumnProfile(6, [])),
			("Precio unitario", ColumnType.Decimal, new ColumnProfile(120, [])),
			("Total", ColumnType.Decimal, new ColumnProfile(900, [])),
		],
		37_636,
		Instant.FromUtc(2026, 10, 26, 12, 0));

	public static string Table => Dataset.TableName;

	public static SemanticLayer Layer { get; } = new(
		"c_001",
		[
			new Metric("ventas", "Ventas", new Aggregate(Aggregation.Sum, "c_008")),
			new Metric("unidades", "Unidades", new Aggregate(Aggregation.Sum, "c_006")),
			new Metric("facturas", "Facturas", new Aggregate(Aggregation.CountDistinct, "c_002")),
			new Metric("precio_medio", "Precio medio", new Aggregate(Aggregation.Average, "c_007")),
			new Metric("lineas", "Líneas", new Aggregate(Aggregation.Count, null)),
			new Metric("ticket_promedio", "Ticket promedio", new Ratio("ventas", "facturas")),
		],
		[
			new Dimension("sucursal", "Sucursal", "c_003"),
			new Dimension("categoria", "Categoría", "c_004"),
			new Dimension("producto", "Producto", "c_005"),
		],
		new LocalDate(2026, 10, 15));
}
