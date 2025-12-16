namespace Committy.Tests;

public class GitServiceTests
{
	private readonly GitService _gitService = new();

	[Fact]
	public async Task GetStagedDiffAsync_NoGitRepo_ThrowsInvalidOperationException()
	{
		// Act & Assert
		// This test assumes we're not in a git repository or have no staged changes
		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			GitService.GetStagedDiffAsync);

		Assert.NotNull(exception.Message);
	}

	[Fact]
	public void GitService_Constructor_CreatesInstance()
	{
		// Arrange & Act
		var service = new GitService();

		// Assert
		Assert.NotNull(service);
	}

	// Note: These tests are integration tests that require git to be available
	// In a real test environment, we might want to:
	// 1. Create a temporary git repository for testing
	// 2. Mock the CliWrap calls using NSubstitute (would require interface extraction)
	// 3. Use a test framework that provides git repository fixtures

	// Example of how we might test with a real git repository:
	[Fact(Skip = "Integration test - requires actual git repository with staged changes")]
	public async Task GetStagedDiffAsync_WithStagedChanges_ReturnsDiff()
	{
		// This would require setting up a test git repository with staged changes
		// and would be more of an integration test

		// Arrange - would need to create temp git repo and stage files

		// Act
		// var result = await _gitService.GetStagedDiffAsync();

		// Assert
		// Assert.NotEmpty(result);
		// Assert.Contains("diff --git", result);
	}

	[Fact(Skip = "Integration test - requires git environment")]
	public async Task GetStagedDiffAsync_NoStagedChanges_ThrowsException()
	{
		// This would test the scenario where git diff --cached returns empty
		// but git is available and we're in a repository

		// Would require setting up a clean git repository with no staged changes
	}
}

// Helper class for integration tests (if we want to add them later)
public class GitTestFixture : IDisposable
{
	public string TempDirectory { get; }

	public string OriginalDirectory { get; }

	public GitTestFixture()
	{
		OriginalDirectory = Directory.GetCurrentDirectory();
		TempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(TempDirectory);
		Directory.SetCurrentDirectory(TempDirectory);
	}

	public async Task InitializeGitRepositoryAsync()
	{
		// Initialize git repository in temp directory
		// This would use CliWrap to run git init, etc.
		// We could mock CliWrap using NSubstitute if we extracted an interface
	}

	public void Dispose()
	{
		Directory.SetCurrentDirectory(OriginalDirectory);

		if (Directory.Exists(TempDirectory))
		{
			Directory.Delete(TempDirectory, true);
		}
	}
}
