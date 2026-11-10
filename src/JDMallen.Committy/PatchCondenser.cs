using System.Globalization;
using System.Text;

namespace JDMallen.Committy;

/// <summary>How much a patch changes, judged on its meaningful content.</summary>
public enum ChangeSize
{
	Small,
	Medium,
	Large,
}

/// <summary>What <see cref="PatchCondenser" /> did to one file's diff.</summary>
public enum FileCondensing
{
	/// <summary>Sent in full.</summary>
	None,

	/// <summary>Lock or generated file; diff replaced with a one-line note.</summary>
	Generated,

	/// <summary>
	/// Same shape of change as a sibling file that is sent in full; diff left
	/// out.
	/// </summary>
	Repeated,

	/// <summary>Diff cut short at the per-file limit.</summary>
	Truncated,

	/// <summary>Diff left out to keep the whole patch under the total limit.</summary>
	Omitted,
}

/// <summary>One file in a patch, as the condenser saw it.</summary>
/// <param name="Path">The file's path (the new path for a rename).</param>
/// <param name="Status">A (added), D (deleted), R (renamed) or M (modified).</param>
/// <param name="Added">Lines added.</param>
/// <param name="Removed">Lines removed.</param>
/// <param name="IsBinary">Whether git reported it as binary.</param>
/// <param name="Condensing">What was done to its diff.</param>
/// <param name="RepeatOf">
/// For <see cref="FileCondensing.Repeated" />, the path of the file sent in
/// full.
/// </param>
public sealed record FileChange(
	string Path,
	char Status,
	int Added,
	int Removed,
	bool IsBinary,
	FileCondensing Condensing,
	string? RepeatOf = null);

/// <summary>The condensed patch and what is known about the change.</summary>
/// <param name="Text">The patch text to send to the model.</param>
/// <param name="Files">Every file in the patch, in patch order.</param>
/// <param name="MeaningfulFiles">
/// Files that count toward the size: lock/generated files and collapsed
/// repeats excluded.
/// </param>
/// <param name="MeaningfulLines">
/// Changed lines (added + removed) in those files.
/// </param>
/// <param name="Size">The size tier derived from the meaningful counts.</param>
public sealed record CondensedPatch(
	string Text,
	IReadOnlyList<FileChange> Files,
	int MeaningfulFiles,
	int MeaningfulLines,
	ChangeSize Size)
{
	public int TotalAdded => Files.Sum(file => file.Added);

	public int TotalRemoved => Files.Sum(file => file.Removed);
}

/// <summary>
/// Shrinks a unified git patch before it goes to the model: lock and generated
/// files become one-line notes, near-identical sibling files (translations,
/// for one) are shown once, and oversized diffs are truncated, so a big
/// lock file can't push a request past the model's limits. Small patches pass
/// through unchanged.
/// </summary>
public static class PatchCondenser
{
	/// <summary>A single file's diff longer than this is truncated.</summary>
	public const int PER_FILE_CHAR_LIMIT = 12_000;

	/// <summary>
	/// The condensed patch is kept under this (~30k tokens); file diffs that
	/// don't fit are left out.
	/// </summary>
	public const int TOTAL_CHAR_LIMIT = 120_000;

	/// <summary>
	/// When the total is over budget, no file is cut below this many chars
	/// while sharing the budget out.
	/// </summary>
	public const int MIN_SHARED_FILE_CHARS = 600;

	/// <summary>Room kept for the notes added after a cut.</summary>
	private const int NOTE_ALLOWANCE = 200;

	/// <summary>Smallest group of look-alike siblings that gets collapsed.</summary>
	public const int MIN_REPEAT_GROUP = 3;

	public const int SMALL_MAX_LINES = 30;
	public const int SMALL_MAX_FILES = 3;
	public const int LARGE_MIN_LINES = 251;
	public const int LARGE_MIN_FILES = 11;

	/// <summary>Prefix on every note the condenser writes into the patch.</summary>
	public const string NOTE_PREFIX = "[committy]";

	private const string DIFF_HEADER = "diff --git ";

	private static readonly HashSet<string> s_generatedFileNames =
		new(StringComparer.OrdinalIgnoreCase)
		{
			"pnpm-lock.yaml",
			"package-lock.json",
			"npm-shrinkwrap.json",
			"yarn.lock",
			"bun.lock",
			"bun.lockb",
			"Cargo.lock",
			"Gemfile.lock",
			"composer.lock",
			"poetry.lock",
			"Pipfile.lock",
			"uv.lock",
			"flake.lock",
			"Podfile.lock",
			"go.sum",
			"packages.lock.json",
			".terraform.lock.hcl",
		};

	private static readonly string[] s_generatedFileSuffixes =
	[
		".min.js",
		".min.css",
		".map",
		".pdf",
	];

	public static CondensedPatch Condense(string patch)
	{
		ArgumentNullException.ThrowIfNull(patch);

		string[] lines = patch.Split('\n');
		List<int> sectionStarts = [];

		for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
		{
			if (lines[lineIndex].StartsWith(DIFF_HEADER, StringComparison.Ordinal))
			{
				sectionStarts.Add(lineIndex);
			}
		}

		if (sectionStarts.Count == 0)
		{
			// Not a git patch we can parse; send it as-is.
			return new CondensedPatch(
				patch,
				[],
				0,
				0,
				ChangeSize.Small);
		}

		string? preamble = sectionStarts[0] > 0
			? string.Join('\n', lines[..sectionStarts[0]])
			: null;
		List<FileSection> sections = [];

		for (int sectionIndex = 0;
			 sectionIndex < sectionStarts.Count;
			 sectionIndex++)
		{
			int start = sectionStarts[sectionIndex];
			int end = sectionIndex + 1 < sectionStarts.Count
				? sectionStarts[sectionIndex + 1]
				: lines.Length;
			sections.Add(FileSection.Parse(lines[start..end]));
		}

		MarkGenerated(sections);
		MarkRepeated(sections);
		int preambleLength = preamble?.Length ?? 0;
		TruncateLongFiles(sections, PER_FILE_CHAR_LIMIT);
		ShareBudget(sections, preambleLength);
		int omittedCount = OmitOverBudget(sections, preambleLength);

		return new CondensedPatch(
			BuildText(preamble, sections, omittedCount),
			[.. sections.Select(section => section.ToFileChange())],
			CountMeaningfulFiles(sections),
			CountMeaningfulLines(sections),
			ClassifySize(sections));
	}

	/// <summary>The size tier for the given meaningful counts.</summary>
	public static ChangeSize ClassifySize(int meaningfulLines, int meaningfulFiles)
	{
		if (meaningfulLines >= LARGE_MIN_LINES
			|| meaningfulFiles >= LARGE_MIN_FILES)
		{
			return ChangeSize.Large;
		}

		return meaningfulLines <= SMALL_MAX_LINES
			   && meaningfulFiles <= SMALL_MAX_FILES
			? ChangeSize.Small
			: ChangeSize.Medium;
	}

	/// <summary>Whether a path names a lock or generated file.</summary>
	public static bool IsGeneratedFile(string path)
	{
		string fileName = FileNameOf(path);

		return s_generatedFileNames.Contains(fileName)
			|| s_generatedFileSuffixes.Any(suffix =>
				fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
	}

	private static ChangeSize ClassifySize(List<FileSection> sections) =>
		ClassifySize(
			CountMeaningfulLines(sections),
			CountMeaningfulFiles(sections));

	private static bool IsMeaningful(FileSection section) =>
		section.Condensing is not FileCondensing.Generated
			and not FileCondensing.Repeated;

	private static int CountMeaningfulFiles(List<FileSection> sections) =>
		sections.Count(IsMeaningful);

	private static int CountMeaningfulLines(List<FileSection> sections) =>
		sections
			.Where(IsMeaningful)
			.Sum(section => section.Added + section.Removed);

	private static void MarkGenerated(List<FileSection> sections)
	{
		foreach (FileSection section in sections)
		{
			if (!section.HasBinaryPatch && !IsGeneratedFile(section.Path))
			{
				continue;
			}

			section.Condensing = FileCondensing.Generated;
			string note = section.HasBinaryPatch
				? $"{NOTE_PREFIX} Binary file; contents omitted."
				: string.Create(
					CultureInfo.InvariantCulture,
					$"{NOTE_PREFIX} Lock/generated file, +{section.Added}/-{section.Removed} lines; contents omitted.");
			section.Text = section.HeaderText() + "\n" + note;
		}
	}

	private static void MarkRepeated(List<FileSection> sections)
	{
		IEnumerable<IGrouping<string, FileSection>> groups = sections
			.Where(section => section.Condensing == FileCondensing.None
							  && !section.IsBinary
							  && section.Added + section.Removed > 0)
			.Select(section => (Section: section, Key: section.RepeatKey()))
			.Where(pair => pair.Key is not null)
			.GroupBy(pair => pair.Key!, pair => pair.Section)
			.Where(group => group.Count() >= MIN_REPEAT_GROUP);

		foreach (IGrouping<string, FileSection> group in groups)
		{
			FileSection representative = group.First();
			List<FileSection> repeats = [.. group.Skip(1)];

			foreach (FileSection repeat in repeats)
			{
				repeat.Condensing = FileCondensing.Repeated;
				repeat.RepeatOf = representative.Path;
				repeat.Text = null;
			}

			// After the header, so a later truncation can't cut it off.
			representative.Text = representative.HeaderText()
				+ "\n"
				+ string.Create(
					CultureInfo.InvariantCulture,
					$"{NOTE_PREFIX} The same shape of change (+{representative.Added}/-{representative.Removed} lines each) was also made to {repeats.Count} other files; their diffs are omitted: ")
				+ string.Join(
					", ",
					repeats.Select(repeat => FileNameOf(repeat.Path)))
				+ ".\n"
				+ representative.BodyText();
		}
	}

	private static void TruncateLongFiles(
		List<FileSection> sections,
		int charLimit)
	{
		foreach (FileSection section in sections)
		{
			if (section.Condensing is (FileCondensing.None
					or FileCondensing.Truncated)
				&& section.Text is not null
				&& section.Text.Length > charLimit)
			{
				Truncate(section, charLimit);
			}
		}
	}

	/// <summary>
	/// Over the total limit: give every file the same allowance (files smaller
	/// than it keep their full text, and the slack goes to the rest), so a
	/// few huge files can't crowd out every file after them.
	/// </summary>
	private static void ShareBudget(
		List<FileSection> sections,
		int preambleLength)
	{
		List<int> lengths = [.. sections
			.Where(section => section.Text is not null)
			.Select(section => section.Text!.Length + 1)
			.Order()];
		int available = TOTAL_CHAR_LIMIT - preambleLength - NOTE_ALLOWANCE;

		if (lengths.Sum() <= available)
		{
			return;
		}

		int allowance = 0;

		for (int index = 0; index < lengths.Count; index++)
		{
			allowance = available / (lengths.Count - index);

			if (lengths[index] > allowance)
			{
				break;
			}

			available -= lengths[index];
		}

		// Many files: cut to a readable minimum and let OmitOverBudget drop
		// whatever still doesn't fit.
		TruncateLongFiles(
			sections,
			Math.Max(allowance - NOTE_ALLOWANCE, MIN_SHARED_FILE_CHARS));
	}

	/// <summary>Cuts a file's diff at a line boundary and says so.</summary>
	private static void Truncate(FileSection section, int charLimit)
	{
		// Re-cutting an already truncated file: drop its old note first.
		string text = section.Condensing == FileCondensing.Truncated
			? section.Text![..section.Text!.LastIndexOf('\n')]
			: section.Text!;
		string[] sectionLines = text.Split('\n');
		int totalLines = section.TotalLines ?? sectionLines.Length;
		int keptLength = 0;
		int keptLines = 0;

		while (keptLines < sectionLines.Length
			   && keptLength + sectionLines[keptLines].Length + 1 <= charLimit)
		{
			keptLength += sectionLines[keptLines].Length + 1;
			keptLines++;
		}

		section.TotalLines = totalLines;
		section.Condensing = FileCondensing.Truncated;
		section.Text = string.Join('\n', sectionLines[..keptLines])
			+ "\n"
			+ string.Create(
				CultureInfo.InvariantCulture,
				$"{NOTE_PREFIX} Diff truncated for length: showed {keptLines} of {totalLines} lines (+{section.Added}/-{section.Removed} lines in full).");
	}

	private static int OmitOverBudget(
		List<FileSection> sections,
		int preambleLength)
	{
		int usedLength = preambleLength;
		int omittedCount = 0;

		foreach (FileSection section in sections)
		{
			if (section.Text is null)
			{
				continue;
			}

			int sectionLength = section.Text.Length + 1;

			if (usedLength + sectionLength <= TOTAL_CHAR_LIMIT)
			{
				usedLength += sectionLength;
				continue;
			}

			section.Condensing = FileCondensing.Omitted;
			section.Text = null;
			omittedCount++;
		}

		return omittedCount;
	}

	private static string BuildText(
		string? preamble,
		List<FileSection> sections,
		int omittedCount)
	{
		List<string> parts = [];

		if (preamble is not null)
		{
			parts.Add(preamble);
		}

		parts.AddRange(
			sections
				.Where(section => section.Text is not null)
				.Select(section => section.Text!));

		if (omittedCount > 0)
		{
			parts.Add(
				string.Create(
					CultureInfo.InvariantCulture,
					$"{NOTE_PREFIX} {omittedCount} more file diffs were omitted to stay within the size limit; FILES CHANGED marks them as omitted."));
		}

		return string.Join('\n', parts);
	}

	private static string FileNameOf(string path)
	{
		int lastSlash = path.LastIndexOf('/');

		return lastSlash < 0 ? path : path[(lastSlash + 1)..];
	}

	/// <summary>One <c>diff --git</c> section and what's been decided about it.</summary>
	private sealed class FileSection
	{
		private string[] _lines = [];

		public string Path { get; private set; } = string.Empty;

		public char Status { get; private set; } = 'M';

		public int Added { get; private set; }

		public int Removed { get; private set; }

		public bool IsBinary { get; private set; }

		public bool HasBinaryPatch { get; private set; }

		public FileCondensing Condensing { get; set; } = FileCondensing.None;

		public string? RepeatOf { get; set; }

		/// <summary>Line count before any truncation.</summary>
		public int? TotalLines { get; set; }

		/// <summary>The text to send, or null when it's left out entirely.</summary>
		public string? Text { get; set; }

		/// <summary>Index of the first hunk or binary-patch line.</summary>
		private int BodyStart { get; set; }

		public static FileSection Parse(string[] lines)
		{
			var section = new FileSection
			{
				_lines = lines,
				Text = string.Join('\n', lines),
				BodyStart = lines.Length,
			};
			string? minusPath = null;
			string? plusPath = null;
			string? renameToPath = null;
			bool inHunk = false;

			for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
			{
				string line = lines[lineIndex];

				if (line.StartsWith("@@", StringComparison.Ordinal))
				{
					section.BodyStart = Math.Min(section.BodyStart, lineIndex);
					inHunk = true;

					continue;
				}

				if (inHunk)
				{
					if (line.StartsWith('+'))
					{
						section.Added++;
					}
					else if (line.StartsWith('-'))
					{
						section.Removed++;
					}

					continue;
				}

				if (line.StartsWith("GIT binary patch", StringComparison.Ordinal))
				{
					section.BodyStart = lineIndex;
					section.IsBinary = true;
					section.HasBinaryPatch = true;

					break;
				}

				if (line.StartsWith("new file mode", StringComparison.Ordinal))
				{
					section.Status = 'A';
				}
				else if (line.StartsWith(
							 "deleted file mode",
							 StringComparison.Ordinal))
				{
					section.Status = 'D';
				}
				else if (line.StartsWith("rename from ", StringComparison.Ordinal))
				{
					section.Status = 'R';
				}
				else if (line.StartsWith("rename to ", StringComparison.Ordinal))
				{
					renameToPath = line["rename to ".Length..].TrimEnd('\r');
				}
				else if (line.StartsWith("--- ", StringComparison.Ordinal))
				{
					minusPath = CleanPath(line[4..]);
				}
				else if (line.StartsWith("+++ ", StringComparison.Ordinal))
				{
					plusPath = CleanPath(line[4..]);
				}
				else if (line.StartsWith("Binary files ", StringComparison.Ordinal))
				{
					section.IsBinary = true;
				}
			}

			section.Path = plusPath is not null and not "/dev/null"
				? plusPath
				: minusPath is not null and not "/dev/null"
					? minusPath
					: renameToPath is not null
						? Unquote(renameToPath)
						: PathFromDiffHeader(lines[0]);

			return section;
		}

		/// <summary>The lines before the first hunk (diff, index, ---/+++).</summary>
		public string HeaderText() =>
			string.Join('\n', _lines[..BodyStart]);

		/// <summary>The hunks (everything after <see cref="HeaderText" />).</summary>
		public string BodyText() =>
			string.Join('\n', _lines[BodyStart..]);

		/// <summary>
		/// Siblings that share this key look alike: same directory, same first
		/// name segment and last extension, same status and line counts. Null
		/// when the name has no extension.
		/// </summary>
		public string? RepeatKey()
		{
			int lastSlash = Path.LastIndexOf('/');
			string directory = lastSlash < 0 ? string.Empty : Path[..lastSlash];
			string fileName = Path[(lastSlash + 1)..];

			if (fileName.Length < 2)
			{
				return null;
			}

			int firstDot = fileName.IndexOf('.', 1);
			int lastDot = fileName.LastIndexOf('.');

			if (firstDot < 0)
			{
				return null;
			}

			return string.Create(
				CultureInfo.InvariantCulture,
				$"{directory}\n{fileName[..firstDot]}\n{fileName[(lastDot + 1)..]}\n{Status}\n{Added}\n{Removed}");
		}

		public FileChange ToFileChange() =>
			new(Path, Status, Added, Removed, IsBinary, Condensing, RepeatOf);

		private static string CleanPath(string rawPath)
		{
			// GNU diff appends a tab and timestamp; git doesn't, but be safe.
			string trimmed = rawPath.TrimEnd('\r');
			int tab = trimmed.IndexOf('\t');
			string path = Unquote(tab < 0 ? trimmed : trimmed[..tab]);

			return path.StartsWith("a/", StringComparison.Ordinal)
				   || path.StartsWith("b/", StringComparison.Ordinal)
				? path[2..]
				: path;
		}

		private static string Unquote(string path) =>
			path.Length >= 2 && path[0] == '"' && path[^1] == '"'
				? path[1..^1]
				: path;

		private static string PathFromDiffHeader(string headerLine)
		{
			string rest = headerLine[DIFF_HEADER.Length..].TrimEnd('\r');
			int quotedStart = rest.LastIndexOf(" \"b/", StringComparison.Ordinal);

			if (quotedStart >= 0)
			{
				return CleanPath(rest[(quotedStart + 1)..]);
			}

			int plainStart = rest.LastIndexOf(" b/", StringComparison.Ordinal);

			return plainStart >= 0 ? CleanPath(rest[(plainStart + 1)..]) : rest;
		}
	}
}
