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

	/// <summary>What one question can cost at most: two reads and a summary, 4K tokens in and 2K out each, with Haiku 5.5.</summary>
	private const decimal WorstQuestionUsd = 0.005m;

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
	/// <param name="results">Every question asked in the run.</param>
	/// <param name="note">A note.</param>
	public static void Record(string experiment, string model, IReadOnlyList<AppResult> results, string note)
	{
		var row = string.Join('\t',
		[
			DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
			Session,
			"data-chat",
			experiment,
			model,
			results.Sum(static r => r.Calls).ToString(CultureInfo.InvariantCulture),
			results.Sum(static r => r.InputTokens).ToString(CultureInfo.InvariantCulture),
			"0",
			"0",
			results.Sum(static r => r.OutputTokens).ToString(CultureInfo.InvariantCulture),
			results.Sum(static r => r.CostUsd).ToString("0.000000", CultureInfo.InvariantCulture),
			note,
		]);
		File.AppendAllText(LedgerPath, row + "\n");
	}

	/// <summary>Writes a results file.</summary>
	/// <param name="path">Where.</param>
	/// <param name="results">The results.</param>
	public static void Write(string path, JsonObject results)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, results.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
	}

	/// <summary>A share, rounded to three decimals; 0 for nothing.</summary>
	/// <param name="items">The yes and no.</param>
	/// <returns>The share.</returns>
	public static decimal Ratio(IEnumerable<bool> items)
	{
		var all = items.ToArray();
		return all.Length == 0 ? 0m : decimal.Round(all.Count(static x => x) / (decimal)all.Length, 3);
	}

	/// <summary>One question's line in a results file.</summary>
	/// <param name="id">Its id.</param>
	/// <param name="result">What happened.</param>
	/// <param name="extra">Set-specific fields: correct, sameAsGold, noEffect…</param>
	/// <returns>The line.</returns>
	public static JsonObject Item(string id, AppResult result, params (string Name, JsonNode? Value)[] extra)
	{
		var item = new JsonObject { ["id"] = id };
		foreach (var (name, value) in extra)
		{
			item[name] = value;
		}

		item["interpretation"] = result.Interpretation;
		item["clarification"] = result.Clarification;
		item["error"] = result.Error;
		item["query"] = result.Query is null ? null : JsonNode.Parse(result.Query);
		item["narrative"] = result.Narrative;
		item["narrativeDiscarded"] = result.NarrativeDiscarded;
		item["costUsd"] = result.CostUsd;
		item["latencyMs"] = result.LatencyMs;
		return item;
	}
}
