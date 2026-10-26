namespace DataChat.Evals;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DataChat.Domain.Semantics;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

/// <summary>
/// <c>evals/questions.json</c>: the questions with their reference SQL, written by hand against the reference table,
/// and the answer that SQL gives on the sample dataset. The app's answers are scored against <see cref="Expected"/>.
/// </summary>
/// <param name="Today">The sample's "today", which named periods resolve against.</param>
/// <param name="Items">The questions.</param>
internal sealed partial record QuestionSet(string Today, IReadOnlyList<Question> Items)
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
		IndentCharacter = '\t',
		IndentSize = 1,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	public static QuestionSet Load(string path) =>
		JsonSerializer.Deserialize<QuestionSet>(File.ReadAllText(path), Json)
			?? throw new InvalidOperationException($"{path} is empty.");

	/// <summary>Writes the set with every array of plain values on one line, so a row reads as a row in review.</summary>
	/// <param name="path">Where.</param>
	public void Save(string path) =>
		File.WriteAllText(path, PlainArray().Replace(JsonSerializer.Serialize(this, Json), static match => "[" + ElementBreak().Replace(match.Groups["items"].Value, ", ") + "]") + "\n");

	// An array whose items are all strings, numbers, true, false or null. A JSON string cannot hold a raw line break,
	// so joining on ",\n" never touches a string's content.
	[GeneratedRegex("""\[\r?\n\t+(?<items>(?:"(?:[^"\\]|\\.)*"|[-\w.+]+)(?:,\r?\n\t+(?:"(?:[^"\\]|\\.)*"|[-\w.+]+))*)\r?\n\t+\]""")]
	private static partial Regex PlainArray();

	[GeneratedRegex(""",\r?\n\t+""")]
	private static partial Regex ElementBreak();
}

/// <summary>One question.</summary>
/// <param name="Id">Stable id, e.g. <c>q01</c> or <c>en01</c>.</param>
/// <param name="Language">The question's language, which is also the dataset's: <c>es</c> or <c>en</c>.</param>
/// <param name="Text">The question as a person would ask it.</param>
/// <param name="Sql">The reference SQL; <c>{sales}</c> is the reference table in the question's language.</param>
/// <param name="Query">
/// The semantic query a perfect translation gives, written by hand: what the app's compiler must turn into the same
/// answer as the reference SQL, and what a model's translation is diagnosed against.
/// </param>
/// <param name="Expected">What the reference SQL returns on the sample, filled in by <c>snapshot</c>.</param>
internal sealed record Question(string Id, string Language, string Text, string Sql, JsonObject Query, Answer? Expected)
{
	private static readonly JsonSerializerOptions QueryJson = CreateQueryJson();

	/// <summary>The gold query as the domain type.</summary>
	/// <returns>The query.</returns>
	public SemanticQuery GoldQuery() =>
		Query.Deserialize<SemanticQuery>(QueryJson) ?? throw new InvalidOperationException($"{Id} has no query.");

	private static JsonSerializerOptions CreateQueryJson()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		options.Converters.Add(new JsonStringEnumConverter());
		options.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
		return options;
	}
}

/// <summary>A result: column names (for people; scoring compares values) and rows.</summary>
/// <param name="Columns">The column names.</param>
/// <param name="Rows">The rows; numbers as JSON numbers, dates and labels as strings.</param>
internal sealed record Answer(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<JsonNode?>> Rows)
{
	/// <summary>An answer from rows of plain values.</summary>
	/// <param name="columns">The column names.</param>
	/// <param name="rows">The rows.</param>
	/// <returns>The answer.</returns>
	public static Answer From(IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<object?>> rows) =>
		new(columns, rows.Select(static row => (IReadOnlyList<JsonNode?>)row.Select(Cell).ToArray()).ToArray());

	/// <summary>Numbers become JSON numbers (decimals keep their scale), dates ISO strings, NULL stays null.</summary>
	/// <param name="value">The value.</param>
	/// <returns>The JSON value.</returns>
	public static JsonNode? Cell(object? value) => value switch
	{
		null or DBNull => null,
		decimal number => JsonValue.Create(number),
		long number => JsonValue.Create(number),
		int number => JsonValue.Create(number),
		DateTime date => JsonValue.Create(date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
		DateOnly date => JsonValue.Create(date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
		string text => JsonValue.Create(text),
		_ => throw new InvalidOperationException($"A value of type {value.GetType().Name} has no JSON form here."),
	};

	/// <summary>
	/// Same rows with the same values, rows and columns in any order: how a model's answer is scored (plan §5, a
	/// comparison of sets), since "sales by branch and category" is right whichever way its rows are sorted and its
	/// columns laid out. Which rows a top-N keeps still has to match.
	/// </summary>
	/// <param name="other">The other answer.</param>
	/// <returns>Whether they match.</returns>
	public bool SameRowsInAnyOrder(Answer other) =>
		Rows.Count == other.Rows.Count
		&& Canonical(this).SequenceEqual(Canonical(other), StringComparer.Ordinal);

	/// <summary>Same rows with the same values, in the same order.</summary>
	/// <param name="other">The other answer.</param>
	/// <returns>Whether they match.</returns>
	public bool SameRows(Answer other) =>
		Rows.Count == other.Rows.Count
		&& Rows.Zip(other.Rows).All(static pair => pair.First.Count == pair.Second.Count
			&& pair.First.Zip(pair.Second).All(static cell => JsonNode.DeepEquals(cell.First, cell.Second)));

	// Numbers through decimal, so 12.50 and 12.5 are the same value.
	private static IEnumerable<string> Canonical(Answer answer) =>
		answer.Rows
			.Select(static row => string.Join('|', row.Select(static cell => cell is JsonValue value && value.TryGetValue<decimal>(out var number)
				? number.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture)
				: cell?.ToJsonString() ?? "null").Order(StringComparer.Ordinal)))
			.Order(StringComparer.Ordinal);
}
