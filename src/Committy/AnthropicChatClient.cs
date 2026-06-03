using System.Text;
using System.Text.Json;

namespace Committy;

/// <summary>
/// Anthropic (Claude) implementation of <see cref="IChatCompletionClient" />. Owns
/// only
/// the Anthropic wire format: the Messages API endpoint, <c>x-api-key</c> +
/// <c>anthropic-version</c> headers, a top-level <c>system</c> prompt, and the
/// <c>content[0].text</c> response shape.
/// </summary>
public sealed class AnthropicChatClient(IHttpService http, AnthropicConfig config)
	: IChatCompletionClient
{
	private const string URL = "https://api.anthropic.com/v1/messages";
	private const string ANTHROPIC_VERSION = "2023-06-01";

	public async Task<string> CompleteAsync(
		CompletionRequest request,
		CancellationToken cancellationToken = default)
	{
		var body = new
		{
			model = config.Model,
			max_tokens = request.MaxTokens,
			temperature = request.Temperature,
			system = request.SystemPrompt,
			messages = new[]
			{
				new
				{
					role = "user",
					content = request.UserPrompt,
				},
			},
		};

		using var message = new HttpRequestMessage(HttpMethod.Post, URL);
		message.Content = new StringContent(
			JsonSerializer.Serialize(body),
			Encoding.UTF8,
			"application/json");
		message.Headers.Add("x-api-key", config.ApiKey);
		message.Headers.Add("anthropic-version", ANTHROPIC_VERSION);

		HttpResponseMessage response
			= await http.SendAsync(message, cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			string error = await response.Content.ReadAsStringAsync(cancellationToken)
				.ConfigureAwait(false);

			throw new HttpRequestException(
				$"Anthropic API request failed: {response.StatusCode} - {error}");
		}

		string content
			= await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		var json = JsonSerializer.Deserialize<JsonElement>(content);

		// The Messages API returns an array of content blocks; concatenate the text ones.
		var sb = new StringBuilder();

		foreach (JsonElement block in json.GetProperty("content").EnumerateArray())
		{
			if (block.TryGetProperty("type", out JsonElement type) && type.GetString() == "text")
			{
				sb.Append(block.GetProperty("text").GetString());
			}
		}

		return sb.ToString();
	}
}
