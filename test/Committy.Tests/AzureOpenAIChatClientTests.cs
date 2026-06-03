using System.Net;
using System.Text;
using NSubstitute;

namespace Committy.Tests;

public class AzureOpenAIChatClientTests
{
	private static readonly AzureConfig Config = new()
	{
		ApiKey = "test-azure-key",
		Endpoint = "https://test.openai.azure.com",
		Deployment = "gpt-4",
	};

	private static readonly CompletionRequest Request = new("system", "user prompt", 500);

	private static HttpResponseMessage Json(string content, HttpStatusCode code = HttpStatusCode.OK)
		=>
			new(code) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

	[Fact]
	public async Task CompleteAsync_CancellationToken_IsPropagated()
	{
		var http = Substitute.For<IHttpService>();
		var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		http.SendAsync(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromCanceled<HttpResponseMessage>(cts.Token));

		var client = new AzureOpenAIChatClient(http, Config);

		await Assert.ThrowsAsync<TaskCanceledException>(() => client.CompleteAsync(Request, cts.Token));
	}

	[Fact]
	public async Task CompleteAsync_ErrorResponse_ThrowsHttpRequestException()
	{
		var http = Substitute.For<IHttpService>();
		http.SendAsync(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
			.Returns(Json("Unauthorized", HttpStatusCode.Unauthorized));

		var client = new AzureOpenAIChatClient(http, Config);

		var exception
			= await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync(Request));

		Assert.Contains("Azure OpenAI API request failed: Unauthorized", exception.Message);
	}

	[Fact]
	public async Task CompleteAsync_Success_ReturnsContentAndSendsAzureShape()
	{
		var http = Substitute.For<IHttpService>();
		string? apiKey = null;
		string? uri = null;
		string? body = null;

		http.SendAsync(
				Arg.Do<HttpRequestMessage>(m =>
				{
					apiKey = m.Headers.GetValues("api-key").FirstOrDefault();
					uri = m.RequestUri?.ToString();
					body = m.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
				}),
				Arg.Any<CancellationToken>())
			.Returns(Json("""{ "choices": [ { "message": { "content": "feat: add thing" } } ] }"""));

		var client = new AzureOpenAIChatClient(http, Config);

		string result = await client.CompleteAsync(Request);

		Assert.Equal("feat: add thing", result);
		Assert.Equal("test-azure-key", apiKey);
		Assert.Contains("/openai/deployments/gpt-4/chat/completions", uri);
		Assert.Contains("api-version=", uri);
		Assert.Contains("\"max_tokens\":500", body);
		Assert.Contains("\"role\":\"system\"", body);
	}
}
