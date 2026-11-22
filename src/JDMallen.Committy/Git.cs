using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace JDMallen.Committy;

/// <summary>
/// Runs git and captures its output. A non-zero exit code is returned, not
/// thrown, so callers decide what each exit code means. Output is decoded as
/// UTF-8, which is what git writes, rather than the console's code page.
/// </summary>
public static class Git
{
	/// <summary>
	/// What committy says when git rejects an option it relies on:
	/// <c>--show-scope</c> needs git 2.26 and <c>diff --no-relative</c> 2.28.
	/// </summary>
	public const string TOO_OLD_MESSAGE =
		"Your git is too old: committy needs git 2.28 or newer.";

	public static Task<ProcessTextOutput> RunAsync(
		IEnumerable<string> arguments,
		CancellationToken cancellationToken = default) =>
		RunAsync(arguments, null, cancellationToken);

	/// <param name="arguments">The git arguments.</param>
	/// <param name="environment">
	/// Extra environment variables for git, such as <c>GIT_CONFIG_GLOBAL</c>;
	/// null for none.
	/// </param>
	/// <param name="cancellationToken">
	/// Cancels the run and kills git.
	/// </param>
	public static async Task<ProcessTextOutput> RunAsync(
		IEnumerable<string> arguments,
		IReadOnlyDictionary<string, string>? environment,
		CancellationToken cancellationToken)
	{
		ProcessStartInfo startInfo = CaptureStartInfo("git", arguments);

		if (environment is not null)
		{
			foreach ((string name, string value) in environment)
			{
				startInfo.Environment[name] = value;
			}
		}

		try
		{
			return await Process
				.RunAndCaptureTextAsync(startInfo, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Win32Exception exception)
		{
			throw new InvalidOperationException(
				$"Could not run git ({exception.Message}). Is git installed and on PATH?",
				exception);
		}
	}

	/// <summary>
	/// Start info that captures stdout and stderr as UTF-8, as
	/// <see cref="Process.RunAndCaptureTextAsync(ProcessStartInfo, CancellationToken)" />
	/// requires.
	/// </summary>
	public static ProcessStartInfo CaptureStartInfo(
		string fileName,
		IEnumerable<string> arguments) =>
		new(fileName, arguments)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
		};
}
