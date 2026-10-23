const int WebPort = 3030;
const string AnthropicKey = "ANTHROPIC_API_KEY";

var builder = DistributedApplication.CreateBuilder(args);

// Pinned so local runs and the integration tests (Testcontainers) use the same Postgres. The volume goes on
// /var/lib/postgresql, not the data folder: Postgres 18 images keep PGDATA in a versioned subdirectory.
var postgres = builder.AddPostgres("postgres")
	.WithImage("postgres")
	.WithImageTag("18.6-alpine")
	.WithVolume("data-chat-pgdata", "/var/lib/postgresql")
	.WithLifetime(ContainerLifetime.Persistent);

var database = postgres.AddDatabase("datachatdb");

var api = builder.AddProject<Projects.DataChat_Api>("api")
	.WithReference(database)
	.WaitFor(database)
	.WithHttpHealthCheck("/health");

// The model is called only when the developer has a key: in the environment, or in the repo's git-ignored .env
// (see .env.template). It is passed on as a secret parameter and never printed.
if ((Environment.GetEnvironmentVariable(AnthropicKey) ?? DotEnv(Path.Combine(builder.AppHostDirectory, "..", "..", ".env"), AnthropicKey)) is { Length: > 0 } key)
{
	api.WithEnvironment("Anthropic__ApiKey", builder.AddParameter("anthropic-api-key", key, secret: true));
}

// The web app on http://localhost:3030, /api forwarded to the API wherever Aspire runs it. Aspire's proxy holds
// the port and hands Next its own in PORT, which "dev:aspire" ("next dev" without --port) honours.
builder.AddJavaScriptApp("web", "../../web", "dev:aspire")
	.WithPnpm(install: false)
	.WithHttpEndpoint(port: WebPort, env: "PORT")
	.WithEnvironment("API_URL", api.GetEndpoint("http"))
	.WithEnvironment("NEXT_TELEMETRY_DISABLED", "1")
	.WithExternalHttpEndpoints()
	.WaitFor(api);

await builder.Build().RunAsync();

// KEY=value lines; blank lines and # comments skipped. Only the one variable is read.
static string? DotEnv(string path, string name) =>
	File.Exists(path)
		? File.ReadLines(path)
			.Select(static line => line.Trim())
			.Where(line => line.StartsWith(name + "=", StringComparison.Ordinal))
			.Select(line => line[(name.Length + 1)..].Trim().Trim('"'))
			.LastOrDefault(static value => value.Length > 0)
		: null;
