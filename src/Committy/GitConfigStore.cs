using CliWrap;
using CliWrap.Buffered;

namespace Committy;

/// <summary>
/// Reads and writes committy settings through <c>git config</c>. Reading uses
/// <c>--get</c>, which (when run inside a repository) transparently layers local
/// over
/// global over system scope, so a per-repo override just works at commit time.
/// Writing
/// targets either <c>--global</c> or <c>--local</c>.
/// </summary>
public sealed class GitConfigStore
{
	public static async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
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

	public async Task SetAsync(
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
}
