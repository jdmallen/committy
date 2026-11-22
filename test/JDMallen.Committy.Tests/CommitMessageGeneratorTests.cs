using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace JDMallen.Committy.Tests;

public class CommitMessageGeneratorTests
{
	private const string
		PATCH = "diff --git a/file.txt b/file.txt\n+added line";

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task GenerateAsync_InvalidPatch_ThrowsArgumentException(
		string patch)
	{
		var generator =
			new CommitMessageGenerator(Substitute.For<IChatCompletionClient>());

		await Assert.ThrowsAsync<ArgumentException>(() =>
			generator.GenerateAsync(patch, false));
	}

	[Fact]
	public async Task
		GenerateAsync_ClientThrows_WrapsInInvalidOperationException()
	{
		var client = Substitute.For<IChatCompletionClient>();
		var inner = new HttpRequestException("boom");
		client.GetCompletionAsync(
				Arg.Any<CompletionRequest>(),
				Arg.Any<CancellationToken>())
			.ThrowsAsync(inner);

		var generator = new CommitMessageGenerator(client);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(()
			=>
			generator.GenerateAsync(PATCH, false));

		Assert.Equal("provider request failed: boom", exception.Message);
		Assert.Equal(inner, exception.InnerException);
	}

	[Fact]
	public async Task GenerateAsync_HttpTimeout_ThrowsTimeoutNamingSetting()
	{
		var client = Substitute.For<IChatCompletionClient>();
		var inner = new TaskCanceledException(
			"The request was canceled due to the configured HttpClient.Timeout of 300 seconds elapsing.");
		client.GetCompletionAsync(
				Arg.Any<CompletionRequest>(),
				Arg.Any<CancellationToken>())
			.ThrowsAsync(inner);

		var generator = new CommitMessageGenerator(
			client,
			timeoutSeconds: 300,
			timeoutSettingKey: "committy.openai.timeoutseconds");

		var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
			generator.GenerateAsync(PATCH, false));

		Assert.Equal(
			"The provider did not answer within 300 seconds. Raise committy.openai.timeoutseconds to wait longer.",
			exception.Message);
		Assert.Equal(inner, exception.InnerException);
	}

	[Fact]
	public async Task GenerateAsync_HttpTimeoutWithoutSetting_OmitsHint()
	{
		var client = Substitute.For<IChatCompletionClient>();
		client.GetCompletionAsync(
				Arg.Any<CompletionRequest>(),
				Arg.Any<CancellationToken>())
			.ThrowsAsync(new TaskCanceledException());

		var generator = new CommitMessageGenerator(client, timeoutSeconds: 30);

		var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
			generator.GenerateAsync(PATCH, false));

		Assert.Equal(
			"The provider did not answer within 30 seconds.",
			exception.Message);
	}

	[Fact]
	public async Task GenerateAsync_OneSecondTimeout_UsesSingular()
	{
		var client = Substitute.For<IChatCompletionClient>();
		client.GetCompletionAsync(
				Arg.Any<CompletionRequest>(),
				Arg.Any<CancellationToken>())
			.ThrowsAsync(new TaskCanceledException());

		var generator = new CommitMessageGenerator(client, timeoutSeconds: 1);

		var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
			generator.GenerateAsync(PATCH, false));

		Assert.Equal(
			"The provider did not answer within 1 second.",
			exception.Message);
	}

	[Fact]
	public async Task GenerateAsync_CallerCancels_RethrowsCancellation()
	{
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();
		var client = Substitute.For<IChatCompletionClient>();
		client.GetCompletionAsync(
				Arg.Any<CompletionRequest>(),
				Arg.Any<CancellationToken>())
			.ThrowsAsync(new TaskCanceledException());

		var generator = new CommitMessageGenerator(client, timeoutSeconds: 30);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			generator.GenerateAsync(PATCH, false, cancellation.Token));
	}

	[Fact]
	public async Task GenerateAsync_TitleAndBody_PassesRequestAndParsesResult()
	{
		var client = Substitute.For<IChatCompletionClient>();
		client.GetCompletionAsync(
				Arg.Any<CompletionRequest>(),
				Arg.Any<CancellationToken>())
			.Returns(
				new CompletionResult("feat: do thing\n\nDetail.")
				{
					Usage = new TokenUsage(1200, 34),
				});

		var generator = new CommitMessageGenerator(client);

		CommitSuggestions result = await generator.GenerateAsync(PATCH, false);

		Assert.Single(result.Messages);
		Assert.Equal("feat: do thing\n\nDetail.", result.Messages[0]);
		Assert.Equal(new TokenUsage(1200, 34), result.Usage);

		// The generator builds the prompt; title+body mode uses the larger token budget.
		await client.Received(1)
			.GetCompletionAsync(
				Arg.Is<CompletionRequest>(r
					=> r.MaxTokens == 500 && r.UserPrompt.Contains(PATCH)),
				Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GenerateAsync_TitlesOnly_UsesTitlesPromptBudget()
	{
		var client = Substitute.For<IChatCompletionClient>();
		client.GetCompletionAsync(
				Arg.Any<CompletionRequest>(),
				Arg.Any<CancellationToken>())
			.Returns(new CompletionResult("feat: a\nfix: b"));

		var generator = new CommitMessageGenerator(client);

		CommitSuggestions result = await generator.GenerateAsync(PATCH, true);

		Assert.Equal(["feat: a", "fix: b"], result.Messages);
		Assert.Null(result.Usage);
		await client.Received(1)
			.GetCompletionAsync(
				Arg.Is<CompletionRequest>(r => r.MaxTokens == 100),
				Arg.Any<CancellationToken>());
	}
}
