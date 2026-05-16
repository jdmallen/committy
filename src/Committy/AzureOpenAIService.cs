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
		bool titlesOnly = false,
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
				new { role = "user", content = BuildUserPrompt(patch, titlesOnly) },
			},
			max_tokens = titlesOnly ? 100 : 500,
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
		string trimmed = (messageContent ?? string.Empty).Trim();

		return titlesOnly
			? ParseTitleSuggestions(string.IsNullOrEmpty(trimmed) ? "feat: implement changes" : trimmed)
			: ParseTitleAndBody(trimmed);
	}

	private const string SystemPrompt =
		"""
		You are a helpful assistant that generates conventional commit messages.You are a git and
		software engineering expert whose job it is to quickly investigate diffs for staged code just
		prior to a commit and make suggestions for a git commit message.
		""";

	private const string TitlesUserPromptTemplate =
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

	private const string TitleAndBodyUserPromptTemplate =
		"""
		Generate a single conventional commit message with a title line followed by a body that briefly summarizes the changes.

		FORMAT:
		<type>[optional scope]: <description>

		<body summarizing what changed and why>

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

		TITLE RULES:
		1. Use imperative mood: 'add' not 'adds' or 'added'
		2. No period at end
		3. Keep under 50 characters when possible
		4. Add scope when it clarifies context
		5. Use ! for breaking changes: feat!: or feat(api)!:

		BODY RULES:
		1. Separate title and body with exactly one blank line
		2. Wrap body lines at ~72 characters
		3. Briefly summarize the what and the why; do not restate the title
		4. Use bullet points (- prefix) only when listing distinct changes
		5. Keep the body to a short paragraph or a few bullets

		EXAMPLE:
		feat(auth): add OAuth2 integration

		Replace the password-only flow with Google OAuth2 sign-in. Adds a
		new /auth/oauth/google endpoint and a token-validation middleware
		so existing API routes can opt in without further changes.

		Git patch:
		```
		{0}
		```

		Return only the commit message text. Do not wrap it in quotation marks or code fences, and do not add any commentary before or after.
		""";

	private static string BuildUserPrompt(string patch, bool titlesOnly) =>
		string.Format(titlesOnly ? TitlesUserPromptTemplate : TitleAndBodyUserPromptTemplate, patch);

	private static List<string> ParseTitleSuggestions(string response)
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

	private static List<string> ParseTitleAndBody(string response)
	{
		string normalized = response.Replace("\r\n", "\n").Trim();

		return string.IsNullOrEmpty(normalized)
			? ["feat: implement changes"]
			: [normalized];
	}
}
