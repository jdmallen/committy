namespace JDMallen.Committy.Tests;

public class GenerateCommandTests
{
	[Fact]
	public async Task CopyToClipboardAsync_NullText_DoesNotThrow()
		=> await GenerateCommand.CopyToClipboardAsync(
			null!,
			CancellationToken.None);

	[Fact]
	public async Task CopyToClipboardAsync_ValidText_DoesNotThrow()
	{
		const string text = "feat: add new feature";

		await GenerateCommand.CopyToClipboardAsync(
			text,
			CancellationToken.None);
	}

	[Fact]
	public void
		ReadPatchFromStdinAsync_NoInputRedirected_ThrowsInvalidOperationException()
	{
		// Note: assumes Console.IsInputRedirected is false in the test environment.
		Task<InvalidOperationException> exception =
			Assert.ThrowsAsync<InvalidOperationException>(() =>
				GenerateCommand.ReadPatchFromStdinAsync(
					CancellationToken.None));

		Assert.NotNull(exception);
	}
}
