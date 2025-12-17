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
	public void AzureOpenAIService_Constructor_CreatesInstance()
	{
		// Arrange & Act
		var service = new AzureOpenAIService();

		// Assert
		Assert.NotNull(service);
	}

	[Fact]
	public void AzureOpenAIService_ConstructorWithHttpService_CreatesInstance()
	{
		// Arrange
		var mockHttpService = Substitute.For<IHttpService>();

		// Act
		var service = new AzureOpenAIService(mockHttpService);

		// Assert
		Assert.NotNull(service);
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_ValidResponse_ParsesCorrectly()
	{
		// Arrange
		var mockHttpService = Substitute.For<IHttpService>();
		var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("""
			{
				"choices": [
					{
						"message": {
							"content": "feat: add feature\nfix: bug\ndocs: update readme"
						}
					}
				]
			}
			""", Encoding.UTF8, "application/json"),
		};

		mockHttpService.SendAsync(
			Arg.Any<HttpRequestMessage>(),
			Arg.Any<CancellationToken>())
			.Returns(mockResponse);

		var service = new AzureOpenAIService(mockHttpService);

		// Act
		List<string> result = await service.GenerateCommitMessageSuggestionsAsync(
			TestPatch, TestApiKey, TestEndpoint, TestDeployment);

		// Assert
		Assert.Equal(5, result.Count);
		Assert.Equal("feat: add feature", result[0]);
		Assert.Equal("fix: bug", result[1]);
		Assert.Equal("docs: update readme", result[2]);
		Assert.StartsWith("feat: implement changes", result[3]);
		Assert.StartsWith("feat: implement changes", result[4]);
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_EmptyResponse_ReturnsFallbackSuggestions()
	{
		// Arrange
		var mockHttpService = Substitute.For<IHttpService>();
		var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("""
			{
				"choices": [
					{
						"message": {
							"content": ""
						}
					}
				]
			}
			""", Encoding.UTF8, "application/json"),
		};

		mockHttpService.SendAsync(
			Arg.Any<HttpRequestMessage>(),
			Arg.Any<CancellationToken>())
			.Returns(mockResponse);

		var service = new AzureOpenAIService(mockHttpService);

		// Act
		List<string> result = await service.GenerateCommitMessageSuggestionsAsync(
			TestPatch, TestApiKey, TestEndpoint, TestDeployment);

		// Assert
		Assert.Equal(5, result.Count);
		Assert.All(result, suggestion => Assert.StartsWith("feat: implement changes", suggestion));
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_InsufficientSuggestions_FillsWithDefaults()
	{
		// Arrange
		var mockHttpService = Substitute.For<IHttpService>();
		var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("""
			{
				"choices": [
					{
						"message": {
							"content": "feat: add feature\nfix: resolve issue"
						}
					}
				]
			}
			""", Encoding.UTF8, "application/json"),
		};

		mockHttpService.SendAsync(
			Arg.Any<HttpRequestMessage>(),
			Arg.Any<CancellationToken>())
			.Returns(mockResponse);

		var service = new AzureOpenAIService(mockHttpService);

		// Act
		List<string> result = await service.GenerateCommitMessageSuggestionsAsync(
			TestPatch, TestApiKey, TestEndpoint, TestDeployment);

		// Assert
		Assert.Equal(5, result.Count);
		Assert.Equal("feat: add feature", result[0]);
		Assert.Equal("fix: resolve issue", result[1]);
		Assert.Equal("feat: implement changes (3)", result[2]);
		Assert.Equal("feat: implement changes (4)", result[3]);
		Assert.Equal("feat: implement changes (5)", result[4]);
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_ErrorResponse_ThrowsHttpRequestException()
	{
		// Arrange
		var mockHttpService = Substitute.For<IHttpService>();
		var mockResponse = new HttpResponseMessage(HttpStatusCode.Unauthorized)
		{
			Content = new StringContent("Unauthorized", Encoding.UTF8, "application/json"),
		};

		mockHttpService.SendAsync(
			Arg.Any<HttpRequestMessage>(),
			Arg.Any<CancellationToken>())
			.Returns(mockResponse);

		var service = new AzureOpenAIService(mockHttpService);

		// Act & Assert
		var exception = await Assert.ThrowsAsync<HttpRequestException>(
			() => service.GenerateCommitMessageSuggestionsAsync(
				TestPatch,
				"invalid-key",
				TestEndpoint,
				TestDeployment));

		Assert.Contains("Azure OpenAI API request failed: Unauthorized", exception.Message);
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_CancellationToken_IsPropagated()
	{
		// Arrange
		var mockHttpService = Substitute.For<IHttpService>();
		var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		mockHttpService.SendAsync(
			Arg.Any<HttpRequestMessage>(),
			Arg.Any<CancellationToken>())
			.Returns(Task.FromCanceled<HttpResponseMessage>(cts.Token));

		var service = new AzureOpenAIService(mockHttpService);

		// Act & Assert - TaskCanceledException is a subclass of OperationCanceledException
		await Assert.ThrowsAsync<TaskCanceledException>(() =>
			service.GenerateCommitMessageSuggestionsAsync(
				TestPatch, TestApiKey, TestEndpoint, TestDeployment, cts.Token));
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_ValidRequest_SendsCorrectApiKey()
	{
		// Arrange
		var mockHttpService = Substitute.For<IHttpService>();
		var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("""
			{
				"choices": [
					{
						"message": {
							"content": "feat: test"
						}
					}
				]
			}
			""", Encoding.UTF8, "application/json"),
		};

		mockHttpService.SendAsync(
			Arg.Any<HttpRequestMessage>(),
			Arg.Any<CancellationToken>())
			.Returns(mockResponse);

		var service = new AzureOpenAIService(mockHttpService);

		// Act
		await service.GenerateCommitMessageSuggestionsAsync(
			TestPatch, TestApiKey, TestEndpoint, TestDeployment);

		// Assert
		await mockHttpService.Received(1).SendAsync(
			Arg.Is<HttpRequestMessage>(req => 
				req.Headers.Contains("api-key") && 
				req.Headers.GetValues("api-key").First() == TestApiKey),
			Arg.Any<CancellationToken>());
	}
}
