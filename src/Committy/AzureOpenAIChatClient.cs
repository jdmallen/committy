using System.Text;
using System.Text.Json;

namespace Committy;

/// <summary>
/// Azure OpenAI implementation of <see cref="IChatCompletionClient" />. Owns only
/// the
/// Azure wire format: deployment-in-path URL, <c>api-key</c> header, request body,
/// and
/// the <c>choices[0].message.content</c> response shape.
/// </summary>
public sealed class AzureOpenAIChatClient(IHttpService http, AzureConfig config)
	: IChatCompletionClient
{
	private const string ApiVersion = "2024-10-21";

	public async Task<string> CompleteAsync(
		CompletionRequest request,
		CancellationToken cancellationToken = default)
	{
		var body = new
		{
			messages = new[]
			{
				new
				{
					role = "system",
					content = request.SystemPrompt,
				},
				new
				{
					role = "user",
					content = request.UserPrompt,
				},
			},
			max_tokens = request.MaxTokens,
			temperature = request.Temperature,
			top_p = 1.0,
			frequency_penalty = 0,
			presence_penalty = 0,
		};

		var url =
			$"{config.Endpoint!.TrimEnd('/')}/openai/deployments/{config.Deployment}/chat/completions?api-version={ApiVersion}";

		using var message = new HttpRequestMessage(HttpMethod.Post, url);
		message.Content = new StringContent(
			JsonSerializer.Serialize(body),
			Encoding.UTF8,
			"application/json");
		message.Headers.Add("api-key", config.ApiKey);

		HttpResponseMessage response
			= await http.SendAsync(message, cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			string error = await response.Content.ReadAsStringAsync(cancellationToken)
				.ConfigureAwait(false);

			throw new HttpRequestException(
				$"Azure OpenAI API request failed: {response.StatusCode} - {error}");
		}

		string content
			= await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		var json = JsonSerializer.Deserialize<JsonElement>(content);

		return json
				.GetProperty("choices")[0]
				.GetProperty("message")
				.GetProperty("content")
				.GetString()
			?? string.Empty;
	}
}
