namespace DataChat.Application.Features.Datasets;

using DataChat.Application.Common;
using DataChat.Domain.Datasets;

/// <summary>Reads datasets.</summary>
/// <param name="store">The store.</param>
public sealed class DatasetQueries(IDatasetStore store)
{
	/// <summary>The dataset that does not exist.</summary>
	public static readonly Error NotFound = Error.NotFound("dataset.not_found", "There is no dataset with that id.");

	/// <summary>All datasets, newest first.</summary>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The datasets.</returns>
	public Task<IReadOnlyList<Dataset>> ListAsync(CancellationToken cancellationToken) => store.ListAsync(cancellationToken);

	/// <summary>One dataset.</summary>
	/// <param name="id">Its id.</param>
	/// <param name="cancellationToken">Cancellation.</param>
	/// <returns>The dataset, or <see cref="NotFound"/>.</returns>
	public async Task<Result<Dataset>> GetAsync(Guid id, CancellationToken cancellationToken) =>
		await store.FindAsync(id, cancellationToken).ConfigureAwait(false) is { } dataset ? dataset : NotFound;
}
