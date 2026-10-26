namespace DataChat.Infrastructure.Ai;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DataChat.Domain.Periods;
using DataChat.Domain.Semantics;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

/// <summary>The model's answer, as the schema shapes it.</summary>
/// <param name="Interpretation">How it read the question.</param>
/// <param name="Clarification">A question back, or null.</param>
/// <param name="Query">A semantic query, or null.</param>
internal sealed record ModelAnswer(string Interpretation, string? Clarification, SemanticQuery? Query);

/// <summary>
/// The JSON Schema the model answers with, generated per dataset (ADR 0002): its enums are this layer's metric and
/// dimension keys, so the model cannot name a metric, a dimension or a sort key that does not exist. Structured
/// outputs guarantee the shape; <see cref="SemanticQuery.Validate"/> still checks the rules a schema cannot express
/// (at most two groupings, a comparison needs a period).
/// </summary>
internal static class QuestionSchema
{
	/// <summary>How semantic queries are written as JSON: camelCase, enums by name, dates ISO.</summary>
	public static readonly JsonSerializerOptions Json = CreateJson();

	/// <summary>The schema for a layer.</summary>
	/// <param name="layer">The layer.</param>
	/// <returns>The schema, as the SDK takes it.</returns>
	public static Dictionary<string, JsonElement> For(SemanticLayer layer)
	{
		ArgumentNullException.ThrowIfNull(layer);
		var metrics = layer.Metrics.Select(static m => m.Key).ToArray();
		var dimensions = layer.Dimensions.Select(static d => d.Key).ToArray();
		var hasTime = layer.TimeColumn is not null;

		var query = new JsonObject
		{
			["metrics"] = Array(Enum(metrics), "The metrics asked for, by key: 1 to 4."),
			["dimensions"] = dimensions.Length > 0
				? Array(Enum(dimensions), "What to group by, by key: at most 2, counting the time grain.")
				: Array(new JsonObject { ["type"] = "string", ["enum"] = new JsonArray() }, "This dataset has no dimensions: always empty."),
			["grain"] = Nullable(hasTime ? Enum(Names<TimeGrain>()) : null, "Group by time at this grain, or null."),
			["filters"] = Array(
				Object(
					("dimension", dimensions.Length > 0 ? Enum(dimensions) : new JsonObject { ["type"] = "string", ["enum"] = new JsonArray() }),
					("operator", Enum(Names<FilterOperator>())),
					("values", Array(new JsonObject { ["type"] = "string" }, "Values exactly as they appear in the data."))),
				"Filters on dimensions."),
			["period"] = Nullable(
				hasTime
					? Object(
						("named", Nullable(Enum(Names<NamedPeriod>()), "A named period, or null when dates are given.")),
						("from", Nullable(new JsonObject { ["type"] = "string", ["format"] = "date" }, "First date, or null with a named period.")),
						("to", Nullable(new JsonObject { ["type"] = "string", ["format"] = "date" }, "Last date, or null with a named period.")))
					: null,
				"The dates, or null for all of them."),
			["compareTo"] = Nullable(hasTime ? Enum(Names<Comparison>()) : null, "Compare the period with another one, or null."),
			["sort"] = Nullable(
				Object(
					("by", Enum([.. metrics, .. dimensions, .. hasTime ? new[] { SemanticQuery.TimeKey } : []])),
					("direction", Enum(Names<SortDirection>()))),
				"The order, or null for the default."),
			["limit"] = Nullable(new JsonObject { ["type"] = "integer" }, "The most rows (top N), or null."),
		};

		var root = Object(
			("interpretation", new JsonObject { ["type"] = "string", ["description"] = "One short sentence, in the question's language, saying what is computed." }),
			("clarification", Nullable(new JsonObject { ["type"] = "string" }, "A question back, in the question's language, when it cannot be answered as asked; otherwise null.")),
			("query", Nullable(Object(query), "The semantic query, or null when asking back.")));

		return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(root.ToJsonString())!;
	}

	private static JsonObject Enum(IEnumerable<string> values) =>
		new() { ["type"] = "string", ["enum"] = new JsonArray([.. values.Select(static v => (JsonNode)JsonValue.Create(v)!)]) };

	private static string[] Names<T>()
		where T : struct, System.Enum => System.Enum.GetNames<T>();

	private static JsonObject Array(JsonObject items, string description) =>
		new() { ["type"] = "array", ["items"] = items, ["description"] = description };

	/// <summary>A value or null; with no value schema, only null (the dataset has no date column, say).</summary>
	private static JsonObject Nullable(JsonObject? schema, string description) =>
		schema is null
			? new JsonObject { ["type"] = "null", ["description"] = description }
			: new JsonObject { ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" }), ["description"] = description };

	/// <summary>An object whose properties are all required and nothing else is allowed, as structured outputs ask.</summary>
	private static JsonObject Object(params (string Name, JsonObject Schema)[] properties) => Object(new JsonObject(properties.Select(static p => KeyValuePair.Create(p.Name, (JsonNode?)p.Schema))));

	private static JsonObject Object(JsonObject properties) => new()
	{
		["type"] = "object",
		["properties"] = properties,
		["required"] = new JsonArray([.. properties.Select(static p => (JsonNode)JsonValue.Create(p.Key)!)]),
		["additionalProperties"] = false,
	};

	private static JsonSerializerOptions CreateJson()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		options.Converters.Add(new JsonStringEnumConverter());
		options.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
		return options;
	}
}
