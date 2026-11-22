namespace JDMallen.Committy;

/// <summary>
/// The names a setting accepts for each value of <typeparamref name="T" />:
/// one canonical spelling, which <c>committy config</c> writes, plus aliases
/// it also reads. Parsing and printing share the table, so they can't drift.
/// Matching ignores case and surrounding whitespace.
/// </summary>
/// <param name="blank">The value an unset or blank setting means.</param>
/// <param name="entries">Each value, its canonical name, then its aliases.</param>
public sealed class NameTable<T>(
	T blank,
	params (T Value, string Name, string[] Aliases)[] entries)
	where T : struct, Enum
{
	private readonly Dictionary<string, T> _byName = entries
		.SelectMany(entry => entry.Aliases
			.Prepend(entry.Name)
			.Select(name => KeyValuePair.Create(name, entry.Value)))
		.ToDictionary(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<T, string> _canonical = entries.ToDictionary(
		entry => entry.Value,
		entry => entry.Name);

	/// <summary>
	/// The value <paramref name="name" /> means; <c>blank</c> when it is unset
	/// or blank; null when it names no value.
	/// </summary>
	public T? Parse(string? name) =>
		string.IsNullOrWhiteSpace(name)
			? blank
			: _byName.TryGetValue(name.Trim(), out T value)
				? value
				: null;

	/// <summary>The canonical spelling, which <see cref="Parse" /> reads back.</summary>
	public string Name(T value) => _canonical[value];
}
