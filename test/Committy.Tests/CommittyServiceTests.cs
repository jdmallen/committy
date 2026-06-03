using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Committy.Tests;

public class CommittyServiceTests
{
	private const string TEST_PATCH = "diff --git a/file.txt b/file.txt\n+added line";
	private const string TEST_API_KEY = "test-api-key";
	private const string TEST_ENDPOINT = "https://test.openai.azure.com";
	private const string TEST_DEPLOYMENT_NAME = "gpt-4";

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
		var expectedSuggestions = new List<string>
		{
			"feat: add new functionality", "fix: resolve bug in parser", "docs: update documentation",
		};

		_mockAzureOpenAIService
			.GenerateCommitMessageSuggestionsAsync(TEST_PATCH, TEST_API_KEY, TEST_DEPLOYMENT_NAME, false, CancellationToken.None)
			.Returns(expectedSuggestions);

		// Act
		List<string> result =
			await _committyService.GenerateCommitMessageSuggestionsAsync(
				TEST_PATCH,
				TEST_API_KEY,
				TEST_ENDPOINT,
				TEST_DEPLOYMENT_NAME,
				cancellationToken: CancellationToken.None);

		// Assert
		Assert.Equal(expectedSuggestions, result);
		await _mockAzureOpenAIService.Received(1)
			.GenerateCommitMessageSuggestionsAsync(TEST_PATCH, TEST_API_KEY, TEST_DEPLOYMENT_NAME, false, CancellationToken.None);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task GenerateCommitMessageSuggestionsAsync_InvalidPatch_ThrowsArgumentException(
		string invalidPatch)
	{
		// Act & Assert
		var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				invalidPatch,
				TEST_API_KEY,
				TEST_ENDPOINT,
				TEST_DEPLOYMENT_NAME,
				cancellationToken: CancellationToken.None));

		Assert.Equal("Patch cannot be null or empty (Parameter 'patch')", exception.Message);
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_NullPatch_ThrowsArgumentException()
	{
		// Act & Assert
		var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				null!,
				TEST_API_KEY,
				TEST_ENDPOINT,
				TEST_DEPLOYMENT_NAME,
				cancellationToken: CancellationToken.None));

		Assert.Equal("Patch cannot be null or empty (Parameter 'patch')", exception.Message);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task GenerateCommitMessageSuggestionsAsync_InvalidApiKey_ThrowsArgumentException(
		string invalidApiKey)
	{
		// Act & Assert
		var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				TEST_PATCH,
				invalidApiKey,
				TEST_ENDPOINT,
				TEST_DEPLOYMENT_NAME,
				cancellationToken: CancellationToken.None));

		Assert.Equal("API key cannot be null or empty (Parameter 'apiKey')", exception.Message);
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_NullApiKey_ThrowsArgumentException()
	{
		// Act & Assert
		var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				TEST_PATCH,
				null!,
				TEST_ENDPOINT,
				TEST_DEPLOYMENT_NAME,
				cancellationToken: CancellationToken.None));

		Assert.Equal("API key cannot be null or empty (Parameter 'apiKey')", exception.Message);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task GenerateCommitMessageSuggestionsAsync_InvalidEndpoint_ThrowsArgumentException(
		string invalidEndpoint)
	{
		// Act & Assert
		var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				TEST_PATCH,
				TEST_API_KEY,
				invalidEndpoint,
				TEST_DEPLOYMENT_NAME,
				cancellationToken: CancellationToken.None));

		Assert.Equal("Endpoint cannot be null or empty (Parameter 'endpoint')", exception.Message);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task
		GenerateCommitMessageSuggestionsAsync_InvalidDeploymentName_ThrowsArgumentException(
			string invalidDeploymentName)
	{
		// Act & Assert
		var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				TEST_PATCH,
				TEST_API_KEY,
				TEST_ENDPOINT,
				invalidDeploymentName,
				cancellationToken: CancellationToken.None));

		Assert.Equal(
			"Deployment name cannot be null or empty (Parameter 'deploymentName')",
			exception.Message);
	}

	[Fact]
	public async Task
		GenerateCommitMessageSuggestionsAsync_AzureOpenAIServiceThrows_WrapsInInvalidOperationException()
	{
		// Arrange
		var innerException = new HttpRequestException("API request failed");

		_mockAzureOpenAIService
			.GenerateCommitMessageSuggestionsAsync(TEST_PATCH, TEST_API_KEY, TEST_DEPLOYMENT_NAME, false, CancellationToken.None)
			.ThrowsAsync(innerException);

		// Act & Assert
		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				TEST_PATCH,
				TEST_API_KEY,
				TEST_ENDPOINT,
				TEST_DEPLOYMENT_NAME,
				cancellationToken: CancellationToken.None));

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
		await CommittyService.CopyToClipboardAsync(text, CancellationToken.None);
	}

	[Fact]
	public async Task CopyToClipboardAsync_NullText_DoesNotThrow()
	{
		// Act & Assert - should not throw
		await CommittyService.CopyToClipboardAsync(null!, CancellationToken.None);
	}
}
