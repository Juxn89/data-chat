namespace DataChat.Infrastructure.Ai;

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using DataChat.Application.Features.Questions;
using DataChat.Domain.Datasets;
using DataChat.Domain.Semantics;
using Microsoft.Extensions.Options;

/// <summary>The model and what it costs, bound from the <c>Anthropic</c> section.</summary>
public sealed class AnthropicOptions
{
	/// <summary>The configuration section.</summary>
	public const string SectionName = "Anthropic";

	/// <summary>The API key. Optional at startup, so the app runs without questions; required on first use.</summary>
	public string? ApiKey { get; set; }

	/// <summary>Claude Haiku 5.5 for every call (AGENTS.md).</summary>
	[Required]
	public string Model { get; set; } = "claude-haiku-5-5";

	/// <summary>The answer is a small JSON object, plus Haiku 5.5's adaptive thinking, which counts toward this cap.</summary>
	[Range(256, 32_000)]
	public int MaxOutputTokens { get; set; } = 2048;

	/// <summary>Reading a question into a query needs little reasoning, so <c>low</c>, as in invoice-extractor.</summary>
	[Required]
	public string Effort { get; set; } = "low";

	/// <summary>USD per million input tokens: Haiku 5.5 for prompts up to 100K tokens (models reference, 23 Oct 2026).</summary>
	[Range(0, 100)]
	public decimal InputUsdPerMillionTokens { get; set; } = 0.10m;

	/// <summary>USD per million output tokens, thinking included, same source and date.</summary>
	[Range(0, 100)]
	public decimal OutputUsdPerMillionTokens { get; set; } = 0.50m;

	/// <summary>A translation should take seconds; past this something is stuck.</summary>
	[Range(typeof(TimeSpan), "00:00:05", "00:05:00")]
	public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Reads a question with Claude through structured outputs (ADR 0002): the answer is constrained to the schema
/// generated from the dataset's layer. The model sees the layer's names, keys and sample values, never the physical
/// columns or any rows.
/// </summary>
/// <param name="options">The options.</param>
internal sealed class ClaudeQuestionTranslator(IOptions<AnthropicOptions> options) : IQuestionTranslator, IDisposable
{
	private const string RefusalStopReason = "refusal";

	private const string MaxTokensStopReason = "max_tokens";

	private const decimal TokensPerMillion = 1_000_000m;

	private static readonly string SystemPrompt = PromptLibrary.Read("translate-question.md");

	private readonly Lazy<AnthropicClient> _client = new(() => new AnthropicClient
	{
		ApiKey = options.Value.ApiKey is { Length: > 0 } key
			? key
			: throw new InvalidOperationException("Anthropic:ApiKey is not set; questions need an API key."),
		Timeout = options.Value.Timeout,
	});

	public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

	public async Task<Translation> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var settings = options.Value;
		var response = await _client.Value.Messages.Create(
			new MessageCreateParams
			{
				Model = settings.Model,
				MaxTokens = settings.MaxOutputTokens,
				System = SystemPrompt,
				Thinking = new ThinkingConfigAdaptive(),
				OutputConfig = new OutputConfig
				{
					Effort = settings.Effort,
					Format = new JsonOutputFormat { Schema = QuestionSchema.For(request.Layer) },
				},
				Messages = [new() { Role = Role.User, Content = Content(request) }],
			},
			cancellationToken).ConfigureAwait(false);

		var usage = new TranslationUsage(
			settings.Model,
			response.Usage.InputTokens,
			response.Usage.OutputTokens,
			((response.Usage.InputTokens * settings.InputUsdPerMillionTokens) + (response.Usage.OutputTokens * settings.OutputUsdPerMillionTokens)) / TokensPerMillion);

		if (response.StopReason == RefusalStopReason)
		{
			return Failed(usage, "The model declined the question.");
		}

		if (response.StopReason == MaxTokensStopReason)
		{
			return Failed(usage, $"The answer was cut off at {settings.MaxOutputTokens} output tokens.");
		}

		// Thinking blocks may come first; the answer is the text.
		var json = new StringBuilder();
		foreach (var block in response.Content)
		{
			if (block.TryPickText(out var text))
			{
				json.Append(text.Text);
			}
		}

		try
		{
			var answer = JsonSerializer.Deserialize<ModelAnswer>(json.ToString(), QuestionSchema.Json);
			return answer is null
				? Failed(usage, "The answer was empty.")
				: new Translation(answer.Interpretation, answer.Query, answer.Clarification, usage);
		}
		catch (JsonException exception)
		{
			return Failed(usage, $"The answer is not valid JSON for the schema: {exception.Message}");
		}
	}

	public void Dispose()
	{
		if (_client.IsValueCreated)
		{
			_client.Value.Dispose();
		}
	}

	/// <summary>
	/// The dataset's description and the question go in separate blocks, each in its own tags: the question is
	/// material to read, never part of the instructions. A retry is a fresh single-turn request with the reasons the
	/// previous query was rejected, not a follow-up turn. In a conversation, the question before and its query come
	/// first, so "¿y en abril?" can change that query instead of starting from nothing.
	/// </summary>
	private static List<ContentBlockParam> Content(TranslationRequest request)
	{
		List<ContentBlockParam> content = [new TextBlockParam { Text = $"<dataset>\n{Describe(request)}\n</dataset>" }];
		if (request.Previous is { } previous)
		{
			// The previous period's dates, worked out here, so "el trimestre anterior" is counted from them, not guessed.
			var covered = previous.Query?.Period?.Resolve(request.Today) is { } period
				? $", which covered {period.Start.ToString("uuuu-MM-dd", CultureInfo.InvariantCulture)} to {period.End.ToString("uuuu-MM-dd", CultureInfo.InvariantCulture)}"
				: string.Empty;
			var read = previous.Query is null
				? "it was not read into a query"
				: $"it was read as {JsonSerializer.Serialize(previous.Query, QuestionSchema.Json)}{covered}";
			content.Add(new TextBlockParam
			{
				Text = $"<previous_question>\n{previous.Text}\n</previous_question>\n" +
					$"The question below follows that one in the same conversation; {read}. If it refers to it (\"¿y en abril?\", " +
					"\"solo Centro\", \"ahora en unidades\", \"and the month before?\"), change that query as asked and keep the rest; " +
					"if it stands on its own, read it on its own.",
			});
		}

		content.Add(new TextBlockParam { Text = $"<question>\n{request.Question}\n</question>" });

		if (request.Feedback.Count > 0)
		{
			var issues = string.Join('\n', request.Feedback.Select(static issue => $"- {issue.Message}"));
			content.Add(new TextBlockParam { Text = $"A previous reading of this question was rejected by the server:\n{issues}\nAnswer again, within those rules." });
		}

		return content;
	}

	/// <summary>The layer in words: names, keys, how each metric is computed, and each dimension's most frequent values.</summary>
	internal static string Describe(TranslationRequest request)
	{
		var layer = request.Layer;
		var text = new StringBuilder()
			.Append(CultureInfo.InvariantCulture, $"Name: {request.Dataset.Name}\n")
			.Append(CultureInfo.InvariantCulture, $"Today: {request.Today.ToString("uuuu-MM-dd", CultureInfo.InvariantCulture)} ({request.Today.DayOfWeek})\n")
			.Append(layer.TimeColumn is null ? "No date column: no periods or time grains.\n" : string.Empty)
			.Append("Metrics:\n");
		foreach (var metric in layer.Metrics)
		{
			text.Append(CultureInfo.InvariantCulture, $"- {metric.Key}: \"{metric.Name}\" ({Formula(metric, request)})\n");
		}

		text.Append("Dimensions:\n");
		foreach (var dimension in layer.Dimensions)
		{
			var profile = request.Dataset.Column(dimension.Column)?.Profile;
			var samples = profile is { Samples.Count: > 0 } ? string.Join(", ", profile.Samples.Select(static s => $"\"{s}\"")) : "none recorded";
			text.Append(CultureInfo.InvariantCulture, $"- {dimension.Key}: \"{dimension.Name}\" ({profile?.DistinctCount ?? 0} values; most frequent: {samples})\n");
		}

		return text.ToString().TrimEnd();
	}

	private static string Formula(Metric metric, TranslationRequest request) => metric.Formula switch
	{
		Aggregate { Aggregation: Aggregation.Count } => "count of rows",
		Aggregate aggregate => $"{aggregate.Aggregation} of \"{request.Dataset.Column(aggregate.Column!)?.Name}\"",
		Ratio ratio => $"{ratio.Numerator} divided by {ratio.Denominator}",
		_ => "unknown",
	};

	private static Translation Failed(TranslationUsage usage, string failure) => new(string.Empty, null, null, usage, failure);
}

/// <summary>Prompts are Markdown files embedded in this assembly, versioned and reviewed like code.</summary>
internal static class PromptLibrary
{
	/// <summary>Reads one prompt.</summary>
	/// <param name="name">The file name under <c>Ai/Prompts</c>.</param>
	/// <returns>Its text.</returns>
	public static string Read(string name)
	{
		var assembly = typeof(PromptLibrary).Assembly;
		var resource = assembly.GetManifestResourceNames().Single(r => r.EndsWith("." + name, StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(resource)!;
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd().Trim();
	}
}
