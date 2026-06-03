using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Committy.Tests;

public class CommitMessageGeneratorTests
{
	private const string PATCH = "diff --git a/file.txt b/file.txt\n+added line";

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task GenerateAsync_InvalidPatch_ThrowsArgumentException(string patch)
	{
		var generator = new CommitMessageGenerator(Substitute.For<IChatCompletionClient>());

		await Assert.ThrowsAsync<ArgumentException>(() =>
			generator.GenerateAsync(patch, false));
	}

	[Fact]
	public async Task GenerateAsync_ClientThrows_WrapsInInvalidOperationException()
	{
		var client = Substitute.For<IChatCompletionClient>();
		var inner = new HttpRequestException("boom");
		client.CompleteAsync(Arg.Any<CompletionRequest>(), Arg.Any<CancellationToken>())
			.ThrowsAsync(inner);

		var generator = new CommitMessageGenerator(client);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			generator.GenerateAsync(PATCH, false));

		Assert.StartsWith("Failed to generate commit message suggestions:", exception.Message);
		Assert.Equal(inner, exception.InnerException);
	}

	[Fact]
	public async Task GenerateAsync_TitleAndBody_PassesRequestAndParsesResult()
	{
		var client = Substitute.For<IChatCompletionClient>();
		client.CompleteAsync(Arg.Any<CompletionRequest>(), Arg.Any<CancellationToken>())
			.Returns("feat: do thing\n\nDetail.");

		var generator = new CommitMessageGenerator(client);

		List<string> result = await generator.GenerateAsync(PATCH, false);

		Assert.Single(result);
		Assert.Equal("feat: do thing\n\nDetail.", result[0]);

		// The generator builds the prompt; title+body mode uses the larger token budget.
		await client.Received(1)
			.CompleteAsync(
				Arg.Is<CompletionRequest>(r => r.MaxTokens == 500 && r.UserPrompt.Contains(PATCH)),
				Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GenerateAsync_TitlesOnly_UsesTitlesPromptBudget()
	{
		var client = Substitute.For<IChatCompletionClient>();
		client.CompleteAsync(Arg.Any<CompletionRequest>(), Arg.Any<CancellationToken>())
			.Returns("feat: a\nfix: b");

		var generator = new CommitMessageGenerator(client);

		List<string> result = await generator.GenerateAsync(PATCH, true);

		Assert.Equal(5, result.Count);
		await client.Received(1)
			.CompleteAsync(
				Arg.Is<CompletionRequest>(r => r.MaxTokens == 100),
				Arg.Any<CancellationToken>());
	}
}
