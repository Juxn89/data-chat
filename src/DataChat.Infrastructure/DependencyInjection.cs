namespace DataChat.Infrastructure;

using DataChat.Application.Features.Datasets;
using DataChat.Application.Features.Questions;
using DataChat.Application.Features.Semantics;
using DataChat.Infrastructure.Ai;
using DataChat.Infrastructure.Files;
using DataChat.Infrastructure.Persistence;
using DataChat.Infrastructure.Querying;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Npgsql;

/// <summary>Registers the adapters and the use cases. The API calls it once; the integration tests against their own Postgres.</summary>
public static class DependencyInjection
{
	/// <summary>The connection string's name, the same as the database's name in the AppHost.</summary>
	public const string DatabaseConnectionName = "datachatdb";

	/// <summary>Whether questions are answered from a script instead of the model (end-to-end tests only).</summary>
	/// <param name="configuration">The configuration.</param>
	/// <returns><see langword="true"/> when a script is configured.</returns>
	public static bool UsesScriptedTranslator(IConfiguration configuration) =>
		!string.IsNullOrEmpty(configuration?[$"{TranslatorScriptOptions.SectionName}:{nameof(TranslatorScriptOptions.File)}"]);

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
		AddOptions<QueryOptions>(services, configuration, QueryOptions.SectionName);

		// Questions read datasets through plain Npgsql: their tables are only known at run time.
		services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
		services.AddScoped<IQueryRunner, QueryRunner>();
		services.AddScoped<RunQuery>();

		AddOptions<AskOptions>(services, configuration, AskOptions.SectionName);
		AddOptions<AnthropicOptions>(services, configuration, AnthropicOptions.SectionName);
		AddOptions<TranslatorScriptOptions>(services, configuration, TranslatorScriptOptions.SectionName);
		if (UsesScriptedTranslator(configuration))
		{
			services.AddSingleton<IQuestionTranslator, ScriptedQuestionTranslator>();
		}
		else
		{
			services.AddSingleton<IQuestionTranslator, ClaudeQuestionTranslator>();
		}

		services.AddScoped<IQuestionLog, QuestionLog>();
		services.AddScoped<AskQuestion>();

		services.AddSingleton<ISpreadsheetReader, SpreadsheetReader>();
		services.AddScoped<IDatasetStore, DatasetStore>();
		services.AddScoped<ImportDataset>();
		services.AddScoped<DatasetQueries>();
		services.AddScoped<ISemanticLayerStore, SemanticLayerStore>();
		services.AddScoped<SemanticLayers>();
		return services;
	}

	private static void AddOptions<T>(IServiceCollection services, IConfiguration configuration, string section)
		where T : class =>
		services.AddOptions<T>().Bind(configuration.GetSection(section)).ValidateDataAnnotations().ValidateOnStart();
}
