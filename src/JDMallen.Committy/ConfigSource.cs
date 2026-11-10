namespace JDMallen.Committy;

public enum ConfigSourceKind
{
	Flag,
	GitConfig,
	Environment,
	Default,
}

/// <summary>Where a resolved setting's value came from.</summary>
/// <param name="Kind">The kind of source.</param>
/// <param name="Detail">
/// For <see cref="ConfigSourceKind.GitConfig" />, the scope ("local",
/// "global", "system", "worktree" or "command") when known; for
/// <see cref="ConfigSourceKind.Environment" />, the variable's name.
/// </param>
public sealed record ConfigSource(ConfigSourceKind Kind, string? Detail = null)
{
	public static ConfigSource FromFlag { get; } = new(ConfigSourceKind.Flag);

	public static ConfigSource FromDefault { get; } =
		new(ConfigSourceKind.Default);

	/// <summary>
	/// Describes the source the way the user would change it, e.g.
	/// "git config --local" or "$ANTHROPIC_API_KEY_COMMITTY".
	/// </summary>
	public override string ToString() =>
		Kind switch
		{
			ConfigSourceKind.Flag => "command-line flag",
			ConfigSourceKind.GitConfig => Detail switch
			{
				null => "git config",
				"command" => "git -c",
				_ => $"git config --{Detail}",
			},
			ConfigSourceKind.Environment => $"${Detail}",
			_ => "default",
		};
}

/// <summary>
/// A resolved configuration and, keyed by git config key (e.g.
/// <see cref="CommittyConfigResolver.PROVIDER_KEY" />), where each setting
/// came from.
/// </summary>
public sealed record ResolvedConfig(
	CommittyConfig Config,
	IReadOnlyDictionary<string, ConfigSource> Sources);
