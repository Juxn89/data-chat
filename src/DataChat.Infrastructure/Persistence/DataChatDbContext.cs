namespace DataChat.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

/// <summary>
/// The app's own tables: what each dataset is and what its columns are called. The datasets' rows live in tables
/// of their own, created at import time, which EF Core never maps.
/// </summary>
/// <param name="options">The options.</param>
internal sealed class DataChatDbContext(DbContextOptions<DataChatDbContext> options) : DbContext(options)
{
	public DbSet<DatasetRecord> Datasets => Set<DatasetRecord>();

	public DbSet<SemanticLayerRecord> SemanticLayers => Set<SemanticLayerRecord>();

	public DbSet<QuestionEntity> Questions => Set<QuestionEntity>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<DatasetRecord>(dataset =>
		{
			dataset.ToTable("datasets");
			dataset.HasKey(static d => d.Id);
			dataset.Property(static d => d.Name).IsRequired();
			dataset.Property(static d => d.SourceFile).IsRequired();
			dataset.Property(static d => d.TableName).IsRequired();
			dataset.HasIndex(static d => d.TableName).IsUnique();
			dataset.HasIndex(static d => d.ImportedAt);
			dataset.OwnsMany(static d => d.Columns, column =>
			{
				column.ToTable("dataset_columns");
				column.WithOwner().HasForeignKey("DatasetId");
				column.HasKey("DatasetId", nameof(DatasetColumnRecord.Ordinal));
				column.Property(static c => c.PhysicalName).IsRequired();
				column.Property(static c => c.Name).IsRequired();
				column.Property(static c => c.Type).HasConversion<string>().IsRequired();
				column.Property(static c => c.Samples).HasColumnType("text[]").HasDefaultValueSql("'{}'");
			});
		});

		modelBuilder.Entity<SemanticLayerRecord>(layer =>
		{
			layer.ToTable("semantic_layers");
			layer.HasKey(static l => l.DatasetId);
			layer.Property(static l => l.Document).HasColumnType("jsonb").IsRequired();
			layer.HasOne<DatasetRecord>().WithOne().HasForeignKey<SemanticLayerRecord>(static l => l.DatasetId).OnDelete(DeleteBehavior.Cascade);
		});

		modelBuilder.Entity<QuestionEntity>(question =>
		{
			question.ToTable("questions");
			question.HasKey(static q => q.Id);
			question.Property(static q => q.SemanticQuery).HasColumnType("jsonb");
			question.Property(static q => q.Outcome).HasConversion<string>();
			question.Property(static q => q.CostUsd).HasPrecision(12, 6);
			question.HasIndex(static q => q.CreatedAt);
			question.HasIndex(static q => q.DatasetId);
			question.HasIndex(static q => new { q.ConversationId, q.CreatedAt });
			question.HasOne<DatasetRecord>().WithMany().HasForeignKey(static q => q.DatasetId).OnDelete(DeleteBehavior.Cascade);
		});
	}
}

/// <summary>A row of <c>datasets</c>.</summary>
internal sealed class DatasetRecord
{
	public Guid Id { get; set; }

	public required string Name { get; set; }

	public required string SourceFile { get; set; }

	public required string TableName { get; set; }

	public int RowCount { get; set; }

	public Instant ImportedAt { get; set; }

	public List<DatasetColumnRecord> Columns { get; set; } = [];
}

/// <summary>A row of <c>dataset_columns</c>.</summary>
internal sealed class DatasetColumnRecord
{
	public int Ordinal { get; set; }

	public required string PhysicalName { get; set; }

	public required string Name { get; set; }

	public Domain.Datasets.ColumnType Type { get; set; }

	public int DistinctCount { get; set; }

	public List<string> Samples { get; set; } = [];
}

internal static class PersistenceSetup
{
	public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
		options
			.UseNpgsql(connectionString, static npgsql => npgsql.UseNodaTime())
			.UseSnakeCaseNamingConvention();
}

/// <summary>Applies the migrations; run at startup in Development and by the tests.</summary>
public static class DatabaseMigrator
{
	/// <summary>Migrates the database to the latest schema.</summary>
	/// <param name="services">The app's services.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>A task.</returns>
	public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
	{
		await using var scope = services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<DataChatDbContext>();
		await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
	}
}

/// <summary>For <c>dotnet ef</c>: a context without the app's configuration.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DataChatDbContext>
{
	private const string PlaceholderConnectionString = "Host=localhost;Database=data_chat_design";

	public DataChatDbContext CreateDbContext(string[] args)
	{
		var options = new DbContextOptionsBuilder<DataChatDbContext>();
		PersistenceSetup.Configure(options, PlaceholderConnectionString);
		return new DataChatDbContext(options.Options);
	}
}
