using System.ClientModel;
using OpenAI;
using OpenAI.Chat;

namespace Committy;

public class AzureOpenAIClientWrapper : IAzureOpenAIClient
{
	private readonly ChatClient _chatClient;

	public AzureOpenAIClientWrapper(string deploymentName, string apiKey, string endpoint)
	{
		_chatClient = new ChatClient(
			model: deploymentName,
			credential: new ApiKeyCredential(apiKey),
			options: new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
	}

	public async Task<ChatCompletion> CompleteChatAsync(
		IEnumerable<ChatMessage> messages,
		ChatCompletionOptions options,
		CancellationToken cancellationToken = default)
	{
		return await _chatClient.CompleteChatAsync(messages, options, cancellationToken).ConfigureAwait(false);
	}
}