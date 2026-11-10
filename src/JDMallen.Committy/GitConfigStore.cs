using CliWrap;
using CliWrap.Buffered;

namespace JDMallen.Committy;

/// <summary>
/// Reads and writes committy settings through <c>git config</c>. Reading uses
/// <c>--get</c>, which (when run inside a repository) transparently layers local
/// over
/// global over system scope, so a per-repo override just works at commit time.
/// Writing
/// targets either <c>--global</c> or <c>--local</c>.
/// </summary>
public static class GitConfigStore
{
	public static async Task<string?> GetAsync(
		string key,
		CancellationToken cancellationToken = default)
	{
		BufferedCommandResult result = await Cli.Wrap("git")
			.WithArguments(["config", "--get", key])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync(cancellationToken)
			.ConfigureAwait(false);

		if (result.ExitCode != 0)
		{
			return null;
		}

		string value = result.StandardOutput.Trim();

		return string.IsNullOrEmpty(value) ? null : value;
	}

	/// <summary>
	/// As <see cref="GetAsync" />, but also says which scope (local, global,
	/// system, worktree or command) the winning value came from.
	/// </summary>
	public static async Task<GitConfigValue?> GetWithScopeAsync(
		string key,
		CancellationToken cancellationToken = default)
	{
		BufferedCommandResult result = await Cli.Wrap("git")
			.WithArguments(["config", "--show-scope", "--get", key])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync(cancellationToken)
			.ConfigureAwait(false);

		return result.ExitCode == 0
			? ParseScopedValue(result.StandardOutput)
			: null;
	}

	/// <summary>Parses <c>--show-scope</c> output: "scope\tvalue".</summary>
	public static GitConfigValue? ParseScopedValue(string output)
	{
		string line = output.TrimEnd('\r', '\n');
		int tab = line.IndexOf('\t');
		string? scope = tab < 0 ? null : line[..tab];
		string value = (tab < 0 ? line : line[(tab + 1)..]).Trim();

		return value.Length == 0 ? null : new GitConfigValue(value, scope);
	}

	public static async Task SetAsync(
		string key,
		string value,
		bool global,
		CancellationToken cancellationToken = default)
	{
		string scope = global ? "--global" : "--local";

		BufferedCommandResult result = await Cli.Wrap("git")
			.WithArguments(["config", scope, key, value])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync(cancellationToken)
			.ConfigureAwait(false);

		if (result.ExitCode != 0)
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
		const int NOT_SET_EXIT_CODE = 5;
		string scope = global ? "--global" : "--local";

		BufferedCommandResult result = await Cli.Wrap("git")
			.WithArguments(["config", scope, "--unset", key])
			.WithValidation(CommandResultValidation.None)
			.ExecuteBufferedAsync(cancellationToken)
			.ConfigureAwait(false);

		if (result.ExitCode is not 0 and not NOT_SET_EXIT_CODE)
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
