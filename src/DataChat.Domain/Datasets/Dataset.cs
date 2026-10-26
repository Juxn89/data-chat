namespace DataChat.Domain.Datasets;

using NodaTime;

/// <summary>A column of an imported dataset.</summary>
/// <param name="Ordinal">Its position, from 1.</param>
/// <param name="PhysicalName">Its column in the dataset's table, e.g. <c>c_001</c>.</param>
/// <param name="Name">What people call it, from the file's header, cleaned.</param>
/// <param name="Type">What it holds.</param>
public sealed record DatasetColumn(int Ordinal, string PhysicalName, string Name, ColumnType Type);

/// <summary>
/// A spreadsheet imported into its own table (ADR 0003): one physical table per dataset, typed columns named
/// <c>c_001</c>, <c>c_002</c>… so nothing from the file ever becomes an identifier in SQL.
/// </summary>
public sealed class Dataset
{
	private const string TablePrefix = "ds_";

	private Dataset(Guid id, string name, string sourceFile, IReadOnlyList<DatasetColumn> columns, int rowCount, Instant importedAt)
	{
		Id = id;
		Name = name;
		SourceFile = sourceFile;
		Columns = columns;
		RowCount = rowCount;
		ImportedAt = importedAt;
	}

	/// <summary>The id.</summary>
	public Guid Id { get; }

	/// <summary>What people call it; the file's name unless they gave another.</summary>
	public string Name { get; }

	/// <summary>The file it came from.</summary>
	public string SourceFile { get; }

	/// <summary>The columns, in the file's order.</summary>
	public IReadOnlyList<DatasetColumn> Columns { get; }

	/// <summary>How many data rows it holds.</summary>
	public int RowCount { get; }

	/// <summary>When it was imported.</summary>
	public Instant ImportedAt { get; }

	/// <summary>Its table: <c>ds_</c> and the id's 32 hex digits, a valid identifier that needs no quoting.</summary>
	public string TableName => $"{TablePrefix}{Id:N}";

	/// <summary>A new dataset.</summary>
	/// <param name="name">Its name.</param>
	/// <param name="sourceFile">The file's name.</param>
	/// <param name="columns">The columns' names and types, in order.</param>
	/// <param name="rowCount">The number of data rows.</param>
	/// <param name="importedAt">Now.</param>
	/// <returns>The dataset.</returns>
	public static Dataset Create(string name, string sourceFile, IReadOnlyList<(string Name, ColumnType Type)> columns, int rowCount, Instant importedAt)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
		ArgumentNullException.ThrowIfNull(columns);
		if (columns.Count == 0)
		{
			throw new ArgumentException("A dataset has at least one column.", nameof(columns));
		}

		var built = columns.Select(static (column, i) => new DatasetColumn(i + 1, Sheet.PhysicalName(i + 1), column.Name, column.Type)).ToArray();
		return new Dataset(Guid.CreateVersion7(), name.Trim(), sourceFile, built, rowCount, importedAt);
	}

	/// <summary>A dataset read back from storage.</summary>
	/// <param name="id">The id.</param>
	/// <param name="name">The name.</param>
	/// <param name="sourceFile">The file's name.</param>
	/// <param name="columns">The columns.</param>
	/// <param name="rowCount">The number of rows.</param>
	/// <param name="importedAt">When it was imported.</param>
	/// <returns>The dataset.</returns>
	public static Dataset Restore(Guid id, string name, string sourceFile, IReadOnlyList<DatasetColumn> columns, int rowCount, Instant importedAt) =>
		new(id, name, sourceFile, columns, rowCount, importedAt);
}
