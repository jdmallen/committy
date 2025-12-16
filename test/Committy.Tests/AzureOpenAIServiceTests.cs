using NSubstitute;
using System.Net;
using System.Text;
using Xunit;

namespace Committy.Tests;

public class AzureOpenAIServiceTests
{
	private readonly HttpMessageHandler _mockHandler;
	private readonly HttpClient _httpClient;
	private readonly AzureOpenAIService _azureOpenAIService;

	public AzureOpenAIServiceTests()
	{
		_mockHandler = Substitute.For<HttpMessageHandler>();
		_httpClient = new HttpClient(_mockHandler);
		_azureOpenAIService = new AzureOpenAIService(_httpClient);
	}

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
		// This demonstrates how we test the parsing logic with Azure OpenAI response format
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

		var testHttpClient = new HttpClient(testHandler);
		var testService = new AzureOpenAIService(testHttpClient);

		// Act
		var result = await testService.GenerateCommitMessageSuggestionsAsync(
			"test patch", 
			"test-api-key", 
			"https://test.openai.azure.com", 
			"gpt-4");

		// Assert
		Assert.Equal(5, result.Count);
		Assert.Equal("feat: test functionality", result[0]);
		Assert.Equal("fix: test bug", result[1]);
		Assert.Equal("docs: update readme", result[2]);
		Assert.StartsWith("feat: implement changes", result[3]); // Fallback suggestions
		Assert.StartsWith("feat: implement changes", result[4]);

		testHttpClient.Dispose();
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_ErrorResponse_ThrowsHttpRequestException()
	{
		var testHandler = new TestHttpMessageHandler("Unauthorized", HttpStatusCode.Unauthorized);
		var testHttpClient = new HttpClient(testHandler);
		var testService = new AzureOpenAIService(testHttpClient);

		// Act & Assert
		var exception = await Assert.ThrowsAsync<HttpRequestException>(
			() => testService.GenerateCommitMessageSuggestionsAsync(
				"test patch",
				"invalid-key",
				"https://test.openai.azure.com",
				"gpt-4"));

		Assert.Contains("Azure OpenAI API request failed: Unauthorized", exception.Message);

		testHttpClient.Dispose();
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_EmptyResponse_ReturnsFallbackSuggestions()
	{
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

		var testHttpClient = new HttpClient(testHandler);
		var testService = new AzureOpenAIService(testHttpClient);

		// Act
		var result = await testService.GenerateCommitMessageSuggestionsAsync(
			"test patch",
			"test-api-key",
			"https://test.openai.azure.com",
			"gpt-4");

		// Assert
		Assert.Equal(5, result.Count);
		Assert.All(result, suggestion => Assert.StartsWith("feat: implement changes", suggestion));

		testHttpClient.Dispose();
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_InsufficientSuggestions_FillsWithDefaults()
	{
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

		var testHttpClient = new HttpClient(testHandler);
		var testService = new AzureOpenAIService(testHttpClient);

		// Act
		var result = await testService.GenerateCommitMessageSuggestionsAsync(
			"test patch",
			"test-api-key",
			"https://test.openai.azure.com",
			"gpt-4");

		// Assert
		Assert.Equal(5, result.Count);
		Assert.Equal("feat: add feature", result[0]);
		Assert.Equal("fix: resolve issue", result[1]);
		Assert.Equal("feat: implement changes (3)", result[2]);
		Assert.Equal("feat: implement changes (4)", result[3]);
		Assert.Equal("feat: implement changes (5)", result[4]);

		testHttpClient.Dispose();
	}
}

// Test helper class for HTTP testing - same as before but included for completeness
public class TestHttpMessageHandler : HttpMessageHandler
{
	private readonly string _responseContent;
	private readonly HttpStatusCode _statusCode;

	public TestHttpMessageHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
	{
		_responseContent = responseContent;
		_statusCode = statusCode;
	}

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var response = new HttpResponseMessage(_statusCode)
		{
			Content = new StringContent(_responseContent, Encoding.UTF8, "application/json")
		};

		return Task.FromResult(response);
	}
}