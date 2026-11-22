namespace JDMallen.Committy.Tests;

public class HookRepairerTests
{
	private const string OLD_HOOK = LegacyHooks.UNSTAMPED_TRAMPOLINE;

	private const string FOREIGN_HOOK =
		"#!/bin/sh\n# my own hook\necho \"running my hook\"\n";

	[Theory]
	[InlineData(OLD_HOOK, true)]
	[InlineData(FOREIGN_HOOK, false)]
	public void IsCommittyManaged_DetectsCommittyHooks(
		string content,
		bool expected) => Assert.Equal(
		expected,
		HookInstaller.IsCommittyManaged(content));

	private static async Task<string> NewRepoAsync()
	{
		string dir = Path.Combine(
			Path.GetTempPath(),
			"committy-repair-" + Path.GetRandomFileName());
		Directory.CreateDirectory(dir);

		// Init with an empty template so a globally-configured hook template doesn't
		// pre-populate prepare-commit-msg; each test controls the hook state itself.
		string emptyTemplate = Path.Combine(dir, ".empty-template");
		Directory.CreateDirectory(emptyTemplate);
		await TestGit.RunAsync(
			"-C",
			dir,
			"init",
			$"--template={emptyTemplate}");

		return dir;
	}

	/// <summary>
	/// Creates a "headless" repository — a separate git directory with no in-tree
	/// <c>.git</c> entry, mirroring a ~/.cfg dotfiles setup — and returns that git dir.
	/// </summary>
	private static async Task<string> NewHeadlessGitDirAsync()
	{
		string root = Path.Combine(
			Path.GetTempPath(),
			"committy-headless-" + Path.GetRandomFileName());
		string workTree = Path.Combine(root, "work");
		string gitDir = Path.Combine(root, "gitdir");
		Directory.CreateDirectory(workTree);

		string emptyTemplate = Path.Combine(root, ".empty-template");
		Directory.CreateDirectory(emptyTemplate);
		await TestGit.RunAsync(
			"-C",
			workTree,
			"init",
			$"--separate-git-dir={gitDir}",
			$"--template={emptyTemplate}");

		return gitDir;
	}

	private static string WriteHook(string repo, string content)
	{
		string hookPath = Path.Combine(
			repo,
			".git",
			"hooks",
			"prepare-commit-msg");
		Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);
		File.WriteAllText(hookPath, content);

		return hookPath;
	}

	[Fact]
	public void IsCommittyManaged_DetectsCurrentTrampoline()
	{
		Assert.True(HookInstaller.IsCommittyManaged(HookInstaller.Trampoline));
		Assert.True(
			HookInstaller.IsCurrentTrampoline(HookInstaller.Trampoline));
	}

	[Fact]
	public async Task RepairRepoAsync_AlreadyTrampoline_ReportsAlreadyCurrent()
	{
		string repo = await NewRepoAsync();
		try
		{
			WriteHook(repo, HookInstaller.Trampoline);

			HookRepairResult result =
				await HookRepairer.RepairRepoAsync(repo, false);

			Assert.Equal(HookRepairOutcome.AlreadyCurrent, result.Outcome);
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	[Fact]
	public async Task RepairRepoAsync_Backup_PreservesPreviousHook()
	{
		string repo = await NewRepoAsync();
		try
		{
			string hookPath = WriteHook(repo, OLD_HOOK);

			await HookRepairer.RepairRepoAsync(repo, false);

			Assert.Equal(
				OLD_HOOK,
				await File.ReadAllTextAsync(hookPath + ".bak"));
			Assert.Equal(
				HookInstaller.Trampoline.Trim(),
				(await File.ReadAllTextAsync(hookPath)).Trim());
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	[Fact]
	public async Task RepairRepoAsync_DryRun_DoesNotModifyStaleHook()
	{
		string repo = await NewRepoAsync();
		try
		{
			string hookPath = WriteHook(repo, OLD_HOOK);

			HookRepairResult result =
				await HookRepairer.RepairRepoAsync(repo, true);

			Assert.Equal(HookRepairOutcome.Updated, result.Outcome);
			Assert.Equal(OLD_HOOK, await File.ReadAllTextAsync(hookPath));
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	private static async Task<string> WriteCustomHooksPathHookAsync(
		string repo,
		string content)
	{
		await TestGit.RunAsync(
			"-C",
			repo,
			"config",
			"core.hooksPath",
			".githooks");
		string hookPath = Path.Combine(
			repo,
			".githooks",
			"prepare-commit-msg");
		Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);
		await File.WriteAllTextAsync(hookPath, content);

		return hookPath;
	}

	[Fact]
	public async Task RepairRepoAsync_StaleHookInCustomHooksPath_IsRepaired()
	{
		string repo = await NewRepoAsync();
		try
		{
			string hookPath =
				await WriteCustomHooksPathHookAsync(repo, OLD_HOOK);

			HookRepairResult result =
				await HookRepairer.RepairRepoAsync(repo, false);

			Assert.Equal(HookRepairOutcome.Updated, result.Outcome);
			Assert.Contains("custom core.hooksPath", result.Detail);
			Assert.Equal(
				HookInstaller.Trampoline.Trim(),
				(await File.ReadAllTextAsync(hookPath)).Trim());
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	[Fact]
	public async Task RepairRepoAsync_ForeignHookInCustomHooksPath_IsUntouched()
	{
		string repo = await NewRepoAsync();
		try
		{
			string hookPath =
				await WriteCustomHooksPathHookAsync(repo, FOREIGN_HOOK);

			HookRepairResult result =
				await HookRepairer.RepairRepoAsync(repo, false);

			Assert.Equal(HookRepairOutcome.SkippedForeign, result.Outcome);
			Assert.Contains("custom core.hooksPath", result.Detail);
			Assert.Equal(FOREIGN_HOOK, await File.ReadAllTextAsync(hookPath));
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	[Fact]
	public async Task RepairRepoAsync_ForeignHook_IsLeftUntouched()
	{
		string repo = await NewRepoAsync();
		try
		{
			string hookPath = WriteHook(repo, FOREIGN_HOOK);

			HookRepairResult result =
				await HookRepairer.RepairRepoAsync(repo, false);

			Assert.Equal(HookRepairOutcome.SkippedForeign, result.Outcome);
			Assert.Equal(FOREIGN_HOOK, await File.ReadAllTextAsync(hookPath));
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	[Fact]
	public async Task RepairRepoAsync_NoHook_ReportsNoHook()
	{
		string repo = await NewRepoAsync();
		try
		{
			HookRepairResult result =
				await HookRepairer.RepairRepoAsync(repo, false);

			Assert.Equal(HookRepairOutcome.NoHook, result.Outcome);
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	[Fact]
	public async Task RepairRepoAsync_HeadlessGitDir_StaleHookIsReplaced()
	{
		string gitDir = await NewHeadlessGitDirAsync();
		try
		{
			string hookPath = Path.Combine(
				gitDir,
				"hooks",
				"prepare-commit-msg");
			Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);
			await File.WriteAllTextAsync(hookPath, OLD_HOOK);

			HookRepairResult result =
				await HookRepairer.RepairRepoAsync(gitDir, false);

			Assert.Equal(HookRepairOutcome.Updated, result.Outcome);
			Assert.Equal(
				HookInstaller.Trampoline.Trim(),
				(await File.ReadAllTextAsync(hookPath)).Trim());
		}
		finally
		{
			Directory.Delete(Path.GetDirectoryName(gitDir)!, true);
		}
	}

	[Fact]
	public async Task RunAsync_GitDirTarget_RepairsHeadlessRepo()
	{
		string gitDir = await NewHeadlessGitDirAsync();
		try
		{
			string hookPath = Path.Combine(
				gitDir,
				"hooks",
				"prepare-commit-msg");
			Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);
			await File.WriteAllTextAsync(hookPath, OLD_HOOK);
			var output = new StringWriter();

			int code = await HookRepairer.RunAsync(
				[],
				[gitDir],
				false,
				true,
				output);

			Assert.Equal(0, code);
			Assert.Equal(
				HookInstaller.Trampoline.Trim(),
				(await File.ReadAllTextAsync(hookPath)).Trim());
		}
		finally
		{
			Directory.Delete(Path.GetDirectoryName(gitDir)!, true);
		}
	}

	[UnreadableFileFact]
	[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
	public async Task RunAsync_Scan_UnreadableHook_ReportsAndContinues()
	{
		string root = Directory.CreateTempSubdirectory("committy-scan-")
			.FullName;
		string? unreadableHook = null;
		try
		{
			string brokenRepo = Path.Combine(root, "a-broken");
			string staleRepo = Path.Combine(root, "b-stale");
			foreach (string repo in new[] { brokenRepo, staleRepo })
			{
				string emptyTemplate = Path.Combine(root, ".empty-template");
				Directory.CreateDirectory(emptyTemplate);
				await TestGit.RunAsync(
					"init",
					$"--template={emptyTemplate}",
					repo);
			}

			unreadableHook = WriteHook(brokenRepo, OLD_HOOK);
			File.SetUnixFileMode(unreadableHook, UnixFileMode.None);
			string staleHook = WriteHook(staleRepo, OLD_HOOK);
			var output = new StringWriter();

			int code = await HookRepairer.RunAsync(
				[root],
				[],
				false,
				false,
				output);

			Assert.Equal(1, code);
			Assert.Contains($"! error: {brokenRepo}", output.ToString());
			Assert.Contains($"updated: {staleRepo}", output.ToString());
			Assert.Equal(
				HookInstaller.Trampoline.Trim(),
				(await File.ReadAllTextAsync(staleHook)).Trim());
		}
		finally
		{
			if (unreadableHook is not null)
			{
				File.SetUnixFileMode(
					unreadableHook,
					UnixFileMode.UserRead | UnixFileMode.UserWrite);
			}

			Directory.Delete(root, true);
		}
	}

	[Fact]
	public async Task RunAsync_Scan_DoesNotFollowDirectorySymlinks()
	{
		string root = Directory.CreateTempSubdirectory("committy-scan-")
			.FullName;
		try
		{
			string repo = Path.Combine(root, "repo");
			string emptyTemplate = Path.Combine(root, ".empty-template");
			Directory.CreateDirectory(emptyTemplate);
			await TestGit.RunAsync(
				"init",
				$"--template={emptyTemplate}",
				repo);

			// A link back up the tree: following it would find the same
			// repository again at every level until the path got too long.
			Directory.CreateSymbolicLink(Path.Combine(root, "loop"), root);
			var output = new StringWriter();

			int code = await HookRepairer.RunAsync(
				[root],
				[],
				true,
				true,
				output);

			Assert.Equal(0, code);
			Assert.Contains("no hook: 1,", output.ToString());
		}
		finally
		{
			File.Delete(Path.Combine(root, "loop"));
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public async Task RunAsync_NonGitDirTarget_IsSkippedWithMessage()
	{
		string dir = Path.Combine(
			Path.GetTempPath(),
			"committy-notrepo-" + Path.GetRandomFileName());
		Directory.CreateDirectory(dir);
		try
		{
			var output = new StringWriter();

			int code = await HookRepairer.RunAsync(
				[],
				[dir],
				false,
				true,
				output);

			Assert.Equal(0, code);
			Assert.Contains("not a git directory", output.ToString());
		}
		finally
		{
			Directory.Delete(dir, true);
		}
	}

	[Fact]
	public async Task
		RepairRepoAsync_StaleCommittyHook_IsReplacedWithTrampoline()
	{
		string repo = await NewRepoAsync();
		try
		{
			string hookPath = WriteHook(repo, OLD_HOOK);

			HookRepairResult result =
				await HookRepairer.RepairRepoAsync(repo, false);

			Assert.Equal(HookRepairOutcome.Updated, result.Outcome);
			Assert.Equal(
				HookInstaller.Trampoline.Trim(),
				(await File.ReadAllTextAsync(hookPath)).Trim());
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	[Fact]
	public void IsCommittyManaged_TrampolineCarriesAValidChecksum() =>
		Assert.Contains(
			"# committy-hook-sha256: ",
			HookInstaller.Trampoline);

	[Fact]
	public void IsCommittyManaged_EditedTrampolineIsForeign() =>
		Assert.False(
			HookInstaller.IsCommittyManaged(
				HookInstaller.Trampoline.Replace(
					"exit 0\n",
					"./scripts/lint.sh\nexit 0\n")));

	[Fact]
	public void IsCommittyManaged_HandWrittenHookCallingCommittyIsForeign() =>
		Assert.False(
			HookInstaller.IsCommittyManaged(
				"#!/bin/sh\n./scripts/lint-message.sh \"$1\"\n"
				+ "committy prepare-commit-msg \"$1\"\n"));

	[Fact]
	public void IsCommittyManaged_LegacyHookSavedWithBomAndCrlf() =>
		Assert.True(
			HookInstaller.IsCommittyManaged(
				"\uFEFF" + OLD_HOOK.Replace("\n", "\r\n") + "\r\n"));

	[Fact]
	public async Task RepairRepoAsync_HandWrittenHookCallingCommitty_IsSkipped()
	{
		string repo = await NewRepoAsync();
		try
		{
			const string handWritten =
				"#!/bin/sh\necho extra\ncommitty prepare-commit-msg \"$1\"\n";
			string hookPath = WriteHook(repo, handWritten);

			HookRepairResult result =
				await HookRepairer.RepairRepoAsync(repo, false);

			Assert.Equal(HookRepairOutcome.SkippedForeign, result.Outcome);
			Assert.Equal(handWritten, await File.ReadAllTextAsync(hookPath));
		}
		finally
		{
			Directory.Delete(repo, true);
		}
	}

	[Fact]
	public void BackUpHook_IdenticalBackup_IsReused()
	{
		string dir = Directory.CreateTempSubdirectory("committy-bak-").FullName;
		try
		{
			string hookPath = Path.Combine(dir, "prepare-commit-msg");
			File.WriteAllText(hookPath, OLD_HOOK);
			File.WriteAllText(hookPath + ".bak", OLD_HOOK);

			string backupPath = HookInstaller.BackUpHook(hookPath);

			Assert.Equal(hookPath + ".bak", backupPath);
			Assert.Equal(2, Directory.GetFiles(dir).Length);
		}
		finally
		{
			Directory.Delete(dir, true);
		}
	}

	[Fact]
	public void BackUpHook_DifferentBackup_IsKeptAndANewOneWritten()
	{
		string dir = Directory.CreateTempSubdirectory("committy-bak-").FullName;
		try
		{
			string hookPath = Path.Combine(dir, "prepare-commit-msg");
			File.WriteAllText(hookPath, OLD_HOOK);
			File.WriteAllText(hookPath + ".bak", FOREIGN_HOOK);

			string backupPath = HookInstaller.BackUpHook(hookPath);

			Assert.Matches(
				@"prepare-commit-msg-saved_\d{8}T\d{6}\.bak$",
				backupPath);
			Assert.Equal(OLD_HOOK, File.ReadAllText(backupPath));
			Assert.Equal(FOREIGN_HOOK, File.ReadAllText(hookPath + ".bak"));
		}
		finally
		{
			Directory.Delete(dir, true);
		}
	}
}

/// <summary>
/// A fact that needs a file the current user cannot read: Unix permissions,
/// and not root, who reads everything regardless.
/// </summary>
public sealed class UnreadableFileFactAttribute : FactAttribute
{
	public UnreadableFileFactAttribute()
	{
		if (OperatingSystem.IsWindows())
		{
			Skip = "Needs Unix file permissions.";
		}
		else if (Environment.UserName == "root")
		{
			Skip = "Root can read a file with no permissions.";
		}
	}
}
