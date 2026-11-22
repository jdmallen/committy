namespace JDMallen.Committy.Tests;

/// <summary>
/// The hook runs on every <c>git commit</c>: it must never throw, never
/// write a non-comment line it did not get from the model, and leave the
/// file alone when there is nothing to do.
/// </summary>
public class PrepareCommitMsgCommandTests : IDisposable
{
	private const string GIT_TEMPLATE =
		"\n# Please enter the commit message for your changes.\n";

	private readonly string _commitMsgFile = Path.GetTempFileName();

	public PrepareCommitMsgCommandTests() =>
		File.WriteAllText(_commitMsgFile, GIT_TEMPLATE);

	public void Dispose() => File.Delete(_commitMsgFile);

	private static CommittyConfig ReadyConfig() =>
		new()
		{
			Provider = new OpenAIConfig
			{
				BaseUrl = "http://127.0.0.1:9/v1",
				Model = "test-model",
				TimeoutSeconds = 30,
				MaxTokens = 2048,
			},
		};

	private static HookSteps Steps(
		string? patch = "diff --git a/f b/f\n+x",
		CommittyConfig? config = null,
		Func<CommitSuggestions>? generate = null,
		List<TokenUsage?>? recorded = null) =>
		new(
			_ => Task.FromResult(patch),
			_ => Task.FromResult(config ?? ReadyConfig()),
			(_, _, _) => Task.FromResult(
				generate?.Invoke()
				?? throw new InvalidOperationException("generate not set")),
			usage => recorded?.Add(usage));

	private string AppendedText() =>
		File.ReadAllText(_commitMsgFile)[GIT_TEMPLATE.Length..];

	private void AssertOnlyCommentsAppended()
	{
		string text = File.ReadAllText(_commitMsgFile);
		Assert.StartsWith(GIT_TEMPLATE, text);
		string[] appended = AppendedText()
			.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		Assert.NotEmpty(appended);
		Assert.All(appended, line => Assert.StartsWith("#", line));
	}

	[Fact]
	public async Task RunAsync_NothingStaged_LeavesFileUntouched()
	{
		await PrepareCommitMsgCommand.RunAsync(
			_commitMsgFile,
			Steps(patch: null),
			TestContext.Current.CancellationToken);

		Assert.Equal(GIT_TEMPLATE, await File.ReadAllTextAsync(
			_commitMsgFile,
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task RunAsync_ConfigProblem_AppendsCommentsOnly()
	{
		var incomplete = new CommittyConfig
		{
			Provider = new OpenAIConfig
			{
				BaseUrl = null,
				Model = "test-model",
				TimeoutSeconds = 30,
				MaxTokens = 2048,
			},
		};

		await PrepareCommitMsgCommand.RunAsync(
			_commitMsgFile,
			Steps(config: incomplete),
			TestContext.Current.CancellationToken);

		AssertOnlyCommentsAppended();
		Assert.Contains("OpenAI base URL is required", AppendedText());
		Assert.Contains("committy config", AppendedText());
	}

	[Fact]
	public async Task RunAsync_EmptyModelResponse_AppendsNoMessageComment()
	{
		var recorded = new List<TokenUsage?>();
		var usage = new TokenUsage(100, 0);

		await PrepareCommitMsgCommand.RunAsync(
			_commitMsgFile,
			Steps(
				generate: () => new CommitSuggestions([], usage),
				recorded: recorded),
			TestContext.Current.CancellationToken);

		AssertOnlyCommentsAppended();
		Assert.Contains("The model returned no message", AppendedText());
		Assert.Equal([usage], recorded);
	}

	[Fact]
	public async Task RunAsync_GenerateThrows_CommentsEveryLineAndNeverThrows()
	{
		await PrepareCommitMsgCommand.RunAsync(
			_commitMsgFile,
			Steps(
				generate: () => throw new InvalidOperationException(
					"provider request failed: boom\nsecond line from az")),
			TestContext.Current.CancellationToken);

		AssertOnlyCommentsAppended();
		Assert.Contains("provider request failed: boom", AppendedText());
		Assert.Contains("second line from az", AppendedText());
	}

	[Fact]
	public async Task RunAsync_Suggestion_WritesMessageAboveTemplate()
	{
		var recorded = new List<TokenUsage?>();
		var usage = new TokenUsage(1200, 34);

		await PrepareCommitMsgCommand.RunAsync(
			_commitMsgFile,
			Steps(
				generate: () => new CommitSuggestions(
					["feat: add thing\n\nWhy it matters."],
					usage),
				recorded: recorded),
			TestContext.Current.CancellationToken);

		string text = await File.ReadAllTextAsync(
			_commitMsgFile,
			TestContext.Current.CancellationToken);
		Assert.StartsWith("feat: add thing\n\nWhy it matters.", text);
		Assert.Contains("# Please enter the commit message", text);
		Assert.Equal([usage], recorded);
	}
}
