using System.Diagnostics;

namespace JDMallen.Committy.Tests;

/// <summary>
/// Runs git to set up a test, failing the test when git fails, so a broken
/// fixture is reported where it breaks instead of as a confusing assertion
/// later.
/// </summary>
internal static class TestGit
{
	public static async Task RunAsync(params string[] arguments)
	{
		ProcessTextOutput result = await Git.RunAsync(arguments);

		if (result.ExitStatus.ExitCode != 0)
		{
			throw new InvalidOperationException(
				$"git {string.Join(' ', arguments)} exited {result.ExitStatus.ExitCode}: {result.StandardError.Trim()}");
		}
	}
}
