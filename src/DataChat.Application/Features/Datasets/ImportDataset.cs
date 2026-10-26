namespace DataChat.Application.Features.Datasets;

using System.ComponentModel.DataAnnotations;
using DataChat.Application.Common;
using DataChat.Domain.Datasets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;

/// <summary>The import's limits (plan §0). They bound the physical table and the time inference takes.</summary>
public sealed class ImportOptions
{
	/// <summary>The configuration section.</summary>
	public const string SectionName = "Import";

	/// <summary>The largest file accepted, in bytes: 10 MB.</summary>
	[Range(1, int.MaxValue)]
	public int MaxFileBytes { get; set; } = 10 * 1024 * 1024;

	/// <summary>The most data rows accepted.</summary>
	[Range(1, int.MaxValue)]
	public int MaxRows { get; set; } = 200_000;

	/// <summary>The most columns accepted.</summary>
	[Range(1, 1000)]
	public int MaxColumns { get; set; } = 50;
}

/// <summary>A sheet as a reader returns it, before any header or type is decided.</summary>
/// <param name="Rows">Its rows, as many cells as the row has.</param>
/// <param name="PreferCommaDecimal">The file hints at comma decimals (a CSV separated by semicolons).</param>
public sealed record RawSheet(IReadOnlyList<IReadOnlyList<RawCell>> Rows, bool PreferCommaDecimal);

/// <summary>Why a file could not be read.</summary>
public enum ReadFailure
{
	/// <summary>Neither a CSV nor an Excel workbook.</summary>
	UnsupportedFormat,

	/// <summary>It has more rows than allowed; reading stopped there.</summary>
	TooManyRows,

	/// <summary>The file is broken.</summary>
	Unreadable,
}

/// <summary>Reads CSV and Excel files into a <see cref="RawSheet"/>.</summary>
public interface ISpreadsheetReader
{
	/// <summary>Reads a file's first sheet.</summary>
	/// <param name="content">The file's bytes.</param>
	/// <param name="fileName">Its name; the extension decides the format.</param>
	/// <param name="maxRows">Stop and fail past this many rows, header and title rows included.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The sheet, or why it could not be read.</returns>
	Task<(RawSheet? Sheet, ReadFailure? Failure)> ReadAsync(Stream content, string fileName, int maxRows, CancellationToken cancellationToken);
}

/// <summary>Where datasets live: their description and their own table.</summary>
public interface IDatasetStore
{
	/// <summary>Creates the dataset's table, loads its rows and records it, all or nothing.</summary>
	/// <param name="dataset">The dataset.</param>
	/// <param name="rows">Its rows, one value per column, already typed.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>A task.</returns>
	Task SaveAsync(Dataset dataset, IReadOnlyList<object?[]> rows, CancellationToken cancellationToken);

	/// <summary>All datasets, newest first.</summary>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The datasets.</returns>
	Task<IReadOnlyList<Dataset>> ListAsync(CancellationToken cancellationToken);

	/// <summary>One dataset.</summary>
	/// <param name="id">Its id.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The dataset, or null.</returns>
	Task<Dataset?> FindAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>A file to import.</summary>
/// <param name="FileName">The file's name.</param>
/// <param name="Length">Its size in bytes.</param>
/// <param name="Content">Its bytes.</param>
/// <param name="Name">The dataset's name; the file's name, without extension, when absent.</param>
public sealed record ImportRequest(string FileName, long Length, Stream Content, string? Name = null);

/// <summary>What an import produced, and what people should check.</summary>
/// <param name="Dataset">The dataset.</param>
/// <param name="AmbiguousDateColumns">Columns whose text dates fit day-first and month-first; day-first was used.</param>
public sealed record ImportResult(Dataset Dataset, IReadOnlyList<string> AmbiguousDateColumns);

/// <summary>
/// Imports a CSV or Excel file: finds the header, infers each column's type from all of its cells, and stores the
/// rows in a table of their own whose columns are named <c>c_001</c>… (ADR 0003).
/// </summary>
/// <param name="reader">The file reader.</param>
/// <param name="store">The store.</param>
/// <param name="options">The limits.</param>
/// <param name="clock">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed partial class ImportDataset(
	ISpreadsheetReader reader,
	IDatasetStore store,
	IOptions<ImportOptions> options,
	IClock clock,
	ILogger<ImportDataset> logger)
{
	/// <summary>Imports a file.</summary>
	/// <param name="request">The file.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The dataset, or why it could not be imported.</returns>
	public async Task<Result<ImportResult>> ExecuteAsync(ImportRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var limits = options.Value;
		if (request.Length == 0)
		{
			return ImportErrors.Empty;
		}

		if (request.Length > limits.MaxFileBytes)
		{
			return ImportErrors.TooLarge(limits.MaxFileBytes);
		}

		var (sheet, failure) = await reader.ReadAsync(request.Content, request.FileName, limits.MaxRows + Sheet.HeaderSearchRows, cancellationToken).ConfigureAwait(false);
		if (failure is not null || sheet is null)
		{
			return failure switch
			{
				ReadFailure.UnsupportedFormat => ImportErrors.UnsupportedFormat,
				ReadFailure.TooManyRows => ImportErrors.TooManyRows(limits.MaxRows),
				_ => ImportErrors.Unreadable,
			};
		}

		if (Sheet.FindHeader(sheet.Rows) is not { } headerIndex)
		{
			return ImportErrors.NoHeader;
		}

		var header = sheet.Rows[headerIndex];
		var data = sheet.Rows.Skip(headerIndex + 1).Where(static row => row.Any(static cell => !cell.IsEmpty)).ToArray();
		var width = Math.Max(header.Count, data.Select(static row => row.Count).DefaultIfEmpty(0).Max());
		if (width > limits.MaxColumns)
		{
			return ImportErrors.TooManyColumns(limits.MaxColumns);
		}

		if (data.Length > limits.MaxRows)
		{
			return ImportErrors.TooManyRows(limits.MaxRows);
		}

		var headerCells = Enumerable.Range(0, width).Select(i => i < header.Count ? header[i] : RawCell.Empty).ToArray();
		var names = Sheet.ColumnNames(headerCells);
		var types = Enumerable.Range(0, width)
			.Select(c => ColumnTypes.Infer(data.Select(row => Cell(row, c)).ToArray(), sheet.PreferCommaDecimal))
			.ToArray();

		var rows = data.Select(row => Enumerable.Range(0, width).Select(c => ColumnTypes.Read(Cell(row, c), types[c])).ToArray()).ToArray();
		var dataset = Dataset.Create(
			string.IsNullOrWhiteSpace(request.Name) ? Path.GetFileNameWithoutExtension(request.FileName) : request.Name,
			Path.GetFileName(request.FileName),
			names.Zip(types, static (name, type) => (name, type.Type)).ToArray(),
			rows.Length,
			clock.GetCurrentInstant());

		await store.SaveAsync(dataset, rows, cancellationToken).ConfigureAwait(false);
		LogImported(dataset.Id, dataset.RowCount, dataset.Columns.Count);
		var ambiguous = names.Zip(types).Where(static pair => pair.Second.AmbiguousDates).Select(static pair => pair.First).ToArray();
		return new ImportResult(dataset, ambiguous);
	}

	private static RawCell Cell(IReadOnlyList<RawCell> row, int column) => column < row.Count ? row[column] : RawCell.Empty;

	[LoggerMessage(Level = LogLevel.Information, Message = "Imported dataset {DatasetId}: {Rows} rows, {Columns} columns")]
	private partial void LogImported(Guid datasetId, int rows, int columns);
}

/// <summary>The import's errors; the codes are documented in <c>docs/errors.md</c>.</summary>
public static class ImportErrors
{
	/// <summary>An empty file.</summary>
	public static readonly Error Empty = Error.Validation("import.empty", "The file is empty.");

	/// <summary>Neither CSV nor Excel.</summary>
	public static readonly Error UnsupportedFormat = Error.Validation("import.unsupported_format", "Only CSV and Excel (.xlsx) files can be imported.");

	/// <summary>A broken file.</summary>
	public static readonly Error Unreadable = Error.Validation("import.unreadable", "The file could not be read; it may be damaged or protected.");

	/// <summary>No header found.</summary>
	public static readonly Error NoHeader = Error.Validation(
		"import.no_header",
		$"No header row was found in the first {Sheet.HeaderSearchRows} rows: the columns need names, and data under them.");

	/// <summary>Too large a file.</summary>
	/// <param name="maxBytes">The limit.</param>
	/// <returns>The error.</returns>
	public static Error TooLarge(int maxBytes) =>
		Error.Validation("import.too_large", $"The file is larger than {maxBytes / (1024 * 1024)} MB.");

	/// <summary>Too many rows.</summary>
	/// <param name="maxRows">The limit.</param>
	/// <returns>The error.</returns>
	public static Error TooManyRows(int maxRows) =>
		Error.Validation("import.too_many_rows", $"The file has more than {maxRows:N0} rows.");

	/// <summary>Too many columns.</summary>
	/// <param name="maxColumns">The limit.</param>
	/// <returns>The error.</returns>
	public static Error TooManyColumns(int maxColumns) =>
		Error.Validation("import.too_many_columns", $"The file has more than {maxColumns} columns.");
}
