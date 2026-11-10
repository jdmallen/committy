using System.Text;

namespace JDMallen.Committy.Tests;

public class PatchCondenserTests
{
	/// <summary>A modified-file section with the given added/removed lines.</summary>
	private static string ModifiedFile(
		string path,
		IEnumerable<string> addedLines,
		IEnumerable<string>? removedLines = null)
	{
		List<string> added = [.. addedLines];
		List<string> removed = [.. removedLines ?? []];
		var builder = new StringBuilder();
		builder.Append($"diff --git a/{path} b/{path}\n");
		builder.Append("index 1111111..2222222 100644\n");
		builder.Append($"--- a/{path}\n");
		builder.Append($"+++ b/{path}\n");
		builder.Append(
			$"@@ -1,{removed.Count + 1} +1,{added.Count + 1} @@\n");
		builder.Append(" context\n");

		foreach (string line in removed)
		{
			builder.Append('-').Append(line).Append('\n');
		}

		foreach (string line in added)
		{
			builder.Append('+').Append(line).Append('\n');
		}

		return builder.ToString();
	}

	private static IEnumerable<string> Lines(int count, string prefix = "line")
		=> Enumerable.Range(1, count).Select(number => $"{prefix} {number}");

	[Fact]
	public void Condense_SmallPatch_PassesThroughUnchanged()
	{
		string patch = ModifiedFile("src/App.cs", ["new"], ["old"]);

		CondensedPatch condensed = PatchCondenser.Condense(patch);

		Assert.Equal(patch, condensed.Text);
		FileChange file = Assert.Single(condensed.Files);
		Assert.Equal(
			new FileChange("src/App.cs", 'M', 1, 1, false, FileCondensing.None),
			file);
		Assert.Equal(ChangeSize.Small, condensed.Size);
	}

	[Fact]
	public void Condense_NotAGitPatch_ReturnsTextAsIs()
	{
		const string patch = "+added line";

		CondensedPatch condensed = PatchCondenser.Condense(patch);

		Assert.Equal(patch, condensed.Text);
		Assert.Empty(condensed.Files);
	}

	[Fact]
	public void Condense_Preamble_IsKept()
	{
		string patch = "commit abc\nAuthor: someone\n\n"
			+ ModifiedFile("a.txt", ["x"]);

		CondensedPatch condensed = PatchCondenser.Condense(patch);

		Assert.Equal(patch, condensed.Text);
		Assert.Single(condensed.Files);
	}

	[Fact]
	public void Condense_ContentLinesStartingWithDashes_AreCountedNotHeaders()
	{
		// "--- x" and "+++ y" inside a hunk are content, not file headers.
		string patch = ModifiedFile("doc.md", ["++ plus"], ["-- dashes"]);

		CondensedPatch condensed = PatchCondenser.Condense(patch);

		FileChange file = Assert.Single(condensed.Files);
		Assert.Equal("doc.md", file.Path);
		Assert.Equal(1, file.Added);
		Assert.Equal(1, file.Removed);
	}

	[Fact]
	public void Condense_NewDeletedRenamedFiles_GetStatusAndPath()
	{
		const string patch =
			"diff --git a/new.txt b/new.txt\n"
			+ "new file mode 100644\n"
			+ "index 0000000..1111111\n"
			+ "--- /dev/null\n"
			+ "+++ b/new.txt\n"
			+ "@@ -0,0 +1 @@\n"
			+ "+hello\n"
			+ "diff --git a/gone.txt b/gone.txt\n"
			+ "deleted file mode 100644\n"
			+ "index 1111111..0000000\n"
			+ "--- a/gone.txt\n"
			+ "+++ /dev/null\n"
			+ "@@ -1 +0,0 @@\n"
			+ "-bye\n"
			+ "diff --git a/old name.txt b/new name.txt\n"
			+ "similarity index 100%\n"
			+ "rename from old name.txt\n"
			+ "rename to new name.txt\n";

		CondensedPatch condensed = PatchCondenser.Condense(patch);

		Assert.Collection(
			condensed.Files,
			file => Assert.Equal(("new.txt", 'A', 1, 0),
				(file.Path, file.Status, file.Added, file.Removed)),
			file => Assert.Equal(("gone.txt", 'D', 0, 1),
				(file.Path, file.Status, file.Added, file.Removed)),
			file => Assert.Equal(("new name.txt", 'R', 0, 0),
				(file.Path, file.Status, file.Added, file.Removed)));
		Assert.Equal(patch, condensed.Text);
	}

	[Fact]
	public void Condense_CrlfPatch_PathHasNoCarriageReturn()
	{
		string patch = ModifiedFile("win.txt", ["x"]).Replace("\n", "\r\n");

		CondensedPatch condensed = PatchCondenser.Condense(patch);

		Assert.Equal("win.txt", Assert.Single(condensed.Files).Path);
	}

	[Theory]
	[InlineData("pnpm-lock.yaml")]
	[InlineData("web/package-lock.json")]
	[InlineData("Cargo.lock")]
	[InlineData("go.sum")]
	[InlineData("src/Proj/packages.lock.json")]
	[InlineData("dist/app.min.js")]
	[InlineData("dist/app.js.map")]
	public void IsGeneratedFile_LockAndGeneratedNames_ReturnsTrue(string path)
		=> Assert.True(PatchCondenser.IsGeneratedFile(path));

	[Theory]
	[InlineData("package.json")]
	[InlineData("src/lock.cs")]
	[InlineData("docs/sitemap.md")]
	public void IsGeneratedFile_OrdinaryNames_ReturnsFalse(string path)
		=> Assert.False(PatchCondenser.IsGeneratedFile(path));

	[Fact]
	public void Condense_LockFile_ReplacedWithNoteAndExcludedFromSize()
	{
		string lockFile = ModifiedFile(
			"pnpm-lock.yaml",
			Lines(5000, "  resolution"),
			Lines(2000, "  old"));
		string patch = ModifiedFile("package.json", ["  \"x\": \"1\""])
			+ lockFile;

		CondensedPatch condensed = PatchCondenser.Condense(patch);

		Assert.DoesNotContain("resolution", condensed.Text);
		Assert.Contains("+++ b/pnpm-lock.yaml", condensed.Text);
		Assert.Contains(
			"[committy] Lock/generated file, +5000/-2000 lines; contents omitted.",
			condensed.Text);
		FileChange lockChange = condensed.Files[1];
		Assert.Equal(FileCondensing.Generated, lockChange.Condensing);
		Assert.Equal(5000, lockChange.Added);
		Assert.Equal(1, condensed.MeaningfulFiles);
		Assert.Equal(1, condensed.MeaningfulLines);
		Assert.Equal(ChangeSize.Small, condensed.Size);
	}

	[Fact]
	public void Condense_GitBinaryPatch_ReplacedWithNote()
	{
		string patch =
			"diff --git a/logo.png b/logo.png\n"
			+ "index 1111111..2222222 100644\n"
			+ "GIT binary patch\n"
			+ "literal 1234\n"
			+ "zcmV-abcdefghijklmnopqrstuvwxyz\n"
			+ "\n";

		CondensedPatch condensed = PatchCondenser.Condense(patch);

		Assert.DoesNotContain("zcmV", condensed.Text);
		Assert.Contains("[committy] Binary file; contents omitted.", condensed.Text);
		FileChange file = Assert.Single(condensed.Files);
		Assert.True(file.IsBinary);
		Assert.Equal(FileCondensing.Generated, file.Condensing);
	}

	[Fact]
	public void Condense_ThreeLookAlikeSiblings_KeepsFirstAndCollapsesRest()
	{
		string patch =
			ModifiedFile("Resources/Strings.de.resx", Lines(4, "de"))
			+ ModifiedFile("Resources/Strings.fr.resx", Lines(4, "fr"))
			+ ModifiedFile("Resources/Strings.ja.resx", Lines(4, "ja"))
			+ ModifiedFile("Resources/Strings.resx", Lines(6, "en"));

		CondensedPatch condensed = PatchCondenser.Condense(patch);

		Assert.Contains("+de 1", condensed.Text);
		Assert.DoesNotContain("+fr 1", condensed.Text);
		Assert.DoesNotContain("+ja 1", condensed.Text);
		// The neutral file's counts differ, so it isn't a repeat.
		Assert.Contains("+en 1", condensed.Text);
		Assert.Contains(
			"also made to 2 other files; their diffs are omitted: Strings.fr.resx, Strings.ja.resx.",
			condensed.Text);
		Assert.Equal(
			[
				FileCondensing.None,
				FileCondensing.Repeated,
				FileCondensing.Repeated,
				FileCondensing.None,
			],
			condensed.Files.Select(file => file.Condensing));
		Assert.Equal("Resources/Strings.de.resx", condensed.Files[1].RepeatOf);
		Assert.Equal(2, condensed.MeaningfulFiles);
		Assert.Equal(10, condensed.MeaningfulLines);
	}

	[Fact]
	public void Condense_TwoLookAlikeSiblings_AreNotCollapsed()
	{
		string patch =
			ModifiedFile("Strings.de.resx", Lines(4, "de"))
			+ ModifiedFile("Strings.fr.resx", Lines(4, "fr"));

		CondensedPatch condensed = PatchCondenser.Condense(patch);

		Assert.Equal(patch, condensed.Text);
	}

	[Fact]
	public void Condense_SiblingsInDifferentDirectories_AreNotCollapsed()
	{
		string patch =
			ModifiedFile("a/Strings.de.resx", ["x"])
			+ ModifiedFile("b/Strings.de.resx", ["x"])
			+ ModifiedFile("c/Strings.de.resx", ["x"]);

		CondensedPatch condensed = PatchCondenser.Condense(patch);

		Assert.Equal(patch, condensed.Text);
	}

	[Fact]
	public void Condense_OversizedFile_IsTruncatedWithNote()
	{
		string patch = ModifiedFile(
			"src/Big.cs",
			Lines(2000, "// a fairly long generated-looking line"));

		CondensedPatch condensed = PatchCondenser.Condense(patch);

		FileChange file = Assert.Single(condensed.Files);
		Assert.Equal(FileCondensing.Truncated, file.Condensing);
		Assert.Equal(2000, file.Added);
		Assert.Contains("[committy] Diff truncated for length: showed ", condensed.Text);
		Assert.True(
			condensed.Text.Length < PatchCondenser.PER_FILE_CHAR_LIMIT + 200);
		Assert.Contains("+++ b/src/Big.cs", condensed.Text);
	}

	private static string ManyFiles(int fileCount, int linesPerFile)
	{
		var builder = new StringBuilder();

		for (int fileNumber = 0; fileNumber < fileCount; fileNumber++)
		{
			builder.Append(
				ModifiedFile(
					$"dir{fileNumber}/File{fileNumber}",
					Lines(linesPerFile, $"f{fileNumber} " + new string('x', 40))));
		}

		return builder.ToString();
	}

	[Fact]
	public void Condense_OverTotalLimit_SharesBudgetSoEveryFileShows()
	{
		// Each file is under the per-file cap; together they're over the total.
		int fileCount = PatchCondenser.TOTAL_CHAR_LIMIT
			/ PatchCondenser.PER_FILE_CHAR_LIMIT
			+ 3;

		CondensedPatch condensed =
			PatchCondenser.Condense(ManyFiles(fileCount, 230));

		Assert.True(condensed.Text.Length <= PatchCondenser.TOTAL_CHAR_LIMIT);
		Assert.All(
			condensed.Files,
			file => Assert.Equal(FileCondensing.Truncated, file.Condensing));

		for (int fileNumber = 0; fileNumber < fileCount; fileNumber++)
		{
			Assert.Contains($"+f{fileNumber} x", condensed.Text);
		}

		Assert.Contains(
			"[committy] Diff truncated for length: showed ",
			condensed.Text);
		Assert.Equal(ChangeSize.Large, condensed.Size);
	}

	[Fact]
	public void Condense_HugeFileAmongSmallOnes_OnlyHugeFileIsCut()
	{
		string patch = ManyFiles(9, PatchCondenser.PER_FILE_CHAR_LIMIT / 50)
			+ ModifiedFile("small/Tiny.cs", ["tiny change"]);

		CondensedPatch condensed = PatchCondenser.Condense(patch);

		Assert.True(condensed.Text.Length <= PatchCondenser.TOTAL_CHAR_LIMIT);
		Assert.Equal(FileCondensing.None, condensed.Files[^1].Condensing);
		Assert.Contains("+tiny change", condensed.Text);
	}

	[Fact]
	public void Condense_TooManyFilesToShareOut_OmitsTheRestAndSaysSo()
	{
		int fileCount = PatchCondenser.TOTAL_CHAR_LIMIT
			/ PatchCondenser.MIN_SHARED_FILE_CHARS
			+ 50;

		CondensedPatch condensed =
			PatchCondenser.Condense(ManyFiles(fileCount, 30));

		Assert.True(
			condensed.Text.Length <= PatchCondenser.TOTAL_CHAR_LIMIT + 200);
		int omitted = condensed.Files.Count(file
			=> file.Condensing == FileCondensing.Omitted);
		Assert.True(omitted > 0);
		Assert.Equal(fileCount, condensed.Files.Count);
		Assert.Contains(
			$"[committy] {omitted} more file diffs were omitted",
			condensed.Text);
	}

	[Fact]
	public void Condense_TruncatedRepresentative_KeepsRepeatNote()
	{
		var builder = new StringBuilder();

		foreach (string locale in new[] { "de", "fr", "ja" })
		{
			builder.Append(
				ModifiedFile(
					$"Resources/Strings.{locale}.resx",
					Lines(1000, $"{locale} long translated text line")));
		}

		CondensedPatch condensed = PatchCondenser.Condense(builder.ToString());

		Assert.Equal(FileCondensing.Truncated, condensed.Files[0].Condensing);
		Assert.Contains("also made to 2 other files", condensed.Text);
	}

	[Theory]
	[InlineData(1, 1, ChangeSize.Small)]
	[InlineData(30, 3, ChangeSize.Small)]
	[InlineData(31, 3, ChangeSize.Medium)]
	[InlineData(10, 4, ChangeSize.Medium)]
	[InlineData(250, 10, ChangeSize.Medium)]
	[InlineData(251, 1, ChangeSize.Large)]
	[InlineData(20, 11, ChangeSize.Large)]
	public void ClassifySize_Thresholds(
		int lines,
		int files,
		ChangeSize expected)
		=> Assert.Equal(expected, PatchCondenser.ClassifySize(lines, files));
}
