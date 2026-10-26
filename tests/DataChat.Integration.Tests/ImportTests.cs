namespace DataChat.Integration.Tests;

using System.Text;
using DataChat.Application.Common;
using DataChat.Application.Features.Datasets;
using DataChat.Domain.Datasets;
using DataChat.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

[Collection(PostgresCollection.Name)]
public sealed class ImportTests(PostgresFixture postgres)
{
	// The sample's pinned total (tests/DataChat.SampleData.Tests): the import must not lose or change a cent.
	private const decimal SampleTotal = 217_544.47m;

	private const int SampleRows = 37_636;

	[Fact]
	public async Task The_spanish_sample_imports_every_row_with_its_types()
	{
		var dataset = (await ImportFileAsync("ventas-demo.xlsx")).Value.Dataset;

		Assert.Equal(SampleRows, dataset.RowCount);
		Assert.Equal(["Fecha", "Factura", "Sucursal", "Categoría", "Producto", "Unidades", "Precio unitario", "Total"], dataset.Columns.Select(static c => c.Name));
		Assert.Equal(
			[ColumnType.Date, ColumnType.Text, ColumnType.Text, ColumnType.Text, ColumnType.Text, ColumnType.Integer, ColumnType.Decimal, ColumnType.Decimal],
			dataset.Columns.Select(static c => c.Type));
		Assert.Equal(SampleTotal, await postgres.ScalarAsync<decimal>($"SELECT sum(c_008) FROM {dataset.TableName}"));
		Assert.Equal(new DateOnly(2025, 4, 1), await postgres.ScalarAsync<DateOnly>($"SELECT min(c_001) FROM {dataset.TableName}"));
	}

	[Fact]
	public async Task The_english_sample_holds_the_same_sales()
	{
		var dataset = (await ImportFileAsync("sales-demo.xlsx")).Value.Dataset;

		Assert.Equal("Category", dataset.Columns[3].Name);
		Assert.Equal(SampleTotal, await postgres.ScalarAsync<decimal>($"SELECT sum(c_008) FROM {dataset.TableName}"));
	}

	[Fact]
	public async Task A_latin1_csv_with_semicolons_and_comma_decimals_is_read_as_its_author_meant()
	{
		var csv = Encoding.Latin1.GetBytes("Fecha;Sucursal;Monto\r\n30/09/2026;Peñas Blancas;1.234,50\r\n01/09/2026;Ñandú;12,00\r\n");

		var dataset = (await ImportAsync("ventas.csv", csv)).Value.Dataset;

		Assert.Equal([ColumnType.Date, ColumnType.Text, ColumnType.Decimal], dataset.Columns.Select(static c => c.Type));
		Assert.Equal(1246.50m, await postgres.ScalarAsync<decimal>($"SELECT sum(c_003) FROM {dataset.TableName}"));
		Assert.Equal("Peñas Blancas", await postgres.ScalarAsync<string>($"SELECT c_002 FROM {dataset.TableName} WHERE c_001 = '2026-09-30'"));
	}

	[Fact]
	public async Task A_malicious_header_stays_a_name_and_touches_nothing()
	{
		var before = await postgres.ScalarAsync<long>("SELECT count(*) FROM datasets");
		var csv = Encoding.UTF8.GetBytes("fecha,\"monto\"\"); DROP TABLE datasets; --\"\n2026-01-01,5\n");

		var dataset = (await ImportAsync("ataque.csv", csv)).Value.Dataset;

		Assert.Equal("monto\"); DROP TABLE datasets; --", dataset.Columns[1].Name);
		Assert.Equal("c_002", dataset.Columns[1].PhysicalName);
		Assert.Equal(before + 1, await postgres.ScalarAsync<long>("SELECT count(*) FROM datasets"));
	}

	[Theory]
	[InlineData("Import:MaxColumns", "2", "a,b,c\n1,2,3\n", "import.too_many_columns")]
	[InlineData("Import:MaxRows", "2", "a,b\n1,2\n3,4\n5,6\n", "import.too_many_rows")]
	[InlineData("Import:MaxFileBytes", "10", "a,b\n1,2\n3,4\n", "import.too_large")]
	public async Task Limits_are_enforced(string setting, string value, string csv, string code)
	{
		var result = await ImportAsync("limites.csv", Encoding.UTF8.GetBytes(csv), new Dictionary<string, string?> { [setting] = value });

		Assert.Equal(code, result.Error?.Code);
	}

	[Theory]
	[InlineData("informe.pdf", "a,b\n1,2\n", "import.unsupported_format")]
	[InlineData("numeros.csv", "1,2\n3,4\n", "import.no_header")]
	[InlineData("roto.xlsx", "this is not a zip file", "import.unreadable")]
	public async Task Files_that_cannot_be_imported_say_why(string fileName, string content, string code)
	{
		var result = await ImportAsync(fileName, Encoding.UTF8.GetBytes(content));

		Assert.Equal(code, result.Error?.Code);
	}

	[Fact]
	public async Task The_demo_samples_are_seeded_once()
	{
		await using var services = postgres.Services(new Dictionary<string, string?> { ["Demo:SamplesDirectory"] = PostgresFixture.SamplesDirectory });

		var first = await DemoDatasets.SeedAsync(services, Directory.GetCurrentDirectory());
		var second = await DemoDatasets.SeedAsync(services, Directory.GetCurrentDirectory());

		Assert.True(first <= 2, $"{first}");
		Assert.Equal(0, second);
		Assert.True(await postgres.ScalarAsync<long>("SELECT count(*) FROM datasets WHERE source_file = 'ventas-demo.xlsx'") >= 1);
	}

	private Task<Result<ImportResult>> ImportFileAsync(string sample) =>
		ImportAsync(sample, File.ReadAllBytes(Path.Combine(PostgresFixture.SamplesDirectory, sample)));

	private async Task<Result<ImportResult>> ImportAsync(string fileName, byte[] content, IReadOnlyDictionary<string, string?>? settings = null)
	{
		await using var services = postgres.Services(settings);
		await using var scope = services.CreateAsyncScope();
		using var stream = new MemoryStream(content);
		return await scope.ServiceProvider.GetRequiredService<ImportDataset>().ExecuteAsync(new ImportRequest(fileName, content.Length, stream), CancellationToken.None);
	}
}
