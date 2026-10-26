namespace DataChat.Domain.Datasets;

using System.Globalization;
using NodaTime;

/// <summary>
/// What a column's values look like: how many distinct ones there are, and the most frequent ones. The semantic
/// layer uses it to tell a dimension (a few branches) from an identifier (thousands of invoices), and the model gets
/// the samples as context, so "Centro" in a question can be matched to the branch filter (plan §5).
/// </summary>
/// <param name="DistinctCount">How many distinct non-empty values.</param>
/// <param name="Samples">Up to <see cref="MaxSamples"/> values, most frequent first.</param>
public sealed record ColumnProfile(int DistinctCount, IReadOnlyList<string> Samples)
{
	/// <summary>How many sample values a column keeps (plan §5: up to 20 per dimension).</summary>
	public const int MaxSamples = 20;

	/// <summary>Profiles a column from its values.</summary>
	/// <param name="values">The column's values, as the import typed them; nulls are skipped.</param>
	/// <returns>The profile.</returns>
	public static ColumnProfile Of(IEnumerable<object?> values)
	{
		ArgumentNullException.ThrowIfNull(values);
		var counts = values
			.Where(static value => value is not null)
			.Select(static value => Text(value!))
			.GroupBy(static value => value, StringComparer.Ordinal)
			.Select(static group => (Value: group.Key, Count: group.Count()))
			.ToArray();

		var samples = counts
			.OrderByDescending(static entry => entry.Count)
			.ThenBy(static entry => entry.Value, StringComparer.Ordinal)
			.Take(MaxSamples)
			.Select(static entry => entry.Value)
			.ToArray();

		return new ColumnProfile(counts.Length, samples);
	}

	/// <summary>Dates as ISO, numbers with an invariant point: the same text whatever the server's culture.</summary>
	private static string Text(object value) => value switch
	{
		LocalDate date => date.ToString("uuuu-MM-dd", CultureInfo.InvariantCulture),
		IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
		_ => value.ToString() ?? string.Empty,
	};
}
