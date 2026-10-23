namespace DataChat.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

/// <summary>Registers the adapters. The API calls it once; the integration tests call it against their own Postgres.</summary>
public static class DependencyInjection
{
	/// <summary>The connection string's name, the same as the database's name in the AppHost.</summary>
	public const string DatabaseConnectionName = "datachatdb";

	/// <summary>Adds the adapters.</summary>
	/// <param name="services">The services.</param>
	/// <param name="configuration">The configuration.</param>
	/// <returns>The services.</returns>
	public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
	{
		var connectionString = configuration.GetConnectionString(DatabaseConnectionName)
			?? throw new InvalidOperationException($"ConnectionStrings:{DatabaseConnectionName} is not configured.");

		// One pooled data source for the app. Imported datasets are tables whose columns are only known at run time,
		// so they are read with Npgsql directly; the role that runs the questions' SQL gets its own source (day 4).
		services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
		return services;
	}
}
