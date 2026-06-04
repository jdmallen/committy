using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

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
		var body = new RequestBody(
			config.Model,
			request.MaxTokens,
			request.Temperature,
			request.SystemPrompt,
			[new Message("user", request.UserPrompt)]);

		using var message = new HttpRequestMessage(HttpMethod.Post, URL);
		message.Content = JsonContent.Create(body);
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

		ResponseBody? result = await response.Content
			.ReadFromJsonAsync<ResponseBody>(cancellationToken)
			.ConfigureAwait(false);

		// The Messages API returns an array of content blocks; concatenate the text ones.
		var sb = new StringBuilder();

		foreach (Block block in (result?.Content ?? []).Where(block => block.Type == "text"))
		{
			sb.Append(block.Text);
		}

		return sb.ToString();
	}

	// Serialized to the request body by the JSON serializer; never read in code.
	[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
	private sealed record RequestBody(
		[property: JsonPropertyName("model")] string Model,
		[property: JsonPropertyName("max_tokens")]
		int MaxTokens,
		[property: JsonPropertyName("temperature")]
		double Temperature,
		[property: JsonPropertyName("system")] string System,
		[property: JsonPropertyName("messages")]
		IReadOnlyList<Message> Messages);

	[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
	private sealed record Message(
		[property: JsonPropertyName("role")] string Role,
		[property: JsonPropertyName("content")]
		string Content);

	private sealed record ResponseBody(
		[property: JsonPropertyName("content")]
		IReadOnlyList<Block>? Content);

	private sealed record Block(
		[property: JsonPropertyName("type")] string? Type,
		[property: JsonPropertyName("text")] string? Text);
}
