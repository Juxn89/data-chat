namespace DataChat.Infrastructure.Tests;

using System.Text.Json;
using DataChat.Application.Features.Questions;
using DataChat.Domain.Periods;
using DataChat.Domain.Semantics;
using DataChat.Infrastructure.Ai;
using NodaTime;

/// <summary>The schema generated per dataset (ADR 0002), and what the model is told about the dataset.</summary>
public sealed class QuestionSchemaTests
{
	private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(QuestionSchema.For(Sample.Layer));

	[Fact]
	public void Metrics_and_dimensions_are_the_layers_keys_and_nothing_else()
	{
		var query = NonNull(Schema.GetProperty("properties").GetProperty("query"));

		Assert.Equal(Sample.Layer.Metrics.Select(static m => m.Key), Enum(query.GetProperty("properties").GetProperty("metrics").GetProperty("items")));
		Assert.Equal(Sample.Layer.Dimensions.Select(static d => d.Key), Enum(query.GetProperty("properties").GetProperty("dimensions").GetProperty("items")));
		Assert.Contains(SemanticQuery.TimeKey, Enum(NonNull(query.GetProperty("properties").GetProperty("sort")).GetProperty("properties").GetProperty("by")));
	}

	[Fact]
	public void Every_object_requires_all_its_properties_and_allows_no_others()
	{
		foreach (var obj in Objects(Schema))
		{
			Assert.False(obj.GetProperty("additionalProperties").GetBoolean());
			Assert.Equal(
				obj.GetProperty("properties").EnumerateObject().Select(static p => p.Name).Order(),
				obj.GetProperty("required").EnumerateArray().Select(static r => r.GetString()!).Order());
		}
	}

	[Fact]
	public void The_language_comes_before_the_sentences_written_in_it()
	{
		var properties = Schema.GetProperty("properties");

		Assert.Equal("language", properties.EnumerateObject().First().Name);
		Assert.Equal(QuestionSchema.Languages, Enum(properties.GetProperty("language")));
	}

	[Fact]
	public void A_dataset_without_dates_offers_no_periods_or_grains()
	{
		var schema = JsonSerializer.SerializeToElement(QuestionSchema.For(Sample.Layer with { TimeColumn = null }));
		var query = NonNull(schema.GetProperty("properties").GetProperty("query")).GetProperty("properties");

		Assert.Equal("null", query.GetProperty("grain").GetProperty("type").GetString());
		Assert.Equal("null", query.GetProperty("period").GetProperty("type").GetString());
	}

	[Fact]
	public void The_models_answer_reads_into_a_semantic_query()
	{
		const string json = """
			{"interpretation":"Sumé las ventas por sucursal del último trimestre.","clarification":null,
			 "query":{"metrics":["ventas"],"dimensions":["sucursal"],"grain":null,"filters":[{"dimension":"categoria","operator":"NotIn","values":["Limpieza"]}],
			          "period":{"named":"LastQuarter","from":null,"to":null},"compareTo":"SamePeriodLastYear","sort":null,"limit":null}}
			""";

		var answer = JsonSerializer.Deserialize<ModelAnswer>(json, QuestionSchema.Json)!;

		Assert.Equal(NamedPeriod.LastQuarter, answer.Query!.Period!.Named);
		Assert.Equal(Comparison.SamePeriodLastYear, answer.Query.CompareTo);
		Assert.Equal(FilterOperator.NotIn, answer.Query.FilterList[0].Operator);
		Assert.Empty(answer.Query.Validate(Sample.Layer));
	}

	[Fact]
	public void A_stored_query_has_the_query_shape_and_nothing_derived()
	{
		var json = JsonSerializer.Serialize(new SemanticQuery(["ventas"], [], Filters: [new("sucursal", FilterOperator.In, ["Centro"])]), QuestionSchema.Json);

		Assert.Contains("\"filters\"", json, StringComparison.Ordinal);
		Assert.DoesNotContain("filterList", json, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void The_model_sees_names_keys_and_sample_values_never_physical_columns()
	{
		var text = ClaudeQuestionTranslator.Describe(new TranslationRequest("¿?", Sample.Dataset, Sample.Layer, new LocalDate(2026, 10, 15), []));

		Assert.Contains("Today: 2026-10-15 (Thursday)", text, StringComparison.Ordinal);
		Assert.Contains("- ventas: \"Ventas\" (Sum of \"Total\")", text, StringComparison.Ordinal);
		Assert.Contains("- ticket_promedio: \"Ticket promedio\" (ventas divided by facturas)", text, StringComparison.Ordinal);
		Assert.Contains("most frequent: \"Centro\"", text, StringComparison.Ordinal);
		Assert.DoesNotContain("c_00", text, StringComparison.Ordinal);
		Assert.DoesNotContain("ds_", text, StringComparison.Ordinal);
	}

	private static JsonElement NonNull(JsonElement nullable) =>
		nullable.GetProperty("anyOf").EnumerateArray().First(static s => s.GetProperty("type").GetString() != "null");

	private static IEnumerable<string> Enum(JsonElement schema) => schema.GetProperty("enum").EnumerateArray().Select(static e => e.GetString()!);

	private static IEnumerable<JsonElement> Objects(JsonElement node)
	{
		if (node.ValueKind == JsonValueKind.Object)
		{
			if (node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "object")
			{
				yield return node;
			}

			foreach (var child in node.EnumerateObject().SelectMany(static p => Objects(p.Value)))
			{
				yield return child;
			}
		}
		else if (node.ValueKind == JsonValueKind.Array)
		{
			foreach (var child in node.EnumerateArray().SelectMany(Objects))
			{
				yield return child;
			}
		}
	}
}
