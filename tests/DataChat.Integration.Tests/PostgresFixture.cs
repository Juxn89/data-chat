namespace DataChat.Integration.Tests;

using DataChat.Infrastructure;
using DataChat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

/// <summary>One real Postgres per test run, migrated once, plus the API on top of it. Same image as the AppHost.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
	public const string Image = "postgres:18.6-alpine";

	private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();

	private WebApplicationFactory<Program>? _api;

	public string ConnectionString => _container.GetConnectionString();

	public WebApplicationFactory<Program> Api => _api ?? throw new InvalidOperationException("Not initialised.");

	/// <summary>The repo's <c>samples</c> folder.</summary>
	public static string SamplesDirectory { get; } = Path.Combine(RepoRoot(), "samples");

	public async Task InitializeAsync()
	{
		await _container.StartAsync();
		_api = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
			.UseEnvironment("Testing")
			.UseSetting($"ConnectionStrings:{DependencyInjection.DatabaseConnectionName}", ConnectionString));
		await DatabaseMigrator.MigrateAsync(_api.Services);
	}

	public async Task DisposeAsync()
	{
		if (_api is not null)
		{
			await _api.DisposeAsync();
		}

		await _container.DisposeAsync();
	}

	/// <summary>The use cases on the real database.</summary>
	/// <param name="settings">Settings over the defaults, e.g. smaller limits.</param>
	/// <param name="replace">Replaces services after registration, e.g. the translator with a fake.</param>
	/// <returns>The services; each scope is one request's unit of work.</returns>
	public ServiceProvider Services(IReadOnlyDictionary<string, string?>? settings = null, Action<IServiceCollection>? replace = null)
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> { [$"ConnectionStrings:{DependencyInjection.DatabaseConnectionName}"] = ConnectionString })
			.AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
			.Build();
		var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration);
		replace?.Invoke(services);
		return services.BuildServiceProvider();
	}

	/// <summary>Runs a scalar query.</summary>
	/// <typeparam name="T">The result's type.</typeparam>
	/// <param name="sql">The query.</param>
	/// <returns>The value.</returns>
	public async Task<T> ScalarAsync<T>(string sql)
	{
		await using var source = NpgsqlDataSource.Create(ConnectionString);
		await using var command = source.CreateCommand(sql);
		return (T)(await command.ExecuteScalarAsync())!;
	}

	private static string RepoRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DataChat.slnx")))
		{
			directory = directory.Parent;
		}

		return directory?.FullName ?? throw new InvalidOperationException("DataChat.slnx not found above the test's folder.");
	}
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
	public const string Name = "postgres";
}
