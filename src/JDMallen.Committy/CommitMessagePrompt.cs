using System.Globalization;
using System.Text;

namespace JDMallen.Committy;

/// <summary>
/// Builds the provider-agnostic <see cref="CompletionRequest" /> for a commit
/// message from the staged patch and the selected output mode. Owns the prompt
/// templates so they are shared across every provider. The patch is condensed
/// first (<see cref="PatchCondenser" />), and the prompt tells the model how
/// big the change is so a light model can size the body to match.
/// </summary>
public static class CommitMessagePrompt
{
	private const int TITLES_MAX_TOKENS = 100;
	private const int TITLE_AND_BODY_MAX_TOKENS = 500;
	private const int LARGE_TITLE_AND_BODY_MAX_TOKENS = 800;

	/// <summary>The FILES CHANGED list stops after this many entries.</summary>
	private const int MAX_LISTED_FILES = 150;

	private const string SYSTEM_PROMPT =
		"""
		You are a git and software engineering expert who writes conventional
		commit messages. You read the staged diff just before a commit and
		describe what it changes, accurately and specifically.
		""";

	private const string TYPES_AND_TITLE_RULES =
		"""
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
		""";

	private const string CONDENSED_PATCH_NOTE =
		"""
		ABOUT THE PATCH: lines starting with [committy] are notes, not code.
		Lock/generated files, repeats of a file already shown, and very long
		diffs may be summarized by such a note instead of shown in full. Those
		files still changed; FILES CHANGED lists every file.
		""";

	// {0} change summary, {1} file list, {2} types/title rules,
	// {3} condensed-patch note, {4} patch.
	private const string TITLES_USER_PROMPT_TEMPLATE =
		"""
		Generate exactly 5 different commit messages following Conventional Commits v1.0.0 specification.

		FORMAT: <type>[optional scope]: <description>

		{2}

		EXAMPLES:
		feat(auth): add OAuth2 integration
		fix(api): prevent memory leak in parser
		docs: update installation guide
		perf(db): optimize query performance
		feat!: remove deprecated login API

		{0}

		FILES CHANGED:
		{1}

		{3}

		Git patch:
		```
		{4}
		```

		Return exactly 5 commit messages, one per line, with no numbering, quotation marks, nor bullets. Imperative mood, under 50 characters when possible.
		""";

	// {0} change summary, {1} file list, {2} types/title rules,
	// {3} condensed-patch note, {4} patch, {5} size guidance.
	private const string TITLE_AND_BODY_USER_PROMPT_TEMPLATE =
		"""
		Generate a single conventional commit message: a title line, a blank line, then a body that describes the change.

		FORMAT:
		<type>[optional scope]: <description>

		<body>

		{2}

		BODY RULES:
		1. Separate title and body with exactly one blank line
		2. Wrap body lines at about 72 characters
		3. Say what changed and why; do not just restate the title
		4. Be specific. Name new or changed config keys and their allowed values, CLI flags and commands, files, public APIs, user-visible behaviour, and dependency or version changes. Never write a vague phrase like "update docs" or "expose settings in the CLI" without saying what.
		5. Size the body to the change (see CHANGE SIZE):
		   - SMALL: 1-3 sentences, no bullets
		   - MEDIUM: a short paragraph; if there are 3 or more distinct changes, follow it with up to 5 bullets
		   - LARGE: a 1-3 sentence summary paragraph, then 4-8 bullets, one per distinct area; optionally end with a short paragraph for dependency, version, or size notes
		6. Bullets start with "- "; wrapped bullet lines are indented two spaces
		7. Group repetitive edits (translations, similar tests, renames) into one bullet
		8. Only describe what the patch shows; do not invent behaviour

		EXAMPLE (SMALL change):
		fix(parser): return empty result for empty input

		Parse threw a NullReferenceException on an empty string. It now
		returns an empty result, the same as for whitespace-only input.

		EXAMPLE (LARGE change):
		feat(webhooks): retry failed deliveries with backoff

		Failed webhook deliveries are now retried instead of dropped, with
		exponential backoff, until webhooks.maxAttempts is reached.

		- New settings: webhooks.maxAttempts (default 5) and
		  webhooks.backoffSeconds (default 30, doubled per attempt).
		- Deliveries that run out of attempts move to a dead-letter table;
		  GET /admin/webhooks/dead-letters lists them and
		  POST /admin/webhooks/dead-letters/{{id}}/replay re-queues one.
		- The delivery worker records each attempt's status code and
		  latency in webhook_attempts.
		- Two new error messages, translated into all 12 languages.
		- Tests cover backoff timing, the attempt cap, and replay.

		Adds migrations for webhook_attempts and webhook_dead_letters.
		Requires Polly 8.5.

		{0}

		FILES CHANGED:
		{1}

		{3}

		Git patch:
		```
		{4}
		```

		REMINDER: {5} Title in imperative mood, under 50 characters when possible. Return only the commit message text: no quotation marks, no code fences, no commentary before or after.
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
		int? maxTokensOverride = null)
	{
		CondensedPatch condensed = PatchCondenser.Condense(patch);
		string changeSummary = DescribeChange(condensed);
		string fileList = ListFiles(condensed.Files);

		string userPrompt = titlesOnly
			? string.Format(
				CultureInfo.InvariantCulture,
				TITLES_USER_PROMPT_TEMPLATE,
				changeSummary,
				fileList,
				TYPES_AND_TITLE_RULES,
				CONDENSED_PATCH_NOTE,
				condensed.Text)
			: string.Format(
				CultureInfo.InvariantCulture,
				TITLE_AND_BODY_USER_PROMPT_TEMPLATE,
				changeSummary,
				fileList,
				TYPES_AND_TITLE_RULES,
				CONDENSED_PATCH_NOTE,
				condensed.Text,
				SizeGuidance(condensed.Size));

		int defaultMaxTokens = titlesOnly
			? TITLES_MAX_TOKENS
			: condensed.Size == ChangeSize.Large
				? LARGE_TITLE_AND_BODY_MAX_TOKENS
				: TITLE_AND_BODY_MAX_TOKENS;

		return new CompletionRequest(
			SYSTEM_PROMPT,
			userPrompt,
			maxTokensOverride ?? defaultMaxTokens);
	}

	/// <summary>The one-line CHANGE SIZE summary.</summary>
	public static string DescribeChange(CondensedPatch condensed)
	{
		string size = condensed.Size.ToString().ToUpperInvariant();
		int fileCount = condensed.Files.Count;

		if (fileCount == 0)
		{
			return $"CHANGE SIZE: {size}";
		}

		string summary = string.Create(
			CultureInfo.InvariantCulture,
			$"CHANGE SIZE: {size} ({Plural(fileCount, "file")}, +{condensed.TotalAdded}/-{condensed.TotalRemoved} lines");

		return condensed.MeaningfulFiles != fileCount
			? string.Create(
				CultureInfo.InvariantCulture,
				$"{summary}; {Plural(condensed.MeaningfulFiles, "file")} and {Plural(condensed.MeaningfulLines, "line")} after setting aside lock/generated files and repeats)")
			: summary + ")";
	}

	/// <summary>The FILES CHANGED list, one file per line.</summary>
	public static string ListFiles(IReadOnlyList<FileChange> files)
	{
		if (files.Count == 0)
		{
			return "(could not parse the file list; see the patch)";
		}

		var builder = new StringBuilder();

		foreach (FileChange file in files.Take(MAX_LISTED_FILES))
		{
			builder.Append(
				CultureInfo.InvariantCulture,
				$"{file.Status} {file.Path} ");
			builder.Append(
				file.IsBinary && file.Added + file.Removed == 0
					? "(binary"
					: string.Create(
						CultureInfo.InvariantCulture,
						$"(+{file.Added}/-{file.Removed}"));
			builder.Append(
				file.Condensing switch
				{
					FileCondensing.Generated when !file.IsBinary =>
						", lock/generated file, diff omitted",
					FileCondensing.Generated => ", diff omitted",
					FileCondensing.Repeated =>
						$", same change as {FileNameOf(file.RepeatOf)}, diff omitted",
					FileCondensing.Truncated => ", diff truncated",
					FileCondensing.Omitted => ", diff omitted for length",
					_ => string.Empty,
				});
			builder.Append(")\n");
		}

		if (files.Count > MAX_LISTED_FILES)
		{
			builder.Append(
				CultureInfo.InvariantCulture,
				$"... and {files.Count - MAX_LISTED_FILES} more files\n");
		}

		return builder.ToString().TrimEnd('\n');
	}

	private static string SizeGuidance(ChangeSize size) =>
		size switch
		{
			ChangeSize.Small =>
				"This is a SMALL change: write a body of 1-3 sentences, no bullets.",
			ChangeSize.Medium =>
				"This is a MEDIUM change: write a short paragraph, plus up to 5 bullets if there are 3 or more distinct changes.",
			_ =>
				"This is a LARGE change: write a 1-3 sentence summary paragraph, then 4-8 specific bullets, one per distinct area, and optionally a short closing paragraph for dependency, version, or size notes.",
		};

	// Repeats share a directory with the file they repeat, so the name is enough.
	private static string FileNameOf(string? path) =>
		path is null ? string.Empty : path[(path.LastIndexOf('/') + 1)..];

	private static string Plural(int count, string noun) =>
		string.Create(
			CultureInfo.InvariantCulture,
			$"{count} {noun}{(count == 1 ? string.Empty : "s")}");
}
