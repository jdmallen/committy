namespace Committy.Tests;

public class CommittyServiceTests
{
	[Fact]
	public async Task CopyToClipboardAsync_NullText_DoesNotThrow()
	{
		await CommittyService.CopyToClipboardAsync(null!, CancellationToken.None);
	}

	[Fact]
	public async Task CopyToClipboardAsync_ValidText_DoesNotThrow()
	{
		const string text = "feat: add new feature";

		await CommittyService.CopyToClipboardAsync(text, CancellationToken.None);
	}

	[Fact]
	public void ReadPatchFromStdinAsync_NoInputRedirected_ThrowsInvalidOperationException()
	{
		// Note: assumes Console.IsInputRedirected is false in the test environment.
		Task<InvalidOperationException> exception =
			Assert.ThrowsAsync<InvalidOperationException>(() =>
				CommittyService.ReadPatchFromStdinAsync(CancellationToken.None));

		Assert.NotNull(exception);
	}
}
