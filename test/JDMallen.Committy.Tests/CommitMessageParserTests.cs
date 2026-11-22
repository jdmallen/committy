namespace JDMallen.Committy.Tests;

public class CommitMessageParserTests
{
	[Theory]
	[InlineData(null, false)]
	[InlineData("", false)]
	[InlineData("  \r\n ", false)]
	[InlineData(null, true)]
	[InlineData("", true)]
	[InlineData("```\n```", true)]
	public void Parse_NothingUsable_ReturnsNoMessage(
		string? raw,
		bool titlesOnly) =>
		Assert.Empty(CommitMessageParser.Parse(raw, titlesOnly));

	[Fact]
	public void Parse_TitleAndBody_NormalizesCrlf()
	{
		List<string> result = CommitMessageParser.Parse(
			"feat: add thing\r\n\r\nDoes the thing.\r\nMore detail.",
			false);

		Assert.Single(result);
		Assert.Equal(
			"feat: add thing\n\nDoes the thing.\nMore detail.",
			result[0]);
	}

	[Fact]
	public void Parse_TitleAndBody_ReturnsSingleMessage()
	{
		const string message =
			"feat(auth): add OAuth2\n\nReplace the password flow with OAuth2.";

		List<string> result = CommitMessageParser.Parse(message, false);

		Assert.Single(result);
		Assert.Equal(message, result[0]);
	}

	[Fact]
	public void Parse_TitlesOnly_KeepsWhatCameBackWithoutPadding()
	{
		List<string> result = CommitMessageParser.Parse(
			"feat: add feature\nfix: resolve issue",
			true);

		Assert.Equal(["feat: add feature", "fix: resolve issue"], result);
	}

	[Fact]
	public void Parse_TitlesOnly_StripsListMarkersAndCodeFences()
	{
		List<string> result = CommitMessageParser.Parse(
			"```text\n1. feat: one\n2) fix: two\n- docs: three\n* test: four\n```",
			true);

		Assert.Equal(
			["feat: one", "fix: two", "docs: three", "test: four"],
			result);
	}

	[Fact]
	public void Parse_TitlesOnly_CapsAtFive()
	{
		List<string> result = CommitMessageParser.Parse(
			"feat: a\nfeat: b\nfeat: c\nfeat: d\nfeat: e\nfeat: f\nfeat: g",
			true);

		Assert.Equal(CommitMessageParser.MAX_TITLES, result.Count);
		Assert.Equal("feat: e", result[^1]);
	}

	[Fact]
	public void Parse_TitlesOnly_KeepsAHyphenInsideATitle()
		=> Assert.Equal(
			["fix: handle a half-written file"],
			CommitMessageParser.Parse("fix: handle a half-written file", true));
}
