using System.Net;
using System.Text;
using NSubstitute;

namespace Committy.Tests;

public class AnthropicChatClientTests
{
	private static readonly AnthropicConfig Config = new()
	{
		ApiKey = "test-anthropic-key",
		Model = "claude-haiku-4-5-20251001",
	};

	private static readonly CompletionRequest Request = new("system", "user prompt", 500);

	private static HttpResponseMessage Json(string content, HttpStatusCode code = HttpStatusCode.OK)
		=>
			new(code) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

	[Fact]
	public async Task CompleteAsync_ErrorResponse_ThrowsHttpRequestException()
	{
		var http = Substitute.For<IHttpService>();
		http.SendAsync(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
			.Returns(Json("bad request", HttpStatusCode.BadRequest));

		var client = new AnthropicChatClient(http, Config);

		var exception
			= await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync(Request));

		Assert.Contains("Anthropic API request failed: BadRequest", exception.Message);
	}

	[Fact]
	public async Task CompleteAsync_SkipsNonTextBlocks()
	{
		var http = Substitute.For<IHttpService>();
		http.SendAsync(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
			.Returns(
				Json(
					"""
					{ "content": [
						{ "type": "thinking", "thinking": "hmm" },
						{ "type": "text", "text": "fix: the bug" }
					] }
					"""));

		var client = new AnthropicChatClient(http, Config);

		string result = await client.CompleteAsync(Request);

		Assert.Equal("fix: the bug", result);
	}

	[Fact]
	public async Task CompleteAsync_Success_ReturnsTextAndSendsAnthropicShape()
	{
		var http = Substitute.For<IHttpService>();
		string? apiKey = null;
		string? version = null;
		string? uri = null;
		string? body = null;

		http.SendAsync(
				Arg.Do<HttpRequestMessage>(m =>
				{
					apiKey = m.Headers.GetValues("x-api-key").FirstOrDefault();
					version = m.Headers.GetValues("anthropic-version").FirstOrDefault();
					uri = m.RequestUri?.ToString();
					body = m.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
				}),
				Arg.Any<CancellationToken>())
			.Returns(
				Json("""{ "content": [ { "type": "text", "text": "feat: add thing\n\nDetail." } ] }"""));

		var client = new AnthropicChatClient(http, Config);

		string result = await client.CompleteAsync(Request);

		Assert.Equal("feat: add thing\n\nDetail.", result);
		Assert.Equal("test-anthropic-key", apiKey);
		Assert.Equal("2023-06-01", version);
		Assert.Equal("https://api.anthropic.com/v1/messages", uri);
		Assert.Contains("\"model\":\"claude-haiku-4-5-20251001\"", body);
		Assert.Contains("\"system\":\"system\"", body);
		Assert.Contains("\"max_tokens\":500", body);
	}
}
