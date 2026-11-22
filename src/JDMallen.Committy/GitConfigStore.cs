using System.Diagnostics;

namespace JDMallen.Committy;

/// <summary>
/// Reads and writes committy settings through <c>git config</c>. Reading
/// (<see cref="ReadAllAsync" /> for every setting at once, or
/// <see cref="GetAsync" /> for one) sees local over global over system scope
/// when run inside a repository, so a per-repo override just works at commit
/// time. Writing targets either <c>--global</c> or <c>--local</c>.
/// </summary>
public static class GitConfigStore
{
	public static async Task<string?> GetAsync(
		string key,
		CancellationToken cancellationToken = default)
	{
		ProcessTextOutput result = await Git.RunAsync(
				["config", "--get", key],
				cancellationToken)
			.ConfigureAwait(false);

		if (result.ExitStatus.ExitCode != 0)
		{
			return null;
		}

		string value = result.StandardOutput.Trim();

		return string.IsNullOrEmpty(value) ? null : value;
	}

	/// <summary>
	/// Every <c>committy.*</c> setting git can see from here, keyed by name
	/// (case-insensitive), with the scope each value came from. One process
	/// instead of one per key. When a key is set in more than one scope, the
	/// value that <c>git config --get</c> would return wins.
	/// </summary>
	public static async Task<IReadOnlyDictionary<string, GitConfigValue>>
		ReadAllAsync(CancellationToken cancellationToken = default)
	{
		ProcessTextOutput result = await Git.RunAsync(
				[
					"config", "--show-scope", "--null", "--get-regexp",
					@"^committy\.",
				],
				cancellationToken)
			.ConfigureAwait(false);

		// Exit code 1 means no committy keys are set. Anything else (an
		// unreadable config file, or a git too old for --show-scope) is
		// reported, rather than read as "nothing set" and blamed on a missing
		// setting.
		return result.ExitStatus.ExitCode switch
		{
			0 => ParseAll(result.StandardOutput),
			1 => new Dictionary<string, GitConfigValue>(
				StringComparer.OrdinalIgnoreCase),
			_ => throw new InvalidOperationException(
				result.StandardError.Contains(
					"show-scope",
					StringComparison.Ordinal)
					? Git.TOO_OLD_MESSAGE
					: $"Could not read git config: {result.StandardError.Trim()}"),
		};
	}

	/// <summary>
	/// Parses <c>git config --show-scope --null --get-regexp</c> output: a
	/// repeating "scope\0key\nvalue\0", where the value may itself contain
	/// newlines and a key set with no <c>=</c> has no "\nvalue" at all. git
	/// lists scopes from system to command, so a later entry wins.
	/// </summary>
	public static IReadOnlyDictionary<string, GitConfigValue> ParseAll(
		string output)
	{
		var values = new Dictionary<string, GitConfigValue>(
			StringComparer.OrdinalIgnoreCase);
		string[] fields = output.Split('\0');

		for (int index = 0; index + 1 < fields.Length; index += 2)
		{
			string scope = fields[index];
			string entry = fields[index + 1];
			int newline = entry.IndexOf('\n');
			string key = newline < 0 ? entry : entry[..newline];
			string value = newline < 0 ? string.Empty : entry[(newline + 1)..];
			values[key] = new GitConfigValue(value, scope);
		}

		return values;
	}

	public static async Task SetAsync(
		string key,
		string value,
		bool global,
		CancellationToken cancellationToken = default)
	{
		string scope = global ? "--global" : "--local";

		ProcessTextOutput result = await Git.RunAsync(
				["config", scope, key, value],
				cancellationToken)
			.ConfigureAwait(false);

		if (result.ExitStatus.ExitCode != 0)
		{
			throw new InvalidOperationException(
				$"`git config {scope} {key}` failed: {result.StandardError.Trim()}");
		}
	}

	/// <summary>
	/// Removes <paramref name="key" /> from the global or local config. A key
	/// that is not set there is not an error.
	/// </summary>
	public static async Task UnsetAsync(
		string key,
		bool global,
		CancellationToken cancellationToken = default)
	{
		// git config exits 5 when there is nothing to unset.
		const int notSetExitCode = 5;
		string scope = global ? "--global" : "--local";

		ProcessTextOutput result = await Git.RunAsync(
				["config", scope, "--unset", key],
				cancellationToken)
			.ConfigureAwait(false);

		if (result.ExitStatus.ExitCode is not 0 and not notSetExitCode)
		{
			throw new InvalidOperationException(
				$"`git config {scope} --unset {key}` failed: {result.StandardError.Trim()}");
		}
	}
}

/// <summary>A git config value and the scope it was read from.</summary>
/// <param name="Value">The value.</param>
/// <param name="Scope">
/// "local", "global", "system", "worktree" or "command", or null when the
/// scope is unknown.
/// </param>
public sealed record GitConfigValue(string Value, string? Scope);
