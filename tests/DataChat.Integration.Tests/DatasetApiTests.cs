namespace DataChat.Integration.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

[Collection(PostgresCollection.Name)]
public sealed class DatasetApiTests(PostgresFixture postgres)
{
	[Fact]
	public async Task An_uploaded_workbook_is_listed_and_described()
	{
		using var client = postgres.Api.CreateClient();
		using var form = Upload("sales-demo.xlsx", File.ReadAllBytes(Path.Combine(PostgresFixture.SamplesDirectory, "sales-demo.xlsx")));
		form.Add(new StringContent("Sales from the API test"), "name");

		using var created = await client.PostAsync(new Uri("/v1/datasets", UriKind.Relative), form);
		var body = await created.Content.ReadFromJsonAsync<JsonElement>();

		Assert.Equal(HttpStatusCode.Created, created.StatusCode);
		Assert.Equal(37_636, body.GetProperty("rowCount").GetInt32());
		Assert.Equal("Date", body.GetProperty("columns")[0].GetProperty("type").GetString());
		Assert.False(body.GetProperty("columns")[0].TryGetProperty("physicalName", out _));

		var id = body.GetProperty("id").GetString();
		var list = await client.GetFromJsonAsync<JsonElement>(new Uri("/v1/datasets", UriKind.Relative));
		Assert.Contains(list.EnumerateArray(), dataset => dataset.GetProperty("id").GetString() == id);

		var one = await client.GetFromJsonAsync<JsonElement>(new Uri($"/v1/datasets/{id}", UriKind.Relative));
		Assert.Equal("Sales from the API test", one.GetProperty("name").GetString());
	}

	[Fact]
	public async Task An_unknown_dataset_is_a_404_with_its_code()
	{
		using var client = postgres.Api.CreateClient();

		using var response = await client.GetAsync(new Uri($"/v1/datasets/{Guid.NewGuid()}", UriKind.Relative));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
		Assert.Equal("dataset.not_found", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
	}

	[Fact]
	public async Task An_unsupported_file_is_a_400_with_its_code()
	{
		using var client = postgres.Api.CreateClient();
		using var form = Upload("informe.pdf", "%PDF-1.7"u8.ToArray());

		using var response = await client.PostAsync(new Uri("/v1/datasets", UriKind.Relative), form);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Equal("import.unsupported_format", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
	}

	private static MultipartFormDataContent Upload(string fileName, byte[] content)
	{
		var file = new ByteArrayContent(content);
		file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
		return new MultipartFormDataContent { { file, "file", fileName } };
	}
}
