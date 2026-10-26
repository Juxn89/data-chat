namespace DataChat.Application.Features.Semantics;

using DataChat.Application.Common;
using DataChat.Application.Features.Datasets;
using DataChat.Domain.Datasets;
using DataChat.Domain.Semantics;

/// <summary>Where each dataset's semantic layer is kept, once a person has confirmed it.</summary>
public interface ISemanticLayerStore
{
	/// <summary>The confirmed layer of a dataset.</summary>
	/// <param name="datasetId">The dataset.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The layer, or null while it is only a proposal.</returns>
	Task<SemanticLayer?> FindAsync(Guid datasetId, CancellationToken cancellationToken);

	/// <summary>Saves a confirmed layer, replacing the previous one.</summary>
	/// <param name="datasetId">The dataset.</param>
	/// <param name="layer">The layer.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>A task.</returns>
	Task SaveAsync(Guid datasetId, SemanticLayer layer, CancellationToken cancellationToken);
}

/// <summary>A dataset's layer and whether a person confirmed it.</summary>
/// <param name="Dataset">The dataset.</param>
/// <param name="Layer">The layer.</param>
/// <param name="Confirmed">False while it is the app's proposal.</param>
public sealed record SemanticLayerView(Dataset Dataset, SemanticLayer Layer, bool Confirmed);

/// <summary>
/// The semantic layer of a dataset (plan §2): the app proposes one after an import, and the person confirms it,
/// renames metrics and dimensions, adds ratios like the average ticket. Questions are only answered against a
/// confirmed layer.
/// </summary>
/// <param name="datasets">The datasets.</param>
/// <param name="layers">The confirmed layers.</param>
public sealed class SemanticLayers(IDatasetStore datasets, ISemanticLayerStore layers)
{
	/// <summary>A dataset's layer: the confirmed one, or the proposal.</summary>
	/// <param name="datasetId">The dataset.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The layer, or <see cref="DatasetQueries.NotFound"/>.</returns>
	public async Task<Result<SemanticLayerView>> GetAsync(Guid datasetId, CancellationToken cancellationToken)
	{
		if (await datasets.FindAsync(datasetId, cancellationToken).ConfigureAwait(false) is not { } dataset)
		{
			return DatasetQueries.NotFound;
		}

		return await layers.FindAsync(datasetId, cancellationToken).ConfigureAwait(false) is { } layer
			? new SemanticLayerView(dataset, layer, Confirmed: true)
			: new SemanticLayerView(dataset, Proposal.For(dataset), Confirmed: false);
	}

	/// <summary>Confirms a layer for a dataset.</summary>
	/// <param name="datasetId">The dataset.</param>
	/// <param name="layer">The layer, as the person edited it.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The confirmed layer, or why it is not valid.</returns>
	public async Task<Result<SemanticLayerView>> ConfirmAsync(Guid datasetId, SemanticLayer layer, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(layer);
		if (await datasets.FindAsync(datasetId, cancellationToken).ConfigureAwait(false) is not { } dataset)
		{
			return DatasetQueries.NotFound;
		}

		var issues = layer.Validate(dataset);
		if (issues.Count > 0)
		{
			return Invalid(issues);
		}

		await layers.SaveAsync(datasetId, layer, cancellationToken).ConfigureAwait(false);
		return new SemanticLayerView(dataset, layer, Confirmed: true);
	}

	/// <summary>The issues as one validation error; each issue's code and message are in the details.</summary>
	/// <param name="issues">The issues.</param>
	/// <returns>The error.</returns>
	public static Error Invalid(IReadOnlyList<SemanticIssue> issues)
	{
		ArgumentNullException.ThrowIfNull(issues);
		var details = issues
			.Select(static (issue, i) => (Key: $"issue{i + 1}", Value: $"{issue.Code}: {issue.Message}"))
			.ToDictionary(static pair => pair.Key, static pair => pair.Value);
		return new Error("semantic.invalid", issues[0].Message, ErrorKind.Validation, details);
	}
}
