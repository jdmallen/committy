using System.Text;
using System.Text.Json;

namespace Committy;

public class AzureOpenAIService(IHttpService httpService) : IAzureOpenAIService
{
	private const string ResourceUrlFormat =
		"/openai/deployments/{0}/chat/completions?api-version=2024-10-21";

	private static readonly JsonSerializerOptions JsonOptions = new()	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
	};

	public AzureOpenAIService() : this(new HttpService()) { }

	public async Task<List<string>> GenerateCommitMessageSuggestionsAsync(
		string patch,
		string apiKey,
		string endpoint,
		string deploymentName,
		CancellationToken cancellationToken = default)
	{
		var request = new
		{
			messages = new[]
			{
				new
				{
					role = "system",
					content = SystemPrompt,
				},
				new { role = "user", content = BuildUserPrompt(patch) },
			},
			max_tokens = 100,
			temperature = 0.1,
			top_p = 1.0,
			frequency_penalty = 0,
			presence_penalty = 0,
		};

		string json = JsonSerializer.Serialize(request, JsonOptions);

		var content = new StringContent(json, Encoding.UTF8, "application/json");

		string requestUrl = string.Format(ResourceUrlFormat, deploymentName);

		using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl);
		requestMessage.Content = content;
		requestMessage.Headers.Add("api-key", apiKey);
		HttpResponseMessage response = await httpService.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			string errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

			throw new HttpRequestException(
				$"Azure OpenAI API request failed: {response.StatusCode} - {errorContent}");
		}

		string responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		var responseObj = JsonSerializer.Deserialize<JsonElement>(responseContent);
		string? messageContent = responseObj
			.GetProperty("choices")[0]
			.GetProperty("message")
			.GetProperty("content")
			.GetString();
		List<string> suggestions =
			ParseSuggestions(messageContent?.Trim() ?? "feat: implement changes");

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

	private static string BuildUserPrompt(string patch) =>
		string.Format(UserPromptTemplate, patch);

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
