namespace Committy.Tests;

public class CommitMessageParserTests
{
	[Fact]
	public void Parse_TitleAndBody_Empty_ReturnsFallback()
	{
		List<string> result = CommitMessageParser.Parse("", false);

		Assert.Single(result);
		Assert.Equal("feat: implement changes", result[0]);
	}

	[Fact]
	public void Parse_TitleAndBody_NormalizesCrlf()
	{
		List<string> result = CommitMessageParser.Parse(
			"feat: add thing\r\n\r\nDoes the thing.\r\nMore detail.",
			false);

		Assert.Single(result);
		Assert.DoesNotContain("\r", result[0]);
		Assert.Equal("feat: add thing\n\nDoes the thing.\nMore detail.", result[0]);
	}

	[Fact]
	public void Parse_TitleAndBody_ReturnsSingleMessage()
	{
		const string message = "feat(auth): add OAuth2\n\nReplace the password flow with OAuth2.";

		List<string> result = CommitMessageParser.Parse(message, false);

		Assert.Single(result);
		Assert.Equal(message, result[0]);
	}

	[Fact]
	public void Parse_TitlesOnly_Empty_ReturnsFiveFallbacks()
	{
		List<string> result = CommitMessageParser.Parse("", true);

		Assert.Equal(5, result.Count);
		Assert.All(result, line => Assert.StartsWith("feat: implement changes", line));
	}

	[Fact]
	public void Parse_TitlesOnly_FillsToFive()
	{
		List<string> result = CommitMessageParser.Parse(
			"feat: add feature\nfix: resolve issue",
			true);

		Assert.Equal(5, result.Count);
		Assert.Equal("feat: add feature", result[0]);
		Assert.Equal("fix: resolve issue", result[1]);
		Assert.Equal("feat: implement changes (3)", result[2]);
		Assert.Equal("feat: implement changes (4)", result[3]);
		Assert.Equal("feat: implement changes (5)", result[4]);
	}

	[Fact]
	public void Parse_TitlesOnly_ReturnsFiveLines()
	{
		List<string> result = CommitMessageParser.Parse(
			"feat: add feature\nfix: bug\ndocs: update readme",
			true);

		Assert.Equal(5, result.Count);
		Assert.Equal("feat: add feature", result[0]);
		Assert.Equal("fix: bug", result[1]);
		Assert.Equal("docs: update readme", result[2]);
		Assert.StartsWith("feat: implement changes", result[3]);
		Assert.StartsWith("feat: implement changes", result[4]);
	}
}
