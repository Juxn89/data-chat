namespace DataChat.Domain.Semantics;

using System.Text;
using System.Text.RegularExpressions;
using DataChat.Domain.Datasets;

/// <summary>
/// The layer the app proposes right after an import, for the person to confirm or rename (plan §2): the first date
/// column is the time; numbers are metrics that add up, unless their name says they are a price or a rate; text with
/// a manageable number of values is a dimension; columns where almost every value is different (invoice numbers)
/// become a count of distinct values.
/// </summary>
public static partial class Proposal
{
	/// <summary>A text column with more distinct values than this is an identifier, not something to group by.</summary>
	public const int MaxDimensionValues = 1000;

	/// <summary>A column whose values are this distinct across the rows identifies rows instead of describing them.</summary>
	private const double IdentifierRatio = 0.3;

	/// <summary>
	/// ...and has at least this many: in a file of ten rows, two salespeople are a high share but plainly a dimension.
	/// </summary>
	private const int MinIdentifierValues = 50;

	/// <summary>The longest key.</summary>
	private const int MaxKeyLength = 40;

	/// <summary>Proposes a layer for a dataset.</summary>
	/// <param name="dataset">The dataset.</param>
	/// <returns>The proposal, valid for the dataset.</returns>
	public static SemanticLayer For(Dataset dataset)
	{
		ArgumentNullException.ThrowIfNull(dataset);
		var time = dataset.Columns.FirstOrDefault(static column => column.Type == ColumnType.Date);
		var metrics = new List<Metric>();
		var dimensions = new List<Dimension>();
		var keys = new HashSet<string>(StringComparer.Ordinal);

		foreach (var column in dataset.Columns.Where(column => column != time))
		{
			var identifier = column.Profile.DistinctCount > dataset.RowCount * IdentifierRatio && column.Profile.DistinctCount >= MinIdentifierValues;
			switch (column.Type)
			{
				case ColumnType.Decimal:
					metrics.Add(new Metric(Key(column.Name, keys), column.Name, new Aggregate(AveragedName().IsMatch(column.Name) ? Aggregation.Average : Aggregation.Sum, column.PhysicalName)));
					break;
				case ColumnType.Integer when identifier:
				case ColumnType.Text when identifier || column.Profile.DistinctCount > MaxDimensionValues:
					metrics.Add(new Metric(Key(column.Name, keys), column.Name, new Aggregate(Aggregation.CountDistinct, column.PhysicalName)));
					break;
				case ColumnType.Integer:
					metrics.Add(new Metric(Key(column.Name, keys), column.Name, new Aggregate(AveragedName().IsMatch(column.Name) ? Aggregation.Average : Aggregation.Sum, column.PhysicalName)));
					break;
				case ColumnType.Text when column.Profile.DistinctCount > 0:
					dimensions.Add(new Dimension(Key(column.Name, keys), column.Name, column.PhysicalName));
					break;
				default:
					break;
			}
		}

		return new SemanticLayer(time?.PhysicalName, metrics, dimensions);
	}

	/// <summary>
	/// A plain key from a name: accents folded by hand (culture data is not guaranteed in a container), anything else
	/// that is not a letter or digit becomes <c>_</c>, and a number is added when the key is taken.
	/// </summary>
	/// <param name="name">The name.</param>
	/// <param name="taken">Keys already used; the new one is added.</param>
	/// <returns>The key.</returns>
	public static string Key(string name, ISet<string> taken)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(taken);
		var key = new StringBuilder();
		foreach (var c in name.ToLowerInvariant())
		{
			var folded = Fold(c);
			if (folded is >= 'a' and <= 'z' or >= '0' and <= '9')
			{
				key.Append(folded);
			}
			else if (key.Length > 0 && key[^1] != '_')
			{
				key.Append('_');
			}
		}

		var text = key.ToString().Trim('_');
		text = text.Length == 0 || !char.IsAsciiLetter(text[0]) ? $"m_{text}" : text;
		text = text.Length > MaxKeyLength ? text[..MaxKeyLength].TrimEnd('_') : text;
		var unique = text;
		for (var n = 2; !taken.Add(unique); n++)
		{
			unique = $"{text[..Math.Min(text.Length, MaxKeyLength - 3)]}_{n}";
		}

		return unique;
	}

	private static char Fold(char c) => c switch
	{
		'á' or 'à' or 'ä' or 'â' or 'ã' => 'a',
		'é' or 'è' or 'ë' or 'ê' => 'e',
		'í' or 'ì' or 'ï' or 'î' => 'i',
		'ó' or 'ò' or 'ö' or 'ô' or 'õ' => 'o',
		'ú' or 'ù' or 'ü' or 'û' => 'u',
		'ñ' => 'n',
		'ç' => 'c',
		_ => c,
	};

	// Prices, rates, percentages and averages do not add up across rows.
	[GeneratedRegex(@"precio|price|tarifa|rate|tasa|porcentaje|percent|%|margen|margin|promedio|average|media", RegexOptions.IgnoreCase)]
	private static partial Regex AveragedName();
}
