using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

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
	private const string API_VERSION = "2024-10-21";

	public async Task<string> CompleteAsync(
		CompletionRequest request,
		CancellationToken cancellationToken = default)
	{
		var body = new RequestBody(
			[
				new Message("system", request.SystemPrompt),
				new Message("user", request.UserPrompt),
			],
			request.MaxTokens,
			request.Temperature);

		var url =
			$"{config.Endpoint!.TrimEnd('/')}/openai/deployments/{config.Deployment}/chat/completions?api-version={API_VERSION}";

		using var message = new HttpRequestMessage(HttpMethod.Post, url);
		message.Content = JsonContent.Create(body);
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

		ResponseBody? result = await response.Content
			.ReadFromJsonAsync<ResponseBody>(cancellationToken)
			.ConfigureAwait(false);

		return result?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
	}

	// Serialized to the request body by the JSON serializer; never read in code.
	[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
	private sealed record RequestBody(
		[property: JsonPropertyName("messages")]
		IReadOnlyList<Message> Messages,
		[property: JsonPropertyName("max_tokens")]
		int MaxTokens,
		[property: JsonPropertyName("temperature")]
		double Temperature)
	{
		[JsonPropertyName("frequency_penalty")]
		public int FrequencyPenalty { get; init; }

		[JsonPropertyName("presence_penalty")]
		public int PresencePenalty { get; init; }

		[JsonPropertyName("top_p")]
		public double TopP { get; init; } = 1.0;
	}

	[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
	private sealed record Message(
		[property: JsonPropertyName("role")] string Role,
		[property: JsonPropertyName("content")]
		string Content);

	private sealed record ResponseBody(
		[property: JsonPropertyName("choices")]
		IReadOnlyList<Choice>? Choices);

	private sealed record Choice(
		[property: JsonPropertyName("message")]
		ChoiceMessage? Message);

	private sealed record ChoiceMessage(
		[property: JsonPropertyName("content")]
		string? Content);
}
