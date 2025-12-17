using OpenAI.Chat;

namespace Committy;

public interface IAzureOpenAIClient
{
	Task<ChatCompletion> CompleteChatAsync(
		IEnumerable<ChatMessage> messages,
		ChatCompletionOptions options,
		CancellationToken cancellationToken = default);
}