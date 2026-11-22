namespace JDMallen.Committy.Tests;

/// <summary>Hook texts that real committy releases installed.</summary>
internal static class LegacyHooks
{
	/// <summary>
	/// The trampoline committy installed from 7d6ffd7 until hooks gained a
	/// checksum line. Kept byte for byte: it is what users have on disk.
	/// </summary>
	public const string UNSTAMPED_TRAMPOLINE =
		"""
		#!/usr/bin/env bash
		# Committy trampoline hook — installed by `committy install-hook`.
		# Intentionally minimal: all behavior lives in the committy binary so it can
		# be updated centrally without re-installing per-repo hooks.

		# Skip non-plain commits (merge, squash, message, template, etc.).
		if [ -n "$2" ]; then
		    exit 0
		fi

		# If committy isn't on PATH, do nothing rather than block the commit.
		if ! command -v committy >/dev/null 2>&1; then
		    exit 0
		fi

		# Never let hook failure abort the commit; the binary writes the message in place.
		committy prepare-commit-msg "$1"
		exit 0
		""";
}
