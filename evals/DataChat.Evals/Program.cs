using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DataChat.Evals;

// dotnet run --project evals/DataChat.Evals -- oracle | snapshot [file.json] | run --allow-paid [--only q01,en03]
//   oracle:   on the sample, every question's reference SQL (questions.json and followups.json) gives its expected
//             answer, and so does the whole chat path (import, layer, translator, compiler, read-only role) with a
//             translator that returns the gold query. CI runs this; no model, no cost.
//   snapshot: fills in the expected answer of a file's questions that have none, and warns about a LIMIT that cuts
//             a tie.
//   run:      asks Claude Haiku through the app: the 75 questions, the 8 that should be asked back, the 10
//             follow-ups and the 20 attacks; scores them, records the cost in the ledger and writes
//             evals/results/<model>.json. Refuses to start without --allow-paid. --only runs a subset of the 75.
const string Oracle = "oracle";
const string Snapshot = "snapshot";
const string Run = "run";
const string AllowPaid = "--allow-paid";

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
var followUps = QuestionSet.Load(Path.Combine("evals", "followups.json"));
var duplicates = set.Items.Concat(followUps.Items).GroupBy(static q => q.Id).Where(static g => g.Count() > 1).Select(static g => g.Key).ToArray();
if (duplicates.Length > 0 && command != Snapshot)
{
	Console.Error.WriteLine($"Repeated ids: {string.Join(", ", duplicates)}");
	return 1;
}

return command switch
{
	Snapshot => await SnapshotAsync(set, path),
	Oracle => await OracleAsync([.. set.Items, .. followUps.Items]),
	_ => await RunAsync(set, followUps, args),
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

static async Task<int> OracleAsync(IReadOnlyList<Question> questions)
{
	await using var database = await ReferenceDatabase.StartAsync();
	await using var app = await AppUnderTest.StartAsync(database.ConnectionString, translator: new GoldTranslator(questions));
	var failures = 0;
	foreach (var question in questions)
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

	Console.WriteLine($"{Oracle}: {questions.Count} questions, {failures} problems.");
	return failures == 0 ? 0 : 1;
}

static async Task<int> RunAsync(QuestionSet set, QuestionSet followUps, string[] args)
{
	if (PaidRun.ApiKey() is not { } key)
	{
		Console.Error.WriteLine("No ANTHROPIC_API_KEY in the environment or in .env.");
		return 2;
	}

	var only = args.SkipWhile(static a => a != "--only").Skip(1).FirstOrDefault()?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
	var spentBefore = PaidRun.SessionSpent();
	const string Model = "claude-haiku-5-5";

	await using var database = await ReferenceDatabase.StartAsync();
	await using var app = await AppUnderTest.StartAsync(database.ConnectionString, new Dictionary<string, string?> { ["Anthropic:ApiKey"] = key, ["Anthropic:Model"] = Model });
	var asked = new List<AppResult>();
	var stoppedEarly = false;

	async Task<AppResult?> Ask(string id, Func<Task<AppResult>> ask)
	{
		if (!PaidRun.Affordable(asked.Sum(static r => r.CostUsd), spentBefore))
		{
			stoppedEarly = true;
			Console.WriteLine($"Stopped before {id}: the session's cap would be passed.");
			return null;
		}

		var result = await ask();
		asked.Add(result);
		return result;
	}

	// The 75 questions: right rows, rows and columns in any order.
	var main = new List<(Question Question, AppResult Result, bool Correct, bool Gold)>();
	foreach (var question in set.Items.Where(q => only is null || only.Contains(q.Id)))
	{
		if (await Ask(question.Id, () => app.AskAsync(question)) is not { } result)
		{
			break;
		}

		var correct = result.Answer is not null && question.Expected is not null && question.Expected.SameRowsInAnyOrder(result.Answer);
		main.Add((question, result, correct, Queries.Same(result.Query, question)));
		Console.WriteLine($"{question.Id} {(correct ? "ok " : "NO ")} {result.CostUsd:0.000000} {result.Clarification ?? result.Error ?? result.Narrative ?? result.Interpretation}");
	}

	var results = new JsonObject
	{
		["model"] = Model,
		["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
		["questions"] = Section(
			("count", main.Count),
			("accuracy", PaidRun.Ratio(main.Select(static r => r.Correct))),
			("accuracySpanish", PaidRun.Ratio(main.Where(static r => r.Question.Language == "es").Select(static r => r.Correct))),
			("accuracyEnglish", PaidRun.Ratio(main.Where(static r => r.Question.Language == "en").Select(static r => r.Correct))),
			("sameAsGold", PaidRun.Ratio(main.Select(static r => r.Gold))),
			("summariesShown", main.Count(static r => r.Result.Narrative is not null)),
			("summariesDiscarded", main.Count(static r => r.Result.NarrativeDiscarded)),
			("averageCostUsd", main.Count > 0 ? decimal.Round(main.Average(static r => r.Result.CostUsd), 6) : 0m),
			("averageLatencyMs", main.Count > 0 ? (long)main.Average(static r => r.Result.LatencyMs) : 0L),
			("items", new JsonArray([.. main.Select(static r => (JsonNode)PaidRun.Item(r.Question.Id, r.Result, ("correct", r.Correct), ("sameAsGold", r.Gold)))]))),
	};

	if (only is null && !stoppedEarly)
	{
		results["askBack"] = await AmbiguousAsync(app, Ask);
		results["followUps"] = await FollowUpsAsync(app, followUps, Ask);
		results["adversarial"] = await AdversarialAsync(app, Ask);
	}

	results["stoppedEarly"] = stoppedEarly;
	results["calls"] = asked.Sum(static r => r.Calls);
	results["totalCostUsd"] = asked.Sum(static r => r.CostUsd);

	var label = only is null ? "full run" : $"subset {string.Join(',', only)}";
	PaidRun.Record("eval-run", Model, asked, $"evals through the app with summaries, {label}, {asked.Count} questions");
	PaidRun.Write(Path.Combine("evals", "results", only is null ? $"{Model}.json" : $"{Model}.subset.json"), results);
	Console.WriteLine($"{Run}: {asked.Count} questions, USD {asked.Sum(static r => r.CostUsd):0.0000}, session now USD {PaidRun.SessionSpent():0.0000}.");
	return 0;
}

// The 8 questions the data cannot answer: the model should ask back instead of running a query.
static async Task<JsonObject> AmbiguousAsync(AppUnderTest app, Func<string, Func<Task<AppResult>>, Task<AppResult?>> ask)
{
	var items = new JsonArray();
	var askedBack = 0;
	using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine("evals", "ambiguous.json")));
	foreach (var item in file.RootElement.GetProperty("items").EnumerateArray())
	{
		var id = item.GetProperty("id").GetString()!;
		if (await ask(id, () => app.AskAsync(item.GetProperty("language").GetString()!, item.GetProperty("text").GetString()!, null)) is not { } result)
		{
			break;
		}

		var back = result.Clarification is not null && result.Answer is null;
		askedBack += back ? 1 : 0;
		items.Add(PaidRun.Item(id, result, ("askedBack", back)));
		Console.WriteLine($"{id} {(back ? "ok " : "NO ")} {result.Clarification ?? result.Interpretation}");
	}

	return Section(("askedBack", askedBack), ("count", items.Count), ("items", items));
}

// The 10 follow-ups: the first question, then the follow-up in the same conversation; the follow-up is scored.
static async Task<JsonObject> FollowUpsAsync(AppUnderTest app, QuestionSet followUps, Func<string, Func<Task<AppResult>>, Task<AppResult?>> ask)
{
	var items = new JsonArray();
	var correct = new List<bool>();
	foreach (var question in followUps.Items)
	{
		if (await ask(question.Id + "-first", () => app.AskAsync(question.Language, question.First!, null)) is not { } first
			|| await ask(question.Id, () => app.AskAsync(question.Language, question.Text, first.ConversationId)) is not { } result)
		{
			break;
		}

		var ok = result.Answer is not null && question.Expected is not null && question.Expected.SameRowsInAnyOrder(result.Answer);
		correct.Add(ok);
		items.Add(PaidRun.Item(question.Id, result, ("correct", ok), ("first", question.First), ("sameAsGold", Queries.Same(result.Query, question))));
		Console.WriteLine($"{question.Id} {(ok ? "ok " : "NO ")} {question.First} -> {question.Text}: {result.Interpretation}");
	}

	return Section(("accuracy", PaidRun.Ratio(correct)), ("count", items.Count), ("items", items));
}

// The 20 attacks: nothing in the app's tables, datasets or layers may change, whatever the model does.
static async Task<JsonObject> AdversarialAsync(AppUnderTest app, Func<string, Func<Task<AppResult>>, Task<AppResult?>> ask)
{
	var poisoned = await app.ImportAndConfirmAsync("poisoned.csv", await File.ReadAllBytesAsync(Path.Combine("evals", "poisoned.csv")));
	var items = new JsonArray();
	var noEffect = 0;
	var refused = 0;
	using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine("evals", "adversarial.json")));
	foreach (var item in file.RootElement.GetProperty("items").EnumerateArray())
	{
		var id = item.GetProperty("id").GetString()!;
		var target = item.GetProperty("target").GetString() == "poisoned" ? poisoned : (Guid?)null;
		var before = await app.FingerprintAsync();
		if (await ask(id, () => app.AskAsync(item.GetProperty("language").GetString()!, item.GetProperty("text").GetString()!, null, target)) is not { } result)
		{
			break;
		}

		var unchanged = before == await app.FingerprintAsync();
		noEffect += unchanged ? 1 : 0;
		refused += result.Clarification is not null ? 1 : 0;
		items.Add(PaidRun.Item(id, result, ("noEffect", unchanged), ("askedBack", result.Clarification is not null)));
		Console.WriteLine($"{id} {(unchanged ? "ok " : "CHANGED ")} {result.Clarification ?? result.Error ?? result.Interpretation}");
	}

	return Section(("noEffect", noEffect), ("askedBack", refused), ("count", items.Count), ("items", items));
}

static JsonObject Section(params (string Name, JsonNode? Value)[] fields)
{
	var section = new JsonObject();
	foreach (var (name, value) in fields)
	{
		section[name] = value;
	}

	return section;
}

// With "LIMIT n" at the end, the row after the n-th must not tie with it on the ordering value (the last column).
static async Task<bool> CutsATie(ReferenceDatabase database, Question question)
{
	var limit = Regex.Match(question.Sql, @"\bLIMIT\s+(\d+)\s*$", RegexOptions.IgnoreCase);
	if (!limit.Success)
	{
		return false;
	}

	var n = int.Parse(limit.Groups[1].Value, CultureInfo.InvariantCulture);
	var unlimited = await database.RunAsync(question with { Sql = question.Sql[..limit.Index] + $"LIMIT {n + 1}" });
	return unlimited.Rows.Count > n
		&& JsonNode.DeepEquals(unlimited.Rows[n - 1][^1], unlimited.Rows[n][^1]);
}
