namespace JDMallen.Committy.Tests;

/// <summary>
/// Redirects <see cref="Console.Error" />, which is process-wide, so these
/// tests must not run beside anything else that writes to it.
/// </summary>
[CollectionDefinition("console-error", DisableParallelization = true)]
public class ConsoleErrorCollection;

[Collection("console-error")]
public class CommandErrorsTests
{
	[Fact]
	public async Task HandleAsync_BodySucceeds_ReturnsItsExitCode()
	{
		(int exitCode, string stderr) = await RunAsync(
			() => Task.FromResult(3),
			CancellationToken.None);

		Assert.Equal(3, exitCode);
		Assert.Empty(stderr);
	}

	[Fact]
	public async Task HandleAsync_UserCancelled_Returns130()
	{
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();

		(int exitCode, string stderr) = await RunAsync(
			() => throw new OperationCanceledException(cancellation.Token),
			cancellation.Token);

		Assert.Equal(130, exitCode);
		Assert.Empty(stderr);
	}

	[Fact]
	public async Task HandleAsync_CancellationUserDidNotCause_ReportsError()
	{
		(int exitCode, string stderr) = await RunAsync(
			() => throw new TaskCanceledException("timed out"),
			CancellationToken.None);

		Assert.Equal(1, exitCode);
		Assert.Equal("Error: timed out", stderr.TrimEnd());
	}

	[Fact]
	public async Task HandleAsync_BodyThrows_ReportsErrorAndReturns1()
	{
		(int exitCode, string stderr) = await RunAsync(
			() => throw new InvalidOperationException("boom"),
			CancellationToken.None);

		Assert.Equal(1, exitCode);
		Assert.Equal("Error: boom", stderr.TrimEnd());
	}

	private static async Task<(int ExitCode, string Stderr)> RunAsync(
		Func<Task<int>> body,
		CancellationToken cancellationToken)
	{
		TextWriter original = Console.Error;
		var stderr = new StringWriter();
		Console.SetError(stderr);
		try
		{
			int exitCode =
				await CommandErrors.HandleAsync(body, cancellationToken);

			return (exitCode, stderr.ToString());
		}
		finally
		{
			Console.SetError(original);
		}
	}
}
