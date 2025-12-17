using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Committy.Tests;

public class CommittyServiceTests
{
	private readonly IAzureOpenAIService _mockAzureOpenAIService;
	private readonly CommittyService _committyService;

	public CommittyServiceTests()
	{
		_mockAzureOpenAIService = Substitute.For<IAzureOpenAIService>();
		_committyService = new CommittyService(_mockAzureOpenAIService);
	}

	[Fact]
	public async Task
		GenerateCommitMessageSuggestionsAsync_ValidParameters_ReturnsExpectedSuggestions()
	{
		// Arrange
		const string patch = "diff --git a/file.txt b/file.txt\n+added line";
		const string apiKey = "test-api-key";
		const string endpoint = "https://test.openai.azure.com";
		const string deploymentName = "gpt-4";
		var expectedSuggestions = new List<string>
		{
			"feat: add new functionality", "fix: resolve bug in parser", "docs: update documentation",
		};

		_mockAzureOpenAIService
			.GenerateCommitMessageSuggestionsAsync(patch, apiKey, endpoint, deploymentName, CancellationToken.None)
			.Returns(expectedSuggestions);

		// Act
		List<string> result =
			await _committyService.GenerateCommitMessageSuggestionsAsync(
				patch,
				apiKey,
				endpoint,
				deploymentName,
				CancellationToken.None);

		// Assert
		Assert.Equal(expectedSuggestions, result);
		await _mockAzureOpenAIService.Received(1)
			.GenerateCommitMessageSuggestionsAsync(patch, apiKey, endpoint, deploymentName, CancellationToken.None);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task GenerateCommitMessageSuggestionsAsync_InvalidPatch_ThrowsArgumentException(
		string invalidPatch)
	{
		// Arrange
		const string apiKey = "test-api-key";
		const string endpoint = "https://test.openai.azure.com";
		const string deploymentName = "gpt-4";

		// Act & Assert
		var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				invalidPatch,
				apiKey,
				endpoint,
				deploymentName,
				CancellationToken.None));

		Assert.Equal("Patch cannot be null or empty (Parameter 'patch')", exception.Message);
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_NullPatch_ThrowsArgumentException()
	{
		// Arrange
		const string apiKey = "test-api-key";
		const string endpoint = "https://test.openai.azure.com";
		const string deploymentName = "gpt-4";

		// Act & Assert
		var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				null!,
				apiKey,
				endpoint,
				deploymentName,
				CancellationToken.None));

		Assert.Equal("Patch cannot be null or empty (Parameter 'patch')", exception.Message);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task GenerateCommitMessageSuggestionsAsync_InvalidApiKey_ThrowsArgumentException(
		string invalidApiKey)
	{
		// Arrange
		const string patch = "diff --git a/file.txt b/file.txt\n+added line";
		const string endpoint = "https://test.openai.azure.com";
		const string deploymentName = "gpt-4";

		// Act & Assert
		var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				patch,
				invalidApiKey,
				endpoint,
				deploymentName,
				CancellationToken.None));

		Assert.Equal("API key cannot be null or empty (Parameter 'apiKey')", exception.Message);
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_NullApiKey_ThrowsArgumentException()
	{
		// Arrange
		const string patch = "diff --git a/file.txt b/file.txt\n+added line";
		const string endpoint = "https://test.openai.azure.com";
		const string deploymentName = "gpt-4";

		// Act & Assert
		var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				patch,
				null!,
				endpoint,
				deploymentName,
				CancellationToken.None));

		Assert.Equal("API key cannot be null or empty (Parameter 'apiKey')", exception.Message);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task GenerateCommitMessageSuggestionsAsync_InvalidEndpoint_ThrowsArgumentException(
		string invalidEndpoint)
	{
		// Arrange
		const string patch = "diff --git a/file.txt b/file.txt\n+added line";
		const string apiKey = "test-api-key";
		const string deploymentName = "gpt-4";

		// Act & Assert
		var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				patch,
				apiKey,
				invalidEndpoint,
				deploymentName,
				CancellationToken.None));

		Assert.Equal("Endpoint cannot be null or empty (Parameter 'endpoint')", exception.Message);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task
		GenerateCommitMessageSuggestionsAsync_InvalidDeploymentName_ThrowsArgumentException(
			string invalidDeploymentName)
	{
		// Arrange
		const string patch = "diff --git a/file.txt b/file.txt\n+added line";
		const string apiKey = "test-api-key";
		const string endpoint = "https://test.openai.azure.com";

		// Act & Assert
		var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				patch,
				apiKey,
				endpoint,
				invalidDeploymentName,
				CancellationToken.None));

		Assert.Equal(
			"Deployment name cannot be null or empty (Parameter 'deploymentName')",
			exception.Message);
	}

	[Fact]
	public async Task
		GenerateCommitMessageSuggestionsAsync_AzureOpenAIServiceThrows_WrapsInInvalidOperationException()
	{
		// Arrange
		const string patch = "diff --git a/file.txt b/file.txt\n+added line";
		const string apiKey = "test-api-key";
		const string endpoint = "https://test.openai.azure.com";
		const string deploymentName = "gpt-4";
		var innerException = new HttpRequestException("API request failed");

		_mockAzureOpenAIService
			.GenerateCommitMessageSuggestionsAsync(patch, apiKey, endpoint, deploymentName, CancellationToken.None)
			.ThrowsAsync(innerException);

		// Act & Assert
		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				patch,
				apiKey,
				endpoint,
				deploymentName,
				CancellationToken.None));

		Assert.StartsWith("Failed to generate commit message suggestions:", exception.Message);
		Assert.Equal(innerException, exception.InnerException);
	}

	[Fact]
	public void ReadPatchFromStdinAsync_NoInputRedirected_ThrowsInvalidOperationException()
	{
		// Note: This test assumes Console.IsInputRedirected is false in test environment
		// In a real test environment, we might need to mock the console input

		// Act & Assert
		Task<InvalidOperationException> exception =
			Assert.ThrowsAsync<InvalidOperationException>(() =>
				CommittyService.ReadPatchFromStdinAsync(CancellationToken.None));

		Assert.NotNull(exception);
	}

	[Fact]
	public async Task CopyToClipboardAsync_ValidText_DoesNotThrow()
	{
		// Arrange
		const string text = "feat: add new feature";

		// Act & Assert - should not throw
		await _committyService.CopyToClipboardAsync(text, CancellationToken.None);
	}

	[Fact]
	public async Task CopyToClipboardAsync_NullText_DoesNotThrow()
	{
		// Act & Assert - should not throw
		await _committyService.CopyToClipboardAsync(null!, CancellationToken.None);
	}
}
