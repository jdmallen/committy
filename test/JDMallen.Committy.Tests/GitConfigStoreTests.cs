namespace JDMallen.Committy.Tests;

/// <summary>
/// <see cref="GitConfigStore.GetAsync" /> runs git in the process working
/// directory and inherits its environment, so these tests change both: the
/// directory to a temp repo, and <c>GIT_CONFIG_GLOBAL</c> to a temp file so
/// ~/.gitconfig is never read or written.
/// </summary>
[Collection("git-cwd")]
public class GitConfigStoreTests
{
	private const string KEY = "committy.testkey";

	private static async Task InRepoWithGlobalAsync(
		Func<string, string, Task> body)
	{
		string originalDirectory = Directory.GetCurrentDirectory();
		string? originalGlobal =
			Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
		string root = Directory.CreateTempSubdirectory("committy-store-")
			.FullName;
		string repo = Path.Combine(root, "repo");
		string globalConfig = Path.Combine(root, "global.gitconfig");
		await File.WriteAllTextAsync(globalConfig, string.Empty);

		try
		{
			Environment.SetEnvironmentVariable(
				"GIT_CONFIG_GLOBAL",
				globalConfig);
			await TestGit.RunAsync("init", "--quiet", repo);
			Directory.SetCurrentDirectory(repo);
			await body(repo, globalConfig);
		}
		finally
		{
			Directory.SetCurrentDirectory(originalDirectory);
			Environment.SetEnvironmentVariable(
				"GIT_CONFIG_GLOBAL",
				originalGlobal);

			try
			{
				Directory.Delete(root, true);
			}
			catch (Exception)
			{
				// Best effort cleanup.
			}
		}
	}

	[Fact]
	public async Task GetAsync_GlobalOnly_LocalScopeSeesNothing()
		=> await InRepoWithGlobalAsync(async (_, globalConfig) =>
		{
			await TestGit.RunAsync(
				"config",
				"--file",
				globalConfig,
				KEY,
				"from-global");

			Assert.Equal("from-global", await GitConfigStore.GetAsync(KEY));
			Assert.Equal(
				"from-global",
				await GitConfigStore.GetAsync(KEY, global: true));
			Assert.Null(await GitConfigStore.GetAsync(KEY, global: false));
		});

	[Fact]
	public async Task GetAsync_LocalOverride_GlobalScopeSeesGlobalValue()
		=> await InRepoWithGlobalAsync(async (repo, globalConfig) =>
		{
			await TestGit.RunAsync(
				"config",
				"--file",
				globalConfig,
				KEY,
				"from-global");
			await TestGit.RunAsync(
				"-C",
				repo,
				"config",
				"--local",
				KEY,
				"from-local");

			Assert.Equal("from-local", await GitConfigStore.GetAsync(KEY));
			Assert.Equal(
				"from-global",
				await GitConfigStore.GetAsync(KEY, global: true));
			Assert.Equal(
				"from-local",
				await GitConfigStore.GetAsync(KEY, global: false));
		});
}
