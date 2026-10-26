namespace DataChat.Domain.Datasets;

using NodaTime;

/// <summary>
/// A cell as a file gives it. Excel cells come typed (a number, a date); CSV cells are always text, and the importer
/// decides per column what that text means.
/// </summary>
public readonly record struct RawCell
{
	private RawCell(string? text, decimal? number, LocalDate? date)
	{
		Text = text;
		Number = number;
		Date = date;
	}

	/// <summary>An empty cell.</summary>
	public static RawCell Empty { get; }

	/// <summary>The text, when the cell is text.</summary>
	public string? Text { get; }

	/// <summary>The number, when the file typed it as one.</summary>
	public decimal? Number { get; }

	/// <summary>The date, when the file typed it as one.</summary>
	public LocalDate? Date { get; }

	/// <summary>Nothing in it: no number, no date, and no text but blanks.</summary>
	public bool IsEmpty => Number is null && Date is null && string.IsNullOrWhiteSpace(Text);

	/// <summary>A text cell; blank text is an empty cell.</summary>
	/// <param name="text">The text.</param>
	/// <returns>The cell.</returns>
	public static RawCell FromText(string? text) => string.IsNullOrWhiteSpace(text) ? Empty : new(text.Trim(), null, null);

	/// <summary>A number the file typed as one.</summary>
	/// <param name="number">The number.</param>
	/// <returns>The cell.</returns>
	public static RawCell FromNumber(decimal number) => new(null, number, null);

	/// <summary>A date the file typed as one.</summary>
	/// <param name="date">The date.</param>
	/// <returns>The cell.</returns>
	public static RawCell FromDate(LocalDate date) => new(null, null, date);
}
