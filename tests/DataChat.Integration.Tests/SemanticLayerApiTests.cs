namespace DataChat.Integration.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DataChat.Infrastructure.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class SemanticLayerApiTests(PostgresFixture postgres)
{
	// Ten lines, two branches, every invoice different: a date, an identifier, a dimension and an amount.
	private const string Csv = """
		Fecha,Factura,Sucursal,Total
		2026-09-01,F-1,Centro,10.50
		2026-09-01,F-2,Altamira,4.00
		2026-09-02,F-3,Centro,7.25
		2026-09-02,F-4,Centro,3.00
		2026-09-03,F-5,Altamira,12.00
		2026-09-03,F-6,Centro,1.75
		2026-09-04,F-7,Altamira,8.00
		2026-09-04,F-8,Centro,6.50
		2026-09-05,F-9,Altamira,2.00
		2026-09-05,F-10,Centro,9.00
		""";

	[Fact]
	public async Task A_new_dataset_comes_with_a_proposal_to_confirm()
	{
		using var client = postgres.Api.CreateClient();
		var id = await UploadAsync(client);

		var layer = await client.GetFromJsonAsync<JsonElement>(Layer(id));

		Assert.False(layer.GetProperty("confirmed").GetBoolean());
		Assert.Equal(1, layer.GetProperty("timeColumn").GetInt32());
		Assert.Equal(["factura", "total"], layer.GetProperty("metrics").EnumerateArray().Select(static m => m.GetProperty("key").GetString()));
		Assert.Equal("CountDistinct", layer.GetProperty("metrics")[0].GetProperty("aggregation").GetString());
		Assert.Equal(["sucursal"], layer.GetProperty("dimensions").EnumerateArray().Select(static d => d.GetProperty("key").GetString()));
	}

	[Fact]
	public async Task A_renamed_layer_with_a_ratio_is_confirmed_and_kept()
	{
		using var client = postgres.Api.CreateClient();
		var id = await UploadAsync(client);

		using var put = await client.PutAsJsonAsync(Layer(id), new
		{
			timeColumn = 1,
			asOf = "2026-09-06",
			metrics = new object[]
			{
				new { key = "ventas", name = "Ventas", aggregation = "Sum", column = 4 },
				new { key = "facturas", name = "Facturas", aggregation = "CountDistinct", column = 2 },
				new { key = "ticket", name = "Ticket promedio", numerator = "ventas", denominator = "facturas" },
			},
			dimensions = new[] { new { key = "sucursal", name = "Sucursal", column = 3 } },
		});
		var again = await client.GetFromJsonAsync<JsonElement>(Layer(id));

		Assert.Equal(HttpStatusCode.OK, put.StatusCode);
		Assert.True(again.GetProperty("confirmed").GetBoolean());
		Assert.Equal("2026-09-06", again.GetProperty("asOf").GetString());
		Assert.Equal("Ticket promedio", again.GetProperty("metrics")[2].GetProperty("name").GetString());
		Assert.Equal("facturas", again.GetProperty("metrics")[2].GetProperty("denominator").GetString());
	}

	[Fact]
	public async Task An_invalid_layer_is_a_400_with_every_issue()
	{
		using var client = postgres.Api.CreateClient();
		var id = await UploadAsync(client);

		using var put = await client.PutAsJsonAsync(Layer(id), new
		{
			timeColumn = 3,
			metrics = new[] { new { key = "ventas", name = "Ventas", aggregation = "Sum", column = 3 } },
			dimensions = Array.Empty<object>(),
		});
		var problem = await put.Content.ReadFromJsonAsync<JsonElement>();

		Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
		Assert.Equal("semantic.invalid", problem.GetProperty("code").GetString());
		Assert.StartsWith("semantic.", problem.GetProperty("issue2").GetString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task A_column_the_dataset_lacks_is_a_400()
	{
		using var client = postgres.Api.CreateClient();
		var id = await UploadAsync(client);

		using var put = await client.PutAsJsonAsync(Layer(id), new
		{
			metrics = new[] { new { key = "ventas", name = "Ventas", aggregation = "Sum", column = 99 } },
			dimensions = Array.Empty<object>(),
		});

		Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
	}

	[Fact]
	public async Task The_seeded_samples_have_their_layers_confirmed()
	{
		await using var services = postgres.Services(new Dictionary<string, string?> { ["Demo:SamplesDirectory"] = PostgresFixture.SamplesDirectory });
		await DemoDatasets.SeedAsync(services, Directory.GetCurrentDirectory());
		using var client = postgres.Api.CreateClient();
		var datasets = await client.GetFromJsonAsync<JsonElement>(new Uri("/v1/datasets", UriKind.Relative));
		var english = datasets.EnumerateArray().First(static d => d.GetProperty("sourceFile").GetString() == "sales-demo.xlsx").GetProperty("id").GetString();

		var layer = await client.GetFromJsonAsync<JsonElement>(new Uri($"/v1/datasets/{english}/semantic-layer", UriKind.Relative));

		Assert.True(layer.GetProperty("confirmed").GetBoolean());
		Assert.Equal("2026-10-15", layer.GetProperty("asOf").GetString());
		Assert.Equal(["sales", "units", "invoices", "average_ticket", "average_price_per_unit"], layer.GetProperty("metrics").EnumerateArray().Select(static m => m.GetProperty("key").GetString()));
	}

	private static Uri Layer(string id) => new($"/v1/datasets/{id}/semantic-layer", UriKind.Relative);

	private static async Task<string> UploadAsync(HttpClient client)
	{
		var file = new ByteArrayContent(Encoding.UTF8.GetBytes(Csv));
		file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
		using var form = new MultipartFormDataContent { { file, "file", "ventas-septiembre.csv" } };
		using var response = await client.PostAsync(new Uri("/v1/datasets", UriKind.Relative), form);
		response.EnsureSuccessStatusCode();
		return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
	}
}
