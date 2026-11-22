using System.Text;

using TextCopy;

namespace JDMallen.Committy;

/// <summary>
/// Process-level helpers shared by the CLI: reading the patch from stdin and
/// copying a
/// suggestion to the clipboard. Commit message generation lives in
/// <see cref="CommitMessageGenerator" />.
/// </summary>
public static class CommittyService
{
	private static bool s_clipboardWarningShown;

	public static async Task CopyToClipboardAsync(
		string text,
		CancellationToken cancellationToken = default)
	{
		try
		{
			await ClipboardService.SetTextAsync(text, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Exception)
		{
			// Don't fail the entire operation if clipboard fails
			// Only show warning once to avoid spam
			if (!s_clipboardWarningShown)
			{
				await Console.Error.WriteLineAsync(
						"Warning: Clipboard functionality not available on this system. If running Linux, try installing `xsel` package.",
						cancellationToken)
					.ConfigureAwait(false);
				s_clipboardWarningShown = true;
			}
		}
	}

	public static async Task<string> ReadPatchFromStdinAsync(
		CancellationToken cancellationToken = default)
	{
		if (!Console.IsInputRedirected)
		{
			throw new InvalidOperationException(
				"No input data available. Please pipe git patch data to stdin.");
		}

		// A piped patch comes from git, which writes UTF-8; the console's code
		// page (often not UTF-8 on Windows) would garble non-ASCII text. A
		// UTF-16 byte order mark, as Windows PowerShell can send, still wins.
		using var reader = new StreamReader(
			Console.OpenStandardInput(),
			Encoding.UTF8);

		return await reader.ReadToEndAsync(cancellationToken)
			.ConfigureAwait(false);
	}
}
