namespace Committy.Tests;

public class CommitMessageComposerTests
{
	private const string EXISTING =
		"\n# Please enter the commit message for your changes.\n# Lines starting with '#' will be ignored.\n";

	[Fact]
	public void Compose_TitleAndBody_NormalizesCrlf()
	{
		const string message = "feat: add thing\r\n\r\nDetail.";

		string result = CommitMessageComposer.Compose([message], false, EXISTING);

		Assert.DoesNotContain("\r", result);
		Assert.StartsWith("feat: add thing\n\nDetail.\n", result);
	}

	[Fact]
	public void Compose_TitleAndBody_PrefillsMessageAboveExistingContent()
	{
		const string message = "feat(auth): add OAuth2\n\nReplace the password flow with OAuth2.";

		string result = CommitMessageComposer.Compose([message], false, EXISTING);

		// Message comes first, then a blank line, then git's template.
		Assert.StartsWith(
			"feat(auth): add OAuth2\n\nReplace the password flow with OAuth2.\n\n",
			result);
		Assert.Contains("# Please enter the commit message", result);
		Assert.DoesNotContain("\r", result);
	}

	[Fact]
	public void Compose_TitlesOnly_CommentsEachSuggestionAndPreservesExisting()
	{
		List<string> suggestions =
		[
			"feat: add feature",
			"fix: resolve bug",
			"docs: update readme",
			"refactor: tidy parser",
			"chore: bump deps",
		];

		string result = CommitMessageComposer.Compose(suggestions, true, EXISTING);

		foreach (string suggestion in suggestions)
		{
			Assert.Contains($"# {suggestion}", result);
		}

		Assert.Contains("Suggested commit messages from Committy", result);
		Assert.Contains("# Or write your own commit message above", result);
		Assert.Contains("# Please enter the commit message", result);
	}

	[Fact]
	public void Compose_TitlesOnly_EmptyExisting_DoesNotThrow()
	{
		string result = CommitMessageComposer.Compose(["feat: x"], true, string.Empty);

		Assert.Contains("# feat: x", result);
		Assert.EndsWith("# Or write your own commit message above\n", result);
	}
}
