using DataChat.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// A malformed request (bad JSON, wrong content type) is the client's error, not a 500.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
	StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
		? badRequest.StatusCode
		: StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

app.MapDefaultEndpoints();

await app.RunAsync();

/// <summary>The entry point, visible to the integration tests' <c>WebApplicationFactory</c>.</summary>
public partial class Program;
