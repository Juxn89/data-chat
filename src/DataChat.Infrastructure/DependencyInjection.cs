namespace DataChat.Infrastructure;

using DataChat.Application.Features.Datasets;
using DataChat.Infrastructure.Files;
using DataChat.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

/// <summary>Registers the adapters and the use cases. The API calls it once; the integration tests against their own Postgres.</summary>
public static class DependencyInjection
{
	/// <summary>The connection string's name, the same as the database's name in the AppHost.</summary>
	public const string DatabaseConnectionName = "datachatdb";

	/// <summary>Adds everything the API needs.</summary>
	/// <param name="services">The services.</param>
	/// <param name="configuration">The configuration.</param>
	/// <returns>The services.</returns>
	/// <exception cref="InvalidOperationException">The connection string is missing.</exception>
	public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		var connectionString = configuration.GetConnectionString(DatabaseConnectionName)
			?? throw new InvalidOperationException(
				$"Missing connection string '{DatabaseConnectionName}'. Run through the AppHost, or set ConnectionStrings__{DatabaseConnectionName}.");

		services.AddSingleton<IClock>(SystemClock.Instance);
		services.AddDbContext<DataChatDbContext>(options => PersistenceSetup.Configure(options, connectionString));

		AddOptions<ImportOptions>(services, configuration, ImportOptions.SectionName);
		AddOptions<DemoOptions>(services, configuration, DemoOptions.SectionName);

		services.AddSingleton<ISpreadsheetReader, SpreadsheetReader>();
		services.AddScoped<IDatasetStore, DatasetStore>();
		services.AddScoped<ImportDataset>();
		services.AddScoped<DatasetQueries>();
		return services;
	}

	private static void AddOptions<T>(IServiceCollection services, IConfiguration configuration, string section)
		where T : class =>
		services.AddOptions<T>().Bind(configuration.GetSection(section)).ValidateDataAnnotations().ValidateOnStart();
}
