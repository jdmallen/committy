using System.Net;
using System.Text;
using System.Text.Json;

namespace JDMallen.Committy.Tests;

public class AzureProviderTests
{
	private static CommittyConfig Wrap(
		bool reasoningModel = false,
		int? maxTokens = null) =>
		new()
		{
			Provider = Provider.Azure,
			Azure = new AzureConfig
			{
				ApiKey = "k",
				Endpoint = "https://example.openai.azure.com",
				Deployment = "gpt-6-luna",
				ReasoningModel = reasoningModel,
				MaxTokens = maxTokens,
			},
		};

	[Fact]
	public void MaxTokensOverride_NotReasoning_KeepsThePromptDefault()
		=> Assert.Null(Wrap().MaxTokensOverride);

	[Fact]
	public void MaxTokensOverride_Reasoning_DefaultsHigher() => Assert.Equal(
		CommittyConfigResolver.DEFAULT_AZURE_REASONING_MAX_TOKENS,
		Wrap(true).MaxTokensOverride);

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void MaxTokensOverride_Configured_Wins(bool reasoningModel)
		=> Assert.Equal(4096, Wrap(reasoningModel, 4096).MaxTokensOverride);

	[Theory]
	[InlineData("y", "true")]
	[InlineData("YES", "true")]
	[InlineData(" true ", "true")]
	[InlineData("n", "false")]
	[InlineData("No", "false")]
	[InlineData("false", "false")]
	[InlineData("maybe", null)]
	[InlineData(null, null)]
	public void NormalizeBool_AcceptsYesNoCaseInsensitively(
		string? input,
		string? expected) => Assert.Equal(
		expected,
		CommittyConfigResolver.NormalizeBool(input));

	[Theory]
	[InlineData("2048", 2048)]
	[InlineData(" 512 ", 512)]
	[InlineData("0", null)]
	[InlineData("abc", null)]
	[InlineData(null, null)]
	public void ParseOptionalPositiveInt_UnsetOrInvalidIsNull(
		string? input,
		int? expected) => Assert.Equal(
		expected,
		CommittyConfigResolver.ParseOptionalPositiveInt(input));

	[Fact]
	public async Task
		Factory_Reasoning_SendsCompletionTokensWithoutTemperature()
	{
		var handler = new CapturingHandler();
		using var http = new HttpClient(handler);
		CommittyConfig config = Wrap(true);
		IChatCompletionClient client =
			new ChatCompletionClientFactory(http).Create(config);

		await new CommitMessageGenerator(client, config.MaxTokensOverride)
			.GenerateAsync("diff --git a/x b/x", true);

		using JsonDocument body = JsonDocument.Parse(handler.RequestBody!);
		JsonElement root = body.RootElement;
		Assert.Equal(
			CommittyConfigResolver.DEFAULT_AZURE_REASONING_MAX_TOKENS,
			root.GetProperty("max_completion_tokens").GetInt32());
		Assert.False(root.TryGetProperty("max_tokens", out _));
		Assert.False(root.TryGetProperty("temperature", out _));
	}

	[Fact]
	public async Task Factory_NotReasoning_StillSendsTemperature()
	{
		var handler = new CapturingHandler();
		using var http = new HttpClient(handler);
		CommittyConfig config = Wrap();
		IChatCompletionClient client =
			new ChatCompletionClientFactory(http).Create(config);

		await new CommitMessageGenerator(client, config.MaxTokensOverride)
			.GenerateAsync("diff --git a/x b/x", false);

		using JsonDocument body = JsonDocument.Parse(handler.RequestBody!);
		Assert.True(body.RootElement.TryGetProperty("temperature", out _));
	}

	/// <summary>Records the request body and answers with one canned message.</summary>
	private sealed class CapturingHandler : HttpMessageHandler
	{
		public string? RequestBody { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			RequestBody = request.Content is null
				? null
				: await request.Content.ReadAsStringAsync(cancellationToken);

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(
					"""{ "choices": [ { "message": { "content": "feat: add thing" } } ] }""",
					Encoding.UTF8,
					"application/json"),
			};
		}
	}
}
