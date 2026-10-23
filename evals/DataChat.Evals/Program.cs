using System.Text.RegularExpressions;
using DataChat.Evals;

// dotnet run --project evals/DataChat.Evals -- oracle | snapshot [questions.json]
//   oracle:   the reference SQL still gives every expected answer on the sample (CI runs this; no model, no cost).
//   snapshot: fills in the expected answer of questions that have none, and warns about a LIMIT that cuts a tie.
const string Oracle = "oracle";
const string Snapshot = "snapshot";

var command = args.Length > 0 ? args[0] : Oracle;
var path = args.Length > 1 ? args[1] : Path.Combine("evals", "questions.json");
if (command is not (Oracle or Snapshot))
{
	Console.Error.WriteLine($"Unknown command '{command}'. Use '{Oracle}' or '{Snapshot}'.");
	return 2;
}

var set = QuestionSet.Load(path);
var duplicates = set.Items.GroupBy(static q => q.Id).Where(static g => g.Count() > 1).Select(static g => g.Key).ToArray();
if (duplicates.Length > 0)
{
	Console.Error.WriteLine($"Repeated ids: {string.Join(", ", duplicates)}");
	return 1;
}

await using var database = await ReferenceDatabase.StartAsync();
var failures = 0;
var updated = new List<Question>();
foreach (var question in set.Items)
{
	var answer = await database.RunAsync(question);
	if (command == Snapshot)
	{
		if (await CutsATie(database, question))
		{
			Console.WriteLine($"{question.Id}: its LIMIT cuts a tie; the expected answer would be arbitrary. Reword it.");
			failures++;
		}

		updated.Add(question.Expected is null ? question with { Expected = answer } : question);
		continue;
	}

	if (question.Expected is null)
	{
		Console.WriteLine($"{question.Id}: no expected answer yet (run '{Snapshot}').");
		failures++;
	}
	else if (!question.Expected.SameRows(answer))
	{
		Console.WriteLine($"{question.Id}: the reference SQL no longer gives the expected answer.");
		failures++;
	}
}

if (command == Snapshot)
{
	(set with { Items = updated }).Save(path);
}

Console.WriteLine($"{command}: {set.Items.Count} questions, {failures} problems.");
return failures == 0 ? 0 : 1;

// With "LIMIT n" at the end, the row after the n-th must not tie with it on the ordering value (the last column).
static async Task<bool> CutsATie(ReferenceDatabase database, Question question)
{
	var limit = Regex.Match(question.Sql, @"\bLIMIT\s+(\d+)\s*$", RegexOptions.IgnoreCase);
	if (!limit.Success)
	{
		return false;
	}

	var n = int.Parse(limit.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
	var unlimited = await database.RunAsync(question with { Sql = question.Sql[..limit.Index] + $"LIMIT {n + 1}" });
	return unlimited.Rows.Count > n
		&& System.Text.Json.Nodes.JsonNode.DeepEquals(unlimited.Rows[n - 1][^1], unlimited.Rows[n][^1]);
}
