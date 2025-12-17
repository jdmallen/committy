namespace Committy.Tests;

public class GitServiceTests
{
	[Fact]
	public async Task GetStagedDiffAsync_BehavesCorrectlyBasedOnStagedChanges()
	{
		try
		{
			// Act
			string result = await GitService.GetStagedDiffAsync(CancellationToken.None);

			// Assert - if we get here, there are staged changes
			Assert.NotNull(result);
			Assert.NotEmpty(result);
			Assert.Contains("diff --git", result);
		}
		catch (InvalidOperationException ex)
		{
			// Assert - if we get here, there are no staged changes
			Assert.Contains("No staged changes found", ex.Message);
		}
	}

	[Fact]
	public void GitService_Constructor_CreatesInstance()
	{
		// Arrange & Act
		var service = new GitService();

		// Assert
		Assert.NotNull(service);
	}

	// In a real test environment, we might want to:
	// 1. Create a temporary git repository for testing
	// 2. Mock the CliWrap calls using NSubstitute (would require interface extraction)
	// 3. Use a test framework that provides git repository fixtures, if one exists

	/// <summary>
	/// Helper class for integration tests (if we want to add them later)
	/// </summary>
// ReSharper disable once UnusedType.Global
	public class GitTestFixture : IDisposable
	{
		// ReSharper disable MemberCanBePrivate.Global
		public string TempDirectory { get; }

		public string OriginalDirectory { get; }
		// ReSharper restore MemberCanBePrivate.Global

		public GitTestFixture()
		{
			OriginalDirectory = Directory.GetCurrentDirectory();
			TempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
			Directory.CreateDirectory(TempDirectory);
			Directory.SetCurrentDirectory(TempDirectory);
		}

		// ReSharper disable once UnusedMember.Global
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
#pragma warning disable CA1822 // Mark members as static
		public async Task InitializeGitRepositoryAsync()
		{
			// Initialize git repository in temp directory
			// This would use CliWrap to run git init, etc.
			// We could mock CliWrap using NSubstitute if we extracted an interface
		}
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
#pragma warning restore CA1822 // Mark members as static

		public void Dispose()
		{
			Directory.SetCurrentDirectory(OriginalDirectory);

			if (Directory.Exists(TempDirectory))
			{
				Directory.Delete(TempDirectory, true);
			}

			GC.SuppressFinalize(this);
		}
	}
}
