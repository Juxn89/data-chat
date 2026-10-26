using System.Text.Json.Serialization;
using DataChat.Api.Endpoints;
using DataChat.Application.Features.Datasets;
using DataChat.Infrastructure;
using DataChat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Features;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

// Instants and dates as ISO 8601; enums by name.
builder.Services.ConfigureHttpJsonOptions(options =>
{
	options.SerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
	options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddInfrastructure(builder.Configuration);

var maxFileBytes = builder.Configuration.GetSection(ImportOptions.SectionName).Get<ImportOptions>()?.MaxFileBytes ?? new ImportOptions().MaxFileBytes;
builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = maxFileBytes);

var app = builder.Build();

// A malformed request (bad JSON, wrong content type) is the client's error, not a 500.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
	StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
		? badRequest.StatusCode
		: StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
	await DatabaseMigrator.MigrateAsync(app.Services);
	await DemoDatasets.SeedAsync(app.Services, app.Environment.ContentRootPath);
}

app.MapDefaultEndpoints();
app.MapDatasetEndpoints(maxFileBytes);
app.MapSemanticLayerEndpoints();
app.MapQueryEndpoints();

await app.RunAsync();

/// <summary>The entry point, visible to the integration tests' <c>WebApplicationFactory</c>.</summary>
public partial class Program;
