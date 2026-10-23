namespace DataChat.Evals;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
/// <param name="Expected">What the reference SQL returns on the sample, filled in by <c>snapshot</c>.</param>
internal sealed record Question(string Id, string Language, string Text, string Sql, Answer? Expected);

/// <summary>A result: column names (for people; scoring compares values) and rows.</summary>
/// <param name="Columns">The column names.</param>
/// <param name="Rows">The rows; numbers as JSON numbers, dates and labels as strings.</param>
internal sealed record Answer(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<JsonNode?>> Rows)
{
	/// <summary>Same rows with the same values, in the same order.</summary>
	/// <param name="other">The other answer.</param>
	/// <returns>Whether they match.</returns>
	public bool SameRows(Answer other) =>
		Rows.Count == other.Rows.Count
		&& Rows.Zip(other.Rows).All(static pair => pair.First.Count == pair.Second.Count
			&& pair.First.Zip(pair.Second).All(static cell => JsonNode.DeepEquals(cell.First, cell.Second)));
}
