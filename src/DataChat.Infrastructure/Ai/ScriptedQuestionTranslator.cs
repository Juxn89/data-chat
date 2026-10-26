namespace DataChat.Infrastructure.Ai;

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DataChat.Application.Features.Questions;
using Microsoft.Extensions.Options;

/// <summary>Where the scripted translator reads its answers; set only for the end-to-end tests and the video's rehearsal.</summary>
public sealed class TranslatorScriptOptions
{
	/// <summary>The configuration section.</summary>
	public const string SectionName = "TranslatorScript";

	/// <summary>A JSON file: an array of <c>{ question, interpretation, clarification, query }</c>.</summary>
	[MinLength(1)]
	public string? File { get; set; }
}

/// <summary>
/// Answers questions from a script instead of the model, so the end-to-end tests run in CI without a key and at no
/// cost (lesson from booking-agent). Questions are matched by their text, ignoring case and surrounding blanks; any
/// other question gets a question back. Development and tests only: the API refuses to start with it elsewhere.
/// </summary>
/// <param name="options">The script's location.</param>
internal sealed class ScriptedQuestionTranslator(IOptions<TranslatorScriptOptions> options) : IQuestionTranslator
{
	private readonly Lazy<Dictionary<string, ModelAnswer>> _script = new(() => Load(options.Value.File!));

	public bool IsConfigured => true;

	public Task<Translation> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var answer = _script.Value.TryGetValue(Key(request.Question), out var scripted)
			? new Translation(scripted.Interpretation, scripted.Query, scripted.Clarification, TranslationUsage.None)
			: new Translation(string.Empty, null, "Esta pregunta no está en el guion.", TranslationUsage.None);
		return Task.FromResult(answer);
	}

	private static Dictionary<string, ModelAnswer> Load(string path)
	{
		using var document = JsonDocument.Parse(System.IO.File.ReadAllText(path));
		return document.RootElement.EnumerateArray().ToDictionary(
			static entry => Key(entry.GetProperty("question").GetString()!),
			static entry => entry.Deserialize<ModelAnswer>(QuestionSchema.Json)!);
	}

	private static string Key(string question) => question.Trim().ToUpperInvariant();
}
