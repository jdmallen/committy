namespace JDMallen.Committy;

/// <summary>
/// Runs a command's body and turns how it ended into the process exit code,
/// so commands return their code instead of calling
/// <see cref="Environment.Exit(int)" /> mid-way and skipping cleanup.
/// </summary>
public static class CommandErrors
{
	/// <summary>The usual exit code for a run stopped by Ctrl+C.</summary>
	private const int CANCELED_EXIT_CODE = 130;

	/// <summary>
	/// Returns the body's exit code; <see cref="CANCELED_EXIT_CODE" /> when
	/// <paramref name="cancellationToken" /> was cancelled; and 1, after
	/// printing "Error: ..." to stderr, when it threw anything else. An
	/// <see cref="OperationCanceledException" /> the user did not cause (an
	/// HTTP timeout, for one) counts as an error, not a cancellation.
	/// </summary>
	public static async Task<int> HandleAsync(
		Func<Task<int>> body,
		CancellationToken cancellationToken)
	{
		try
		{
			return await body();
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
		{
			return CANCELED_EXIT_CODE;
		}
		catch (Exception ex)
		{
			await Console.Error.WriteLineAsync($"Error: {ex.Message}");

			return 1;
		}
	}
}
