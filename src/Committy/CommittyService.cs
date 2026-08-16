using TextCopy;

namespace Committy;

/// <summary>
/// Process-level helpers shared by the CLI: reading the patch from stdin and
/// copying a
/// suggestion to the clipboard. Commit message generation lives in
/// <see cref="CommitMessageGenerator" />.
/// </summary>
public static class CommittyService
{
	private static bool _clipboardWarningShown;

	public static async Task CopyToClipboardAsync(
		string text,
		CancellationToken cancellationToken = default)
	{
		try
		{
			await ClipboardService.SetTextAsync(text, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception)
		{
			// Don't fail the entire operation if clipboard fails
			// Only show warning once to avoid spam
			if (!_clipboardWarningShown)
			{
				await Console.Error.WriteLineAsync(
						"Warning: Clipboard functionality not available on this system. If running Linux, try installing `xsel` package.")
					.ConfigureAwait(false);
				_clipboardWarningShown = true;
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

		using var reader = new StreamReader(Console.OpenStandardInput(), Console.InputEncoding);

		return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

	}
}
