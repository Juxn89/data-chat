namespace DataChat.Domain.Datasets;

using System.Text;

/// <summary>
/// Where a sheet's header is, and what its columns are called. Real exports carry a title, a date or a blank line
/// above the header; the header is the first row that looks like one.
/// </summary>
public static class Sheet
{
	/// <summary>How many rows from the top are searched for the header.</summary>
	public const int HeaderSearchRows = 20;

	/// <summary>The longest name a column keeps; longer names are cut, not rejected.</summary>
	public const int MaxNameLength = 60;

	/// <summary>A header names at least this share of the widest row's columns.</summary>
	private const double MinHeaderFill = 0.5;

	/// <summary>
	/// The header row: among the first <see cref="HeaderSearchRows"/> rows, the first one filled at least halfway
	/// across the widest row, made only of text that is not a number or a date, and followed by a row with data.
	/// </summary>
	/// <param name="rows">The sheet's rows.</param>
	/// <returns>The header's index, or null when there is none.</returns>
	public static int? FindHeader(IReadOnlyList<IReadOnlyList<RawCell>> rows)
	{
		ArgumentNullException.ThrowIfNull(rows);
		var top = rows.Take(HeaderSearchRows).ToArray();
		var widest = top.Select(Filled).DefaultIfEmpty(0).Max();
		if (widest == 0)
		{
			return null;
		}

		for (var i = 0; i < top.Length - 1; i++)
		{
			var row = top[i];
			var filled = Filled(row);
			if (filled >= Math.Max(1, widest * MinHeaderFill)
				&& row.Where(static cell => !cell.IsEmpty).All(IsLabel)
				&& Filled(rows[i + 1]) > 0)
			{
				return i;
			}
		}

		return null;
	}

	/// <summary>
	/// The names people see for a header's columns: control characters removed, blanks collapsed, cut at
	/// <see cref="MaxNameLength"/>; a blank header becomes "Columna n" and a repeated one gets " (2)". These are data
	/// for people and for the model's context, never part of any SQL.
	/// </summary>
	/// <param name="header">The header's cells, one per column.</param>
	/// <returns>The names, unique.</returns>
	public static IReadOnlyList<string> ColumnNames(IReadOnlyList<RawCell> header)
	{
		ArgumentNullException.ThrowIfNull(header);
		var names = new List<string>(header.Count);
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < header.Count; i++)
		{
			var name = Clean(header[i].Text ?? string.Empty);
			if (name.Length == 0)
			{
				name = $"Columna {i + 1}";
			}

			var unique = name;
			for (var n = 2; !seen.Add(unique); n++)
			{
				unique = $"{name} ({n})";
			}

			names.Add(unique);
		}

		return names;
	}

	/// <summary>The physical column name: never derived from the header, so a header cannot reach the SQL.</summary>
	/// <param name="ordinal">The column's position, from 1.</param>
	/// <returns>E.g. <c>c_001</c>.</returns>
	public static string PhysicalName(int ordinal) => $"c_{ordinal:D3}";

	private static int Filled(IReadOnlyList<RawCell> row) => row.Count(static cell => !cell.IsEmpty);

	private static bool IsLabel(RawCell cell) =>
		cell.Text is not null && ColumnTypes.Infer([cell]).Type == ColumnType.Text;

	private static string Clean(string text)
	{
		var clean = new StringBuilder(text.Length);
		var blank = false;
		foreach (var c in text)
		{
			if (char.IsWhiteSpace(c) || char.IsControl(c))
			{
				blank = clean.Length > 0;
				continue;
			}

			if (blank)
			{
				clean.Append(' ');
				blank = false;
			}

			clean.Append(c);
		}

		return clean.Length > MaxNameLength ? clean.ToString(0, MaxNameLength).TrimEnd() : clean.ToString();
	}
}
