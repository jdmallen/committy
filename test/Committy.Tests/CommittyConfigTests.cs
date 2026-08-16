namespace Committy.Tests;

public class CommittyConfigTests
{
	[Theory]
	[InlineData("azure", Provider.Azure)]
	[InlineData("Azure", Provider.Azure)]
	[InlineData("anthropic", Provider.Anthropic)]
	[InlineData("claude", Provider.Anthropic)]
	[InlineData("openai", Provider.OpenAI)]
	[InlineData("OpenAI", Provider.OpenAI)]
	[InlineData("local", Provider.OpenAI)]
	[InlineData(null, Provider.Azure)]
	[InlineData("nonsense", Provider.Azure)]
	public void ParseProvider_MapsNamesCaseInsensitively(string? name, Provider expected)
	{
		Assert.Equal(expected, CommittyConfigResolver.ParseProvider(name));
	}

	[Fact]
	public void Validate_Anthropic_Complete_ReturnsNull()
	{
		var config = new CommittyConfig
		{
			Provider = Provider.Anthropic,
			Anthropic = new AnthropicConfig
			{
				ApiKey = "k",
				Model = "claude",
			},
		};

		Assert.Null(config.Validate());
	}

	[Fact]
	public void Validate_Anthropic_MissingApiKey_ReturnsError()
	{
		var config = new CommittyConfig
		{
			Provider = Provider.Anthropic,
			Anthropic = new AnthropicConfig
			{
				ApiKey = " ",
				Model = "claude",
			},
		};

		Assert.Contains("API key", config.Validate());
	}

	[Fact]
	public void Validate_Azure_Complete_ReturnsNull()
	{
		var config = new CommittyConfig
		{
			Provider = Provider.Azure,
			Azure = new AzureConfig
			{
				ApiKey = "k",
				Endpoint = "https://e",
				Deployment = "d",
			},
		};

		Assert.Null(config.Validate());
	}

	[Fact]
	public void Validate_Azure_MissingApiKey_ReturnsError()
	{
		var config = new CommittyConfig
		{
			Provider = Provider.Azure,
			Azure = new AzureConfig
			{
				ApiKey = null,
				Endpoint = "https://e",
				Deployment = "d",
			},
		};

		Assert.Contains("API key", config.Validate());
	}
}
