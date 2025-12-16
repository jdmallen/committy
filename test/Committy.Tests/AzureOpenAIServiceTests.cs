using NSubstitute;
using System.Net;
using System.Text;

namespace Committy.Tests;

public class AzureOpenAIServiceTests
{
	private const string TestPatch = "test patch";
	private const string TestApiKey = "test-api-key";
	private const string TestEndpoint = "https://test.openai.azure.com";
	private const string TestDeployment = "gpt-4";

	[Fact]
	public void AzureOpenAIService_Constructor_SetsHttpClient()
	{
		// Arrange & Act
		var httpClient = new HttpClient();
		var service = new AzureOpenAIService(httpClient);

		// Assert
		Assert.NotNull(service);
		httpClient.Dispose();
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_WithMockHttpClient_CanTestLogic()
	{
		// Arrange
		var testHandler = new TestHttpMessageHandler("""
		{
			"choices": [
				{
					"message": {
						"content": "feat: test functionality\nfix: test bug\ndocs: update readme"
					}
				}
			]
		}
		""");

		using var testHttpClient = new HttpClient(testHandler);
		var testService = new AzureOpenAIService(testHttpClient);

		// Act
		List<string> result = await testService.GenerateCommitMessageSuggestionsAsync(
			TestPatch, 
			TestApiKey, 
			TestEndpoint, 
			TestDeployment);

		// Assert
		Assert.Equal(5, result.Count);
		Assert.Equal("feat: test functionality", result[0]);
		Assert.Equal("fix: test bug", result[1]);
		Assert.Equal("docs: update readme", result[2]);
		Assert.StartsWith("feat: implement changes", result[3]); // Fallback suggestions
		Assert.StartsWith("feat: implement changes", result[4]);
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_ErrorResponse_ThrowsHttpRequestException()
	{
		// Arrange
		var testHandler = new TestHttpMessageHandler("Unauthorized", HttpStatusCode.Unauthorized);
		using var testHttpClient = new HttpClient(testHandler);
		var testService = new AzureOpenAIService(testHttpClient);

		// Act & Assert
		var exception = await Assert.ThrowsAsync<HttpRequestException>(
			() => testService.GenerateCommitMessageSuggestionsAsync(
				TestPatch,
				"invalid-key",
				TestEndpoint,
				TestDeployment));

		Assert.Contains("Azure OpenAI API request failed: Unauthorized", exception.Message);
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_EmptyResponse_ReturnsFallbackSuggestions()
	{
		// Arrange
		var testHandler = new TestHttpMessageHandler("""
		{
			"choices": [
				{
					"message": {
						"content": ""
					}
				}
			]
		}
		""");

		using var testHttpClient = new HttpClient(testHandler);
		var testService = new AzureOpenAIService(testHttpClient);

		// Act
		List<string> result = await testService.GenerateCommitMessageSuggestionsAsync(
			TestPatch,
			TestApiKey,
			TestEndpoint,
			TestDeployment);

		// Assert
		Assert.Equal(5, result.Count);
		Assert.All(result, suggestion => Assert.StartsWith("feat: implement changes", suggestion));
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_InsufficientSuggestions_FillsWithDefaults()
	{
		// Arrange
		var testHandler = new TestHttpMessageHandler("""
		{
			"choices": [
				{
					"message": {
						"content": "feat: add feature\nfix: resolve issue"
					}
				}
			]
		}
		""");

		using var testHttpClient = new HttpClient(testHandler);
		var testService = new AzureOpenAIService(testHttpClient);

		// Act
		List<string> result = await testService.GenerateCommitMessageSuggestionsAsync(
			TestPatch,
			TestApiKey,
			TestEndpoint,
			TestDeployment);

		// Assert
		Assert.Equal(5, result.Count);
		Assert.Equal("feat: add feature", result[0]);
		Assert.Equal("fix: resolve issue", result[1]);
		Assert.Equal("feat: implement changes (3)", result[2]);
		Assert.Equal("feat: implement changes (4)", result[3]);
		Assert.Equal("feat: implement changes (5)", result[4]);
	}
}

// Test helper class for HTTP testing - same as before but included for completeness
public class TestHttpMessageHandler(
	string responseContent,
	HttpStatusCode statusCode = HttpStatusCode.OK
)
	: HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var response = new HttpResponseMessage(statusCode)
		{
			Content = new StringContent(responseContent, Encoding.UTF8, "application/json"),
		};

		return Task.FromResult(response);
	}
}