namespace DataChat.Application.Features.Questions;

using DataChat.Application.Common;
using DataChat.Application.Features.Datasets;
using DataChat.Application.Features.Semantics;
using DataChat.Domain.Datasets;
using DataChat.Domain.Semantics;
using NodaTime;

/// <summary>What a result column holds.</summary>
public enum ResultColumnKind
{
	/// <summary>A time label: <c>2026-09</c>, <c>2026-Q3</c>.</summary>
	Time,

	/// <summary>A dimension's value.</summary>
	Dimension,

	/// <summary>A metric for the period asked about.</summary>
	Metric,

	/// <summary>The same metric for the period it is compared with.</summary>
	PreviousMetric,
}

/// <summary>A column of an answer.</summary>
/// <param name="Key">The metric's or dimension's key, or <see cref="SemanticQuery.TimeKey"/>.</param>
/// <param name="Name">Its name in the layer.</param>
/// <param name="Kind">What it holds.</param>
public sealed record ResultColumn(string Key, string Name, ResultColumnKind Kind);

/// <summary>A value passed to the SQL as a parameter, shown next to the SQL so a person can read the whole query.</summary>
/// <param name="Name">The parameter, e.g. <c>@p1</c>.</param>
/// <param name="Value">Its value as text.</param>
public sealed record ShownParameter(string Name, string Value);

/// <summary>The rows a semantic query returned, and how they were computed.</summary>
/// <param name="Columns">The columns.</param>
/// <param name="Rows">The rows: strings, longs, decimals or nulls.</param>
/// <param name="Sql">The SQL that ran.</param>
/// <param name="Parameters">Its parameters.</param>
/// <param name="Period">The dates asked about, when the query had a period.</param>
/// <param name="ComparedWith">The dates compared with, when it had a comparison.</param>
/// <param name="Truncated">The rows stopped at the limit; there may be more.</param>
public sealed record QueryAnswer(
	IReadOnlyList<ResultColumn> Columns,
	IReadOnlyList<IReadOnlyList<object?>> Rows,
	string Sql,
	IReadOnlyList<ShownParameter> Parameters,
	DateInterval? Period,
	DateInterval? ComparedWith,
	bool Truncated);

/// <summary>Compiles a semantic query to SQL and runs it with read-only rights.</summary>
public interface IQueryRunner
{
	/// <summary>Runs a valid query.</summary>
	/// <param name="dataset">The dataset.</param>
	/// <param name="layer">Its confirmed layer.</param>
	/// <param name="query">The query, already validated against the layer.</param>
	/// <param name="today">The "today" named periods resolve against.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The answer, or <see cref="RunQuery.TimedOut"/>.</returns>
	Task<Result<QueryAnswer>> RunAsync(Dataset dataset, SemanticLayer layer, SemanticQuery query, LocalDate today, CancellationToken cancellationToken);
}

/// <summary>
/// Answers a semantic query on a dataset (ADR 0001): validated against the confirmed layer, compiled by the server,
/// run by a role that can only read the dataset tables.
/// </summary>
/// <param name="datasets">The datasets.</param>
/// <param name="layers">The confirmed layers.</param>
/// <param name="runner">The runner.</param>
/// <param name="clock">The clock, for datasets without an "as of" date.</param>
public sealed class RunQuery(IDatasetStore datasets, ISemanticLayerStore layers, IQueryRunner runner, IClock clock)
{
	/// <summary>The layer is still a proposal.</summary>
	public static readonly Error NotConfirmed = Error.Conflict(
		"semantic.not_confirmed",
		"Confirm the dataset's metrics and dimensions before asking about it.");

	/// <summary>The query ran past the statement timeout.</summary>
	public static readonly Error TimedOut = Error.Unavailable(
		"query.timeout",
		"The question took too long to answer; narrow the period or the groups.");

	/// <summary>Runs a query.</summary>
	/// <param name="datasetId">The dataset.</param>
	/// <param name="query">The query.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The answer, or why there is none.</returns>
	public async Task<Result<QueryAnswer>> ExecuteAsync(Guid datasetId, SemanticQuery query, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(query);
		if (await datasets.FindAsync(datasetId, cancellationToken).ConfigureAwait(false) is not { } dataset)
		{
			return DatasetQueries.NotFound;
		}

		if (await layers.FindAsync(datasetId, cancellationToken).ConfigureAwait(false) is not { } layer)
		{
			return NotConfirmed;
		}

		var issues = query.Validate(layer);
		if (issues.Count > 0)
		{
			var details = issues.Select(static (issue, i) => (Key: $"issue{i + 1}", Value: $"{issue.Code}: {issue.Message}"))
				.ToDictionary(static pair => pair.Key, static pair => pair.Value);
			return new Error("query.invalid", issues[0].Message, ErrorKind.Validation, details);
		}

		var today = layer.AsOf ?? clock.GetCurrentInstant().InUtc().Date;
		return await runner.RunAsync(dataset, layer, query, today, cancellationToken).ConfigureAwait(false);
	}
}
