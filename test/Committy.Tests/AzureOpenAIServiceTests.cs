using NSubstitute;
using OpenAI.Chat;

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
	public void AzureOpenAIService_ConstructorWithClient_CreatesInstance()
	{
		// Arrange
		var mockClient = Substitute.For<IAzureOpenAIClient>();

		// Act
		var service = new AzureOpenAIService(mockClient);

		// Assert
		Assert.NotNull(service);
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_WithMockClient_CallsClientMethod()
	{
		// Arrange
		var mockClient = Substitute.For<IAzureOpenAIClient>();
		mockClient.CompleteChatAsync(
			Arg.Any<IEnumerable<ChatMessage>>(),
			Arg.Any<ChatCompletionOptions>(),
			Arg.Any<CancellationToken>())
			.Returns(Task.FromException<ChatCompletion>(new NotImplementedException()));

		var service = new AzureOpenAIService(mockClient);

		// Act & Assert - we expect it to call the client even though it throws
		try
		{
			await service.GenerateCommitMessageSuggestionsAsync(
				TestPatch, TestApiKey, TestEndpoint, TestDeployment);
		}
		catch
		{
			// Expected
		}

		await mockClient.Received(1).CompleteChatAsync(
			Arg.Any<IEnumerable<ChatMessage>>(),
			Arg.Any<ChatCompletionOptions>(),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_CancellationToken_IsPropagated()
	{
		// Arrange
		var mockClient = Substitute.For<IAzureOpenAIClient>();
		var cts = new CancellationTokenSource();
		cts.Cancel();

		mockClient.CompleteChatAsync(
			Arg.Any<IEnumerable<ChatMessage>>(),
			Arg.Any<ChatCompletionOptions>(),
			Arg.Any<CancellationToken>())
			.Returns(Task.FromCanceled<ChatCompletion>(cts.Token));

		var service = new AzureOpenAIService(mockClient);

		// Act & Assert - TaskCanceledException is a subclass of OperationCanceledException
		await Assert.ThrowsAsync<TaskCanceledException>(() =>
			service.GenerateCommitMessageSuggestionsAsync(
				TestPatch, TestApiKey, TestEndpoint, TestDeployment, cts.Token));
	}

	[Fact]
	public async Task GenerateCommitMessageSuggestionsAsync_PassesTwoMessagesToClient()
	{
		// Arrange
		var mockClient = Substitute.For<IAzureOpenAIClient>();
		var capturedMessages = new List<ChatMessage>();

		mockClient.CompleteChatAsync(
			Arg.Do<IEnumerable<ChatMessage>>(msgs => capturedMessages.AddRange(msgs)),
			Arg.Any<ChatCompletionOptions>(),
			Arg.Any<CancellationToken>())
			.Returns(Task.FromException<ChatCompletion>(new NotImplementedException()));

		var service = new AzureOpenAIService(mockClient);

		// Act
		try
		{
			await service.GenerateCommitMessageSuggestionsAsync(
				TestPatch, TestApiKey, TestEndpoint, TestDeployment);
		}
		catch
		{
			// Expected
		}

		// Assert
		Assert.Equal(2, capturedMessages.Count);
		Assert.IsType<SystemChatMessage>(capturedMessages[0]);
		Assert.IsType<UserChatMessage>(capturedMessages[1]);
	}
}
