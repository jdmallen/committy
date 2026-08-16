namespace Committy;

/// <summary>
/// Builds the provider-agnostic <see cref="CompletionRequest" /> for a commit
/// message
/// from the staged patch and the selected output mode. Owns the prompt templates
/// so
/// they are shared across every provider.
/// </summary>
public static class CommitMessagePrompt
{
	private const int TITLES_MAX_TOKENS = 100;
	private const int TITLE_AND_BODY_MAX_TOKENS = 500;

	private const string SYSTEM_PROMPT =
		"""
		You are a helpful assistant that generates conventional commit messages.You are a git and
		software engineering expert whose job it is to quickly investigate diffs for staged code just
		prior to a commit and make suggestions for a git commit message.
		""";

	private const string TITLES_USER_PROMPT_TEMPLATE =
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

	private const string TITLE_AND_BODY_USER_PROMPT_TEMPLATE =
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

	/// <param name="patch">The staged diff to summarize.</param>
	/// <param name="titlesOnly">Whether to ask for five titles or one title+body.</param>
	/// <param name="maxTokensOverride">
	/// Replaces the per-mode default budget. Reasoning models need it: their
	/// thinking block is spent from the same budget and then discarded, so the
	/// 100-token titles budget leaves nothing for an actual answer.
	/// </param>
	public static CompletionRequest Build(
		string patch,
		bool titlesOnly,
		int? maxTokensOverride = null) =>
		new(
			SYSTEM_PROMPT,
			string.Format(
				titlesOnly ? TITLES_USER_PROMPT_TEMPLATE : TITLE_AND_BODY_USER_PROMPT_TEMPLATE,
				patch),
			maxTokensOverride ?? (titlesOnly ? TITLES_MAX_TOKENS : TITLE_AND_BODY_MAX_TOKENS));
}
