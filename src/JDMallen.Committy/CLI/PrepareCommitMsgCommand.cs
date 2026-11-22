using System.CommandLine;

namespace JDMallen.Committy;

/// <summary>
/// The git hook entry point. The installed trampoline invokes this with the
/// path to the commit message file. Reads config (provider + credentials) the
/// same way the rest of committy does, generates, and writes the file. Always
/// exits 0: a hook failure must not block a commit.
/// </summary>
public static class PrepareCommitMsgCommand
{
	public static Command Build()
	{
		var commitMsgFileArgument = new Argument<string>("commit-msg-file")
		{
			Description = "Path to the commit message file (provided by git)",
		};

		var command = new Command(
			"prepare-commit-msg",
			"Git hook entry point: writes suggestions into the given commit message file")
		{
			commitMsgFileArgument,
		};

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			await RunAsync(
				parseResult.GetValue(commitMsgFileArgument)!,
				HookSteps.Default,
				cancellationToken);

			return 0;
		});

		return command;
	}

	/// <summary>
	/// Writes suggestions into <paramref name="commitMsgFile" />, or comment
	/// lines saying why there are none. Never throws: every failure becomes a
	/// comment, so the commit goes ahead.
	/// </summary>
	public static async Task RunAsync(
		string commitMsgFile,
		HookSteps steps,
		CancellationToken cancellationToken = default)
	{
		try
		{
			await WriteSuggestionsAsync(
				commitMsgFile,
				steps,
				cancellationToken);
		}
		catch (Exception ex)
			when (ChatCompletionClientFactory.IsSignInRequired(ex))
		{
			await TryAppendLinesAsync(
				commitMsgFile,
				[
					"# Committy: not signed in to Azure (or the sign-in expired).",
					"# Run `committy config` to sign in; hooks never open a browser.",
				]);
		}
		catch (Exception ex)
		{
			// Messages can span lines (the Azure CLI's own error is quoted
			// on the next one); every line must stay a comment.
			await TryAppendLinesAsync(
				commitMsgFile,
				CommitMessageComposer.CommentLines(
					$"Committy: {ex.Message}"));
		}
	}

	private static async Task WriteSuggestionsAsync(
		string commitMsgFile,
		HookSteps steps,
		CancellationToken cancellationToken)
	{
		// Nothing staged → no-op, so an empty commit message stays clean.
		string? patch = await steps.GetStagedDiff(cancellationToken);

		if (string.IsNullOrWhiteSpace(patch))
		{
			return;
		}

		CommittyConfig config = await steps.ResolveConfig(cancellationToken);

		if (GenerateCommand.CheckReady(config) is { } error)
		{
			await AppendLinesAsync(
				commitMsgFile,
				[
					.. CommitMessageComposer.CommentLines(
						$"Committy: {error}"),
					"# Run `committy config` to set up a provider.",
				]);

			return;
		}

		CommitSuggestions suggestions = await steps.Generate(
			config,
			patch,
			cancellationToken);
		steps.RecordUsage(suggestions.Usage);

		if (suggestions.Messages.Count == 0)
		{
			// A comment, never a made-up message, so nothing gets committed
			// by accident.
			await AppendLinesAsync(
				commitMsgFile,
				CommitMessageComposer.CommentLines(
					$"Committy: {CommitMessageParser.NO_MESSAGE}"));

			return;
		}

		string existing = File.Exists(commitMsgFile)
			? await File.ReadAllTextAsync(commitMsgFile, cancellationToken)
			: string.Empty;

		string composed = CommitMessageComposer.Compose(
			suggestions.Messages,
			config.TitlesOnly,
			existing,
			suggestions.Usage);

		await File.WriteAllTextAsync(
			commitMsgFile,
			composed,
			cancellationToken);
	}

	private static async Task AppendLinesAsync(
		string file,
		IEnumerable<string> lines) =>
		await File.AppendAllTextAsync(
			file,
			string.Join('\n', lines) + "\n");

	/// <summary>
	/// Appends comment lines to the commit message file. Never throws: a hook
	/// failure must not abort the commit.
	/// </summary>
	private static async Task TryAppendLinesAsync(
		string file,
		IEnumerable<string> lines)
	{
		try
		{
			await AppendLinesAsync(file, lines);
		}
		catch
		{
			// Best effort only.
		}
	}
}

/// <summary>
/// The hook's outside steps, so tests can run
/// <see cref="PrepareCommitMsgCommand.RunAsync" /> without git, a provider,
/// or the user's token usage log.
/// </summary>
/// <param name="GetStagedDiff">The staged diff, or null when none.</param>
/// <param name="ResolveConfig">The committy config in effect.</param>
/// <param name="Generate">Asks the provider for suggestions.</param>
/// <param name="RecordUsage">Adds a run's tokens to the lifetime totals.</param>
public sealed record HookSteps(
	Func<CancellationToken, Task<string?>> GetStagedDiff,
	Func<CancellationToken, Task<CommittyConfig>> ResolveConfig,
	Func<CommittyConfig, string, CancellationToken, Task<CommitSuggestions>>
		Generate,
	Action<TokenUsage?> RecordUsage)
{
	/// <summary>
	/// The real steps. A hook never opens a browser from inside
	/// <c>git commit</c>, so interactive sign-in is off.
	/// </summary>
	public static HookSteps Default { get; } = new(
		GitService.TryGetStagedDiffAsync,
		cancellationToken => CommittyConfigResolver.ResolveAsync(
			null,
			cancellationToken),
		(config, patch, cancellationToken) => GenerateCommand.GenerateAsync(
			config,
			patch,
			config.TitlesOnly,
			false,
			cancellationToken),
		GenerateCommand.RecordUsage);
}
