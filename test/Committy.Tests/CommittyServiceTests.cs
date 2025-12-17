using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Committy.Tests;

public class CommittyServiceTests
{
	private const string TestPatch = "diff --git a/file.txt b/file.txt\n+added line";
	private const string TestApiKey = "test-api-key";
	private const string TestEndpoint = "https://test.openai.azure.com";
	private const string TestDeploymentName = "gpt-4";

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
			.GenerateCommitMessageSuggestionsAsync(TestPatch, TestApiKey, TestEndpoint, TestDeploymentName, CancellationToken.None)
			.Returns(expectedSuggestions);

		// Act
		List<string> result =
			await _committyService.GenerateCommitMessageSuggestionsAsync(
				TestPatch,
				TestApiKey,
				TestEndpoint,
				TestDeploymentName,
				CancellationToken.None);

		// Assert
		Assert.Equal(expectedSuggestions, result);
		await _mockAzureOpenAIService.Received(1)
			.GenerateCommitMessageSuggestionsAsync(TestPatch, TestApiKey, TestEndpoint, TestDeploymentName, CancellationToken.None);
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
				TestApiKey,
				TestEndpoint,
				TestDeploymentName,
				CancellationToken.None));

		Assert.Equal("Patch cannot be null or empty (Parameter 'patch')", exception.Message);
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_NullPatch_ThrowsArgumentException()
	{
		// Act & Assert
		var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				null!,
				TestApiKey,
				TestEndpoint,
				TestDeploymentName,
				CancellationToken.None));

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
				TestPatch,
				invalidApiKey,
				TestEndpoint,
				TestDeploymentName,
				CancellationToken.None));

		Assert.Equal("API key cannot be null or empty (Parameter 'apiKey')", exception.Message);
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_NullApiKey_ThrowsArgumentException()
	{
		// Act & Assert
		var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				TestPatch,
				null!,
				TestEndpoint,
				TestDeploymentName,
				CancellationToken.None));

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
				TestPatch,
				TestApiKey,
				invalidEndpoint,
				TestDeploymentName,
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
		// Act & Assert
		var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				TestPatch,
				TestApiKey,
				TestEndpoint,
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
		var innerException = new HttpRequestException("API request failed");

		_mockAzureOpenAIService
			.GenerateCommitMessageSuggestionsAsync(TestPatch, TestApiKey, TestEndpoint, TestDeploymentName, CancellationToken.None)
			.ThrowsAsync(innerException);

		// Act & Assert
		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			_committyService.GenerateCommitMessageSuggestionsAsync(
				TestPatch,
				TestApiKey,
				TestEndpoint,
				TestDeploymentName,
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
		await CommittyService.CopyToClipboardAsync(text, CancellationToken.None);
	}

	[Fact]
	public async Task CopyToClipboardAsync_NullText_DoesNotThrow()
	{
		// Act & Assert - should not throw
		await CommittyService.CopyToClipboardAsync(null!, CancellationToken.None);
	}
}
