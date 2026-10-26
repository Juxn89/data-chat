namespace DataChat.Evals;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// A run against the real model (plan §5), guarded like booking-agent's: it reads the shared ledger first and stops
/// before the session's cap, appends one row with what it really spent, and writes the results, failures included,
/// to <c>evals/results/</c>.
/// </summary>
internal static class PaidRun
{
	/// <summary>The session in <c>~/.anthropic-budget/ledger.tsv</c>.</summary>
	public const string Session = "workana-data-chat";

	/// <summary>The session's cap (README of the ledger, confirmed by the user on 23 Oct 2026).</summary>
	private const decimal SessionCapUsd = 0.50m;

	/// <summary>What one question can cost at most: two calls of 4K tokens in and 2K out with Haiku 5.5.</summary>
	private const decimal WorstQuestionUsd = 0.003m;

	private const string KeyVariable = "ANTHROPIC_API_KEY";

	public static string LedgerPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".anthropic-budget", "ledger.tsv");

	/// <summary>The key, from the environment or the repo's git-ignored .env; never printed.</summary>
	/// <returns>The key, or null.</returns>
	public static string? ApiKey() =>
		Environment.GetEnvironmentVariable(KeyVariable) is { Length: > 0 } key
			? key
			: File.Exists(".env")
				? File.ReadLines(".env")
					.Select(static line => line.Trim())
					.Where(static line => line.StartsWith(KeyVariable + "=", StringComparison.Ordinal))
					.Select(static line => line[(KeyVariable.Length + 1)..].Trim().Trim('"'))
					.LastOrDefault(static value => value.Length > 0)
				: null;

	/// <summary>What the session has spent so far, from the ledger.</summary>
	/// <returns>USD.</returns>
	public static decimal SessionSpent() =>
		File.Exists(LedgerPath)
			? File.ReadLines(LedgerPath).Skip(1)
				.Select(static line => line.Split('\t'))
				.Where(static cells => cells.Length > 10 && cells[1] == Session)
				.Sum(static cells => decimal.Parse(cells[10], CultureInfo.InvariantCulture))
			: 0m;

	/// <summary>Whether one more question still fits under the cap.</summary>
	/// <param name="spentThisRun">What this run has spent so far.</param>
	/// <param name="spentBefore">What the session had spent before this run.</param>
	/// <returns>Whether to go on.</returns>
	public static bool Affordable(decimal spentThisRun, decimal spentBefore) =>
		spentBefore + spentThisRun + WorstQuestionUsd <= SessionCapUsd;

	/// <summary>Appends this run's row to the ledger.</summary>
	/// <param name="experiment">What was run.</param>
	/// <param name="model">The model.</param>
	/// <param name="results">The results.</param>
	/// <param name="note">A note.</param>
	public static void Record(string experiment, string model, IReadOnlyList<(Question Question, AppResult Result)> results, string note)
	{
		var row = string.Join('\t',
		[
			DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
			Session,
			"data-chat",
			experiment,
			model,
			results.Sum(static r => r.Result.Calls).ToString(CultureInfo.InvariantCulture),
			results.Sum(static r => r.Result.InputTokens).ToString(CultureInfo.InvariantCulture),
			"0",
			"0",
			results.Sum(static r => r.Result.OutputTokens).ToString(CultureInfo.InvariantCulture),
			results.Sum(static r => r.Result.CostUsd).ToString("0.000000", CultureInfo.InvariantCulture),
			note,
		]);
		File.AppendAllText(LedgerPath, row + "\n");
	}

	/// <summary>Writes the results file.</summary>
	/// <param name="path">Where.</param>
	/// <param name="model">The model.</param>
	/// <param name="results">The results.</param>
	/// <param name="stoppedEarly">The cap stopped the run before the last question.</param>
	public static void Write(string path, string model, IReadOnlyList<(Question Question, AppResult Result, bool Correct, bool SameAsGold)> results, bool stoppedEarly)
	{
		decimal Ratio(IEnumerable<bool> items) => items.Any() ? decimal.Round(items.Count(static x => x) / (decimal)items.Count(), 3) : 0m;

		var summary = new JsonObject
		{
			["model"] = model,
			["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			["questions"] = results.Count,
			["stoppedEarly"] = stoppedEarly,
			["accuracy"] = Ratio(results.Select(static r => r.Correct)),
			["accuracySpanish"] = Ratio(results.Where(static r => r.Question.Language == "es").Select(static r => r.Correct)),
			["accuracyEnglish"] = Ratio(results.Where(static r => r.Question.Language == "en").Select(static r => r.Correct)),
			["sameAsGold"] = Ratio(results.Select(static r => r.SameAsGold)),
			["askedBack"] = results.Count(static r => r.Result.Clarification is not null),
			["failed"] = results.Count(static r => r.Result.Error is not null),
			["totalCostUsd"] = results.Sum(static r => r.Result.CostUsd),
			["averageCostUsd"] = results.Count > 0 ? decimal.Round(results.Average(static r => r.Result.CostUsd), 6) : 0m,
			["averageLatencyMs"] = results.Count > 0 ? (long)results.Average(static r => r.Result.LatencyMs) : 0,
			["calls"] = results.Sum(static r => r.Result.Calls),
			["items"] = new JsonArray([.. results.Select(static r => (JsonNode)new JsonObject
			{
				["id"] = r.Question.Id,
				["correct"] = r.Correct,
				["sameAsGold"] = r.SameAsGold,
				["interpretation"] = r.Result.Interpretation,
				["clarification"] = r.Result.Clarification,
				["error"] = r.Result.Error,
				["query"] = r.Result.Query is null ? null : JsonNode.Parse(r.Result.Query),
				["costUsd"] = r.Result.CostUsd,
				["latencyMs"] = r.Result.LatencyMs,
			})]),
		};

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
	}
}
