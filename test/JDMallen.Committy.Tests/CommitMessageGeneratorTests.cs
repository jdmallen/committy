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

		Assert.StartsWith(
			"Failed to generate commit message suggestions:",
			exception.Message);
		Assert.Equal(inner, exception.InnerException);
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

		Assert.Equal(5, result.Messages.Count);
		Assert.Null(result.Usage);
		await client.Received(1)
			.GetCompletionAsync(
				Arg.Is<CompletionRequest>(r => r.MaxTokens == 100),
				Arg.Any<CancellationToken>());
	}
}
