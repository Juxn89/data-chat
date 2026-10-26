namespace DataChat.Infrastructure.Persistence;

using System.ComponentModel.DataAnnotations;
using DataChat.Application.Features.Datasets;
using DataChat.Application.Features.Semantics;
using DataChat.Domain.Datasets;
using DataChat.Domain.Semantics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NodaTime;

/// <summary>Where the sample dataset is, and whether to load it at startup.</summary>
public sealed class DemoOptions
{
	/// <summary>The configuration section.</summary>
	public const string SectionName = "Demo";

	/// <summary>The folder with <c>ventas-demo.xlsx</c> and <c>sales-demo.xlsx</c>; relative to the API's content root.</summary>
	[Required]
	public string SamplesDirectory { get; set; } = Path.Combine("..", "..", "samples");
}

/// <summary>
/// Loads the sample dataset in both languages, through the same import as any upload, so the demo starts with data
/// and the import is exercised on every run, then confirms its semantic layer as a person would. Files already
/// imported, and layers already confirmed, are left alone.
/// </summary>
public static class DemoDatasets
{
	/// <summary>The sample's "today": its last sale is on 30 Sep 2026 (evals/README.md).</summary>
	public static readonly LocalDate SampleToday = new(2026, 10, 15);

	/// <summary>The samples: file, name, and the names of the layer's metrics and dimensions in that language.</summary>
	private static readonly Sample[] Samples =
	[
		new("ventas-demo.xlsx", "Ventas · Distribuidora Demo", new Words(
			"Fecha", "Total", "Unidades", "Factura", "Sucursal", "Categoría", "Producto",
			"Ventas", "Unidades", "Facturas", "Ticket promedio", "Precio promedio por unidad", "Sucursal", "Categoría", "Producto")),
		new("sales-demo.xlsx", "Sales · Distribuidora Demo", new Words(
			"Date", "Total", "Units", "Invoice", "Branch", "Category", "Product",
			"Sales", "Units", "Invoices", "Average ticket", "Average price per unit", "Branch", "Category", "Product")),
	];

	/// <summary>Imports the samples that are not in the database yet and confirms their layers.</summary>
	/// <param name="services">The app's services.</param>
	/// <param name="contentRoot">The folder the samples' path is relative to.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>How many were imported.</returns>
	public static async Task<int> SeedAsync(IServiceProvider services, string contentRoot, CancellationToken cancellationToken = default)
	{
		await using var scope = services.CreateAsyncScope();
		var directory = Path.GetFullPath(Path.Combine(contentRoot, scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value.SamplesDirectory));
		var db = scope.ServiceProvider.GetRequiredService<DataChatDbContext>();
		var import = scope.ServiceProvider.GetRequiredService<ImportDataset>();
		var datasets = scope.ServiceProvider.GetRequiredService<IDatasetStore>();
		var layers = scope.ServiceProvider.GetRequiredService<SemanticLayers>();
		var imported = 0;
		foreach (var sample in Samples)
		{
			var path = Path.Combine(directory, sample.File);
			var existing = await db.Datasets.AsNoTracking().Where(d => d.SourceFile == sample.File).Select(static d => d.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
			if (existing.Count == 0 && File.Exists(path))
			{
				await using var content = File.OpenRead(path);
				var result = await import.ExecuteAsync(new ImportRequest(sample.File, content.Length, content, sample.Name), cancellationToken).ConfigureAwait(false);
				if (!result.IsSuccess)
				{
					throw new InvalidOperationException($"The sample {sample.File} did not import: {result.Error!.Code}.");
				}

				existing.Add(result.Value.Dataset.Id);
				imported++;
			}

			foreach (var id in existing)
			{
				if (await db.SemanticLayers.AnyAsync(l => l.DatasetId == id, cancellationToken).ConfigureAwait(false)
					|| await datasets.FindAsync(id, cancellationToken).ConfigureAwait(false) is not { } dataset)
				{
					continue;
				}

				var confirmed = await layers.ConfirmAsync(id, sample.Words.Layer(dataset), cancellationToken).ConfigureAwait(false);
				if (!confirmed.IsSuccess)
				{
					throw new InvalidOperationException($"The sample {sample.File}'s layer is not valid: {confirmed.Error!.Message}");
				}
			}
		}

		return imported;
	}

	private sealed record Sample(string File, string Name, Words Words);

	/// <summary>The sample's column names, and the names its layer gives metrics and dimensions, in one language.</summary>
	private sealed record Words(
		string DateColumn, string TotalColumn, string UnitsColumn, string InvoiceColumn, string BranchColumn, string CategoryColumn, string ProductColumn,
		string Sales, string Units, string Invoices, string AverageTicket, string AveragePrice, string Branch, string Category, string Product)
	{
		/// <summary>The layer a person would confirm for the sample: the proposal, renamed, plus two ratios.</summary>
		public SemanticLayer Layer(Dataset dataset)
		{
			var keys = new HashSet<string>(StringComparer.Ordinal);
			var sales = Proposal.Key(Sales, keys);
			var units = Proposal.Key(Units, keys);
			var invoices = Proposal.Key(Invoices, keys);
			return new SemanticLayer(
				Column(dataset, DateColumn),
				[
					new Metric(sales, Sales, new Aggregate(Aggregation.Sum, Column(dataset, TotalColumn))),
					new Metric(units, Units, new Aggregate(Aggregation.Sum, Column(dataset, UnitsColumn))),
					new Metric(invoices, Invoices, new Aggregate(Aggregation.CountDistinct, Column(dataset, InvoiceColumn))),
					new Metric(Proposal.Key(AverageTicket, keys), AverageTicket, new Ratio(sales, invoices)),
					new Metric(Proposal.Key(AveragePrice, keys), AveragePrice, new Ratio(sales, units)),
				],
				[
					new Dimension(Proposal.Key(Branch, keys), Branch, Column(dataset, BranchColumn)),
					new Dimension(Proposal.Key(Category, keys), Category, Column(dataset, CategoryColumn)),
					new Dimension(Proposal.Key(Product, keys), Product, Column(dataset, ProductColumn)),
				],
				SampleToday);
		}

		private static string Column(Dataset dataset, string name) =>
			dataset.Columns.Single(column => column.Name == name).PhysicalName;
	}
}
