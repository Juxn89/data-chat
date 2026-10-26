using System.Text.RegularExpressions;
using DataChat.Evals;

// dotnet run --project evals/DataChat.Evals -- oracle | snapshot | run --allow-paid [--only q01,en03] [questions.json]
//   oracle:   on the sample, every question's reference SQL gives its expected answer, and so does the whole chat path
//             (import, layer, translator, compiler, read-only role) with a translator that returns the gold query.
//             CI runs this; no model, no cost.
//   snapshot: fills in the expected answer of questions that have none, and warns about a LIMIT that cuts a tie.
//   run:      asks Claude Haiku every question through the app, scores the answers, records the cost in the ledger
//             and writes evals/results/<model>.json. Refuses to start without --allow-paid.
const string Oracle = "oracle";
const string Snapshot = "snapshot";
const string Run = "run";
const string AllowPaid = "--allow-paid";
const string Only = "--only";

var command = args.Length > 0 ? args[0] : Oracle;
var path = args.Skip(1).FirstOrDefault(static a => a.EndsWith(".json", StringComparison.Ordinal)) ?? Path.Combine("evals", "questions.json");
if (command is not (Oracle or Snapshot or Run))
{
	Console.Error.WriteLine($"Unknown command '{command}'. Use '{Oracle}', '{Snapshot}' or '{Run}'.");
	return 2;
}

if (command == Run && !args.Contains(AllowPaid))
{
	Console.Error.WriteLine($"'{Run}' calls the model and costs money: add {AllowPaid}.");
	return 2;
}

var set = QuestionSet.Load(path);
var duplicates = set.Items.GroupBy(static q => q.Id).Where(static g => g.Count() > 1).Select(static g => g.Key).ToArray();
if (duplicates.Length > 0)
{
	Console.Error.WriteLine($"Repeated ids: {string.Join(", ", duplicates)}");
	return 1;
}

return command switch
{
	Snapshot => await SnapshotAsync(set, path),
	Oracle => await OracleAsync(set),
	_ => await RunAsync(set, args),
};

static async Task<int> SnapshotAsync(QuestionSet set, string path)
{
	await using var database = await ReferenceDatabase.StartAsync();
	var failures = 0;
	var updated = new List<Question>();
	foreach (var question in set.Items)
	{
		var answer = await database.RunAsync(question);
		if (await CutsATie(database, question))
		{
			Console.WriteLine($"{question.Id}: its LIMIT cuts a tie; the expected answer would be arbitrary. Reword it.");
			failures++;
		}

		updated.Add(question.Expected is null ? question with { Expected = answer } : question);
	}

	(set with { Items = updated }).Save(path);
	Console.WriteLine($"{Snapshot}: {set.Items.Count} questions, {failures} problems.");
	return failures == 0 ? 0 : 1;
}

static async Task<int> OracleAsync(QuestionSet set)
{
	await using var database = await ReferenceDatabase.StartAsync();
	await using var app = await AppUnderTest.StartAsync(database.ConnectionString, translator: new GoldTranslator(set.Items));
	var failures = 0;
	foreach (var question in set.Items)
	{
		if (question.Expected is null)
		{
			Console.WriteLine($"{question.Id}: no expected answer yet (run '{Snapshot}').");
			failures++;
			continue;
		}

		if (!question.Expected.SameRows(await database.RunAsync(question)))
		{
			Console.WriteLine($"{question.Id}: the reference SQL no longer gives the expected answer.");
			failures++;
			continue;
		}

		var result = await app.AskAsync(question);
		if (result.Answer is null || !question.Expected.SameRows(result.Answer))
		{
			Console.WriteLine(result.Error is null
				? $"{question.Id}: the gold query, through the app, does not give the expected answer."
				: $"{question.Id}: the gold query failed in the app with {result.Error}.");
			failures++;
		}
	}

	Console.WriteLine($"{Oracle}: {set.Items.Count} questions, {failures} problems.");
	return failures == 0 ? 0 : 1;
}

static async Task<int> RunAsync(QuestionSet set, string[] args)
{
	if (PaidRun.ApiKey() is not { } key)
	{
		Console.Error.WriteLine("No ANTHROPIC_API_KEY in the environment or in .env.");
		return 2;
	}

	var only = args.SkipWhile(static a => a != Only).Skip(1).FirstOrDefault()?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
	var questions = set.Items.Where(q => only is null || only.Contains(q.Id)).ToArray();
	var spentBefore = PaidRun.SessionSpent();
	const string Model = "claude-haiku-5-5";

	await using var database = await ReferenceDatabase.StartAsync();
	await using var app = await AppUnderTest.StartAsync(database.ConnectionString, new Dictionary<string, string?> { ["Anthropic:ApiKey"] = key, ["Anthropic:Model"] = Model });
	var results = new List<(Question, AppResult, bool, bool)>();
	var stoppedEarly = false;
	foreach (var question in questions)
	{
		if (!PaidRun.Affordable(results.Sum(static r => r.Item2.CostUsd), spentBefore))
		{
			stoppedEarly = true;
			Console.WriteLine($"Stopped before {question.Id}: the session's cap would be passed.");
			break;
		}

		var result = await app.AskAsync(question);
		var correct = result.Answer is not null && question.Expected is not null && question.Expected.SameRowsInAnyOrder(result.Answer);
		results.Add((question, result, correct, Queries.Same(result.Query, question)));
		Console.WriteLine($"{question.Id} {(correct ? "ok " : "NO ")} {result.CostUsd:0.000000} {result.Clarification ?? result.Error ?? result.Interpretation}");
	}

	var label = only is null ? "full run" : $"subset {string.Join(',', only)}";
	PaidRun.Record("day5-eval-run", Model, [.. results.Select(static r => (r.Item1, r.Item2))], $"day 5: evals through the app, {label}, {results.Count} questions");
	var output = only is null ? Path.Combine("evals", "results", $"{Model}.json") : Path.Combine("evals", "results", $"{Model}.subset.json");
	PaidRun.Write(output, Model, results, stoppedEarly);

	var accuracy = results.Count == 0 ? 0 : results.Count(static r => r.Item3) / (double)results.Count;
	Console.WriteLine($"{Run}: {results.Count} questions, accuracy {accuracy:P1}, USD {results.Sum(static r => r.Item2.CostUsd):0.0000}, session now USD {PaidRun.SessionSpent():0.0000}.");
	return 0;
}

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
