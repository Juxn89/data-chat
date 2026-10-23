namespace DataChat.SampleData.Tests;

/// <summary>Files the tests read from the repo: the committed samples and the evals' questions.</summary>
internal static class RepoPaths
{
	/// <summary>The repo's root: the first folder above the test's binaries that holds the solution.</summary>
	public static string Root { get; } = FindRoot();

	private static string FindRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DataChat.slnx")))
		{
			directory = directory.Parent;
		}

		return directory?.FullName ?? throw new InvalidOperationException("DataChat.slnx not found above the test's folder.");
	}
}
