namespace DataChat.Infrastructure.Ai;

using System.Text;
using Anthropic;
using Anthropic.Models.Messages;
using DataChat.Application.Features.Questions;
using Microsoft.Extensions.Options;

/// <summary>
/// Summarizes an answer with Claude Haiku (plan §5, step 7). It only sees the aggregated rows and the figures derived
/// from them; whatever it writes is checked number by number before anyone reads it (<see cref="NarrationFacts"/>).
/// </summary>
/// <param name="options">The model's options, the same as the translator's.</param>
internal sealed class ClaudeNarrator(IOptions<AnthropicOptions> options) : INarrator, IDisposable
{
	/// <summary>Two sentences need little room, but adaptive thinking counts toward the cap too.</summary>
	private const int MaxOutputTokens = 1024;

	private const decimal TokensPerMillion = 1_000_000m;

	private static readonly string SystemPrompt = PromptLibrary.Read("narrate-answer.md");

	private readonly Lazy<AnthropicClient> _client = new(() => new AnthropicClient
	{
		ApiKey = options.Value.ApiKey is { Length: > 0 } key
			? key
			: throw new InvalidOperationException("Anthropic:ApiKey is not set; summaries need an API key."),
		Timeout = options.Value.Timeout,
	});

	public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

	public async Task<Narration> NarrateAsync(NarrationRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var settings = options.Value;
		var response = await _client.Value.Messages.Create(
			new MessageCreateParams
			{
				Model = settings.Model,
				MaxTokens = MaxOutputTokens,
				System = SystemPrompt,
				Thinking = new ThinkingConfigAdaptive(),
				OutputConfig = new OutputConfig { Effort = settings.Effort },
				Messages =
				[
					new()
					{
						Role = Role.User,
						Content = new List<ContentBlockParam>
						{
							new TextBlockParam { Text = $"<language>{request.Language ?? "the question's"}</language>" },
							new TextBlockParam { Text = $"<question>\n{request.Question}\n</question>" },
							new TextBlockParam { Text = $"<interpretation>\n{request.Interpretation}\n</interpretation>" },
							new TextBlockParam { Text = $"<facts>\n{request.Facts}\n</facts>" },
						},
					},
				],
			},
			cancellationToken).ConfigureAwait(false);

		var usage = new TranslationUsage(
			settings.Model,
			response.Usage.InputTokens,
			response.Usage.OutputTokens,
			((response.Usage.InputTokens * settings.InputUsdPerMillionTokens) + (response.Usage.OutputTokens * settings.OutputUsdPerMillionTokens)) / TokensPerMillion);

		var text = new StringBuilder();
		foreach (var block in response.Content)
		{
			if (block.TryPickText(out var part))
			{
				text.Append(part.Text);
			}
		}

		return new Narration(text.ToString().Trim() is { Length: > 0 } written ? written : null, usage);
	}

	public void Dispose()
	{
		if (_client.IsValueCreated)
		{
			_client.Value.Dispose();
		}
	}
}

/// <summary>No summaries: the scripted runs show the rows alone.</summary>
internal sealed class NoNarrator : INarrator
{
	public bool IsConfigured => false;

	public Task<Narration> NarrateAsync(NarrationRequest request, CancellationToken cancellationToken) =>
		Task.FromResult(new Narration(null, TranslationUsage.None));
}
