using System.ClientModel;
using Azure.AI.OpenAI;
using OpenAI;
using OpenAI.Chat;

namespace Committy;

public class AzureOpenAIService : IAzureOpenAIService
{
	// private readonly IAzureOpenAIClient? _client;

	// public AzureOpenAIService() { }

	// public AzureOpenAIService(IAzureOpenAIClient client)
	// {
	// 	_client = client;
	// }

	public async Task<List<string>> GenerateCommitMessageSuggestionsAsync(
		string patch,
		string apiKey,
		string endpoint,
		string deploymentName,
		CancellationToken cancellationToken = default)
	{
		// IAzureOpenAIClient client = _client ?? new AzureOpenAIClientWrapper(deploymentName, apiKey, endpoint);

		AzureOpenAIClient azureClient = new(
			new Uri(endpoint),
			new ApiKeyCredential(apiKey));

		ChatClient chatClient = azureClient.GetChatClient(deploymentName);

		var options = new ChatCompletionOptions
		{
			MaxOutputTokenCount = 100,
			ResponseFormat = ChatResponseFormat.CreateTextFormat(),
			StoredOutputEnabled = false,
			Temperature = 0.1F,
			TopP = 1.0F,
			FrequencyPenalty = 0,
			PresencePenalty = 0,
			ToolChoice = ChatToolChoice.CreateNoneChoice(),
		};

		var messages = new List<ChatMessage> { BuildSystemPrompt(), BuildUserPrompt(patch) };

		ChatCompletion completion = null;

		try
		{
			completion = await chatClient.CompleteChatAsync(messages, options, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			throw;
		}

		List<string> suggestions =
			ParseSuggestions(completion.Content[0].Text ?? "feat: implement changes");

		return suggestions;
	}

	private const string SystemPrompt =
		"""
		You are a helpful assistant that generates conventional commit messages.You are a git and 
		software engineering expert whose job it is to quickly investigate diffs for staged code just 
		prior to a commit and make suggestions for a git commit message.
		""";

	private const string UserPromptTemplate =
		"""
		Generate exactly 5 different commit messages following Conventional Commits v1.0.0 specification.

		FORMAT: <type>[optional scope]: <description>

		TYPES:
		- feat: new feature
		- fix: bug fix
		- docs: documentation
		- style: code style/formatting
		- refactor: code refactoring
		- perf: performance improvement
		- test: adding/updating tests
		- build: build system changes
		- ci: CI configuration
		- chore: maintenance tasks

		RULES:
		1. Use imperative mood: 'add' not 'adds' or 'added'
		2. No period at end
		3. Keep under 50 characters when possible
		4. Add scope when it clarifies context
		5. Use ! for breaking changes: feat!: or feat(api)!:

		EXAMPLES:
		feat(auth): add OAuth2 integration
		fix(api): prevent memory leak in parser
		docs: update installation guide
		perf(db): optimize query performance
		feat!: remove deprecated login API

		Git patch:
		```
		{0}
		```

		Return exactly 5 commit messages, one per line, with no numbering, quotation marts, nor bullets:
		""";

	private static SystemChatMessage BuildSystemPrompt() => new SystemChatMessage(SystemPrompt);

	private static UserChatMessage BuildUserPrompt(string patch) =>
		new UserChatMessage(string.Format(UserPromptTemplate, patch));

	private static List<string> ParseSuggestions(string response)
	{
		var suggestions = new List<string>(5);
		string[] lines = response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

		suggestions.AddRange(
			lines
				.Select(line => line.Trim())
				.Where(trimmed => !string.IsNullOrEmpty(trimmed)));

		while (suggestions.Count < 5)
		{
			suggestions.Add($"feat: implement changes ({suggestions.Count + 1})");
		}

		return suggestions;
	}
}
