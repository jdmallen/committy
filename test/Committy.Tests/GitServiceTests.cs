using CliWrap;
using CliWrap.Buffered;

namespace Committy.Tests;

// GitService runs `git diff --cached` in the process working directory, so these tests
// change it. Isolate the class in a non-parallel collection so that mutation can't race
// with other tests.
[CollectionDefinition("git-cwd", DisableParallelization = true)]
public class GitCwdCollection;

[Collection("git-cwd")]
public class GitServiceTests
{
	/// <summary>
	/// Creates a throwaway git repository, runs <paramref name="body" /> with the
	/// process
	/// working directory set to it, then restores the directory and cleans up.
	/// </summary>
	private static async Task InRepoAsync(Func<string, Task> body)
	{
		string original = Directory.GetCurrentDirectory();
		string repo = Path.Combine(Path.GetTempPath(), "committy-git-" + Path.GetRandomFileName());
		Directory.CreateDirectory(repo);

		try
		{
			// Empty template so a globally-configured hook template doesn't add noise.
			string emptyTemplate = Path.Combine(repo, ".empty-template");
			Directory.CreateDirectory(emptyTemplate);
			await Cli.Wrap("git")
				.WithArguments(["-C", repo, "init", $"--template={emptyTemplate}"])
				.ExecuteBufferedAsync();

			Directory.SetCurrentDirectory(repo);
			await body(repo);
		}
		finally
		{
			Directory.SetCurrentDirectory(original);

			try
			{
				Directory.Delete(repo, true);
			}
			catch (Exception)
			{
				// Best effort cleanup.
			}
		}
	}

	private static async Task StageFileAsync(string repo, string name, string content)
	{
		await File.WriteAllTextAsync(Path.Combine(repo, name), content);
		await Cli.Wrap("git").WithArguments(["-C", repo, "add", name]).ExecuteBufferedAsync();
	}

	/// <summary>
	/// Runs <paramref name="body" /> with the process working directory set to a fresh
	/// temp directory that is not a git repository, then restores it and cleans up.
	/// </summary>
	private static async Task OutsideRepoAsync(Func<Task> body)
	{
		string original = Directory.GetCurrentDirectory();
		string dir = Path.Combine(Path.GetTempPath(), "committy-nogit-" + Path.GetRandomFileName());
		Directory.CreateDirectory(dir);

		try
		{
			Directory.SetCurrentDirectory(dir);
			await body();
		}
		finally
		{
			Directory.SetCurrentDirectory(original);

			try
			{
				Directory.Delete(dir, true);
			}
			catch (Exception)
			{
				// Best effort cleanup.
			}
		}
	}

	[Fact]
	public async Task TryGetStagedDiffAsync_OutsideRepository_ThrowsInvalidOperationException()
	{
		await OutsideRepoAsync(async () =>
		{
			var exception = await Assert.ThrowsAsync<InvalidOperationException>(()
				=> GitService.TryGetStagedDiffAsync(CancellationToken.None));

			Assert.Contains("Not a git repository", exception.Message);
		});
	}

	[Fact]
	public async Task GetStagedDiffAsync_NothingStaged_ThrowsInvalidOperationException()
	{
		await InRepoAsync(async _ =>
		{
			var exception = await Assert.ThrowsAsync<InvalidOperationException>(()
				=> GitService.GetStagedDiffAsync(CancellationToken.None));

			Assert.Contains("No staged changes found", exception.Message);
		});
	}

	[Fact]
	public async Task GetStagedDiffAsync_WithStagedChange_ReturnsDiff()
	{
		await InRepoAsync(async repo =>
		{
			await StageFileAsync(repo, "file.txt", "content\n");

			string diff = await GitService.GetStagedDiffAsync(CancellationToken.None);

			Assert.Contains("diff --git", diff);
		});
	}

	[Fact]
	public async Task TryGetStagedDiffAsync_NothingStaged_ReturnsNull()
	{
		await InRepoAsync(async _ =>
		{
			string? diff = await GitService.TryGetStagedDiffAsync(CancellationToken.None);

			Assert.Null(diff);
		});
	}

	[Fact]
	public async Task TryGetStagedDiffAsync_WithStagedChange_ReturnsDiff()
	{
		await InRepoAsync(async repo =>
		{
			await StageFileAsync(repo, "file.txt", "hello world\n");

			string? diff = await GitService.TryGetStagedDiffAsync(CancellationToken.None);

			Assert.NotNull(diff);
			Assert.Contains("diff --git", diff);
			Assert.Contains("hello world", diff);
		});
	}
}
