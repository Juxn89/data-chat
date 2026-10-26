namespace DataChat.Infrastructure.Persistence;

using System.ComponentModel.DataAnnotations;
using DataChat.Application.Features.Datasets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
/// and the import is exercised on every run. Files already imported are skipped.
/// </summary>
public static class DemoDatasets
{
	/// <summary>The sample files and the names they get.</summary>
	private static readonly (string File, string Name)[] Samples =
	[
		("ventas-demo.xlsx", "Ventas · Distribuidora Demo"),
		("sales-demo.xlsx", "Sales · Distribuidora Demo"),
	];

	/// <summary>Imports the samples that are not in the database yet.</summary>
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
		var imported = 0;
		foreach (var (file, name) in Samples)
		{
			var path = Path.Combine(directory, file);
			if (!File.Exists(path) || await db.Datasets.AnyAsync(d => d.SourceFile == file, cancellationToken).ConfigureAwait(false))
			{
				continue;
			}

			await using var content = File.OpenRead(path);
			var result = await import.ExecuteAsync(new ImportRequest(file, content.Length, content, name), cancellationToken).ConfigureAwait(false);
			if (!result.IsSuccess)
			{
				throw new InvalidOperationException($"The sample {file} did not import: {result.Error!.Code}.");
			}

			imported++;
		}

		return imported;
	}
}
