# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0]

Adds a third provider: any endpoint speaking the OpenAI
`/v1/chat/completions` API, which covers openai.com and — the motivating case —
models you host yourself via llama.cpp, llama-swap, Ollama, vLLM, or LM Studio.
The Azure and Anthropic paths are unchanged.

### Added

- **`openai` provider (alias `local`).** Select it with
  `committy config --provider openai`, `--provider openai` on a single run, or
  `COMMITTY_PROVIDER=openai`. It needs a base URL and a model name; `--endpoint`
  doubles as the base URL and `--model` as the model, so no new flags were added.
  New git config keys: `committy.openai.baseurl`, `committy.openai.model`,
  `committy.openai.apikey`, `committy.openai.timeoutseconds`, and
  `committy.openai.maxtokens`; new environment overrides: `OPENAI_BASE_URL`,
  `OPENAI_MODEL`, and `OPENAI_API_KEY_COMMITTY`.
- **Optional API key.** Unlike the other providers, a missing key is valid rather
  than a misconfiguration — self-hosted runners generally accept anonymous
  requests, and no `Authorization` header is sent when none is set.
- **Reasoning-model support.** Thinking blocks are stripped before parsing,
  whether the server splits them into `reasoning_content` or inlines them as
  `<think>…</think>`.
- **`committy config` prompts for the OpenAI fields** and offers the provider as
  choice 3 in the interactive picker.

### Changed

- **The HTTP timeout is now per-provider,** and the transport is built after the
  config resolves rather than once at startup. Azure and Anthropic keep the
  previous 30 seconds; `openai` defaults to 300, because a self-hosted model may
  spend minutes loading weights before its first token.
- **The token budget is now overridable.** `CommitMessagePrompt.Build` takes an
  optional budget, and the `openai` provider defaults it to 2048 in place of the
  built-in 100 (titles) and 500 (title + body). A reasoning model spends most of
  its output on a thinking block that is then discarded, so the 100-token titles
  budget would leave nothing behind.
- **`JDMallen.Toolbox.AI`** upgraded from 3.0.0 to **3.1.0**, which supplies the
  underlying `OpenAICompatibleChatClient`.
- **`publish.sh` moved to `scripts/publish.sh`,** next to the engine it sources,
  which is also where the vendored `publish-dotnet.sh` header documents the
  wrapper as living. Build it with `./scripts/publish.sh`; arguments are
  unchanged. The wrapper resolves `repo_root` one directory up and still `cd`s
  there, so relative paths (`src/Committy/Committy.csproj`, `dist/`) mean the
  same thing regardless of where you invoke it from.

### Internal

No behavioral change for users; recorded so the diff between 1.2.0 and 1.3.0
reads cleanly.

- **The `HookRepairOutcome` switch in `HookRepairer` is now exhaustive.**
  `AlreadyCurrent` and `NoHook` are listed explicitly as no-ops, and an
  unrecognized outcome throws `ArgumentOutOfRangeException` instead of falling
  through silently — so adding a case to the enum surfaces as a failure rather
  than a skipped repo.
- **`CommittyConfigResolver.ResolveAsync` is now static.** It held no state;
  `Program.ResolveConfigAsync` calls it directly instead of constructing a
  resolver per invocation.
- **Guard clauses inverted** in `CommittyService.ReadPatchFromStdinAsync` (the
  stdin-missing throw comes first) and in the masked-password reader in
  `Program.cs` (control characters `continue` early). Same behavior, less
  nesting; `HookRepairer.DiscoverRepos` returns a collection expression in place
  of `.ToList()`.
- **`Directory.Build.props` and `Committy.slnx` reindented** with spaces and
  spaced self-closing tags, matching what the IDE emits; `CHANGELOG.md` joins
  the solution's Solution Items folder.
- **Test tidy.** The OpenAI provider test constants are `BASE_URL` and `MODEL`,
  and a few long expressions wrap one argument per line.

## [1.2.0]

### Added

- **`--git-dir <dir>` option for `committy repair-hooks`** (repeatable). Targets a
  headless repository directly—a separate git directory with no in-tree `.git`,
  such as a `~/.cfg` dotfiles repo driven by `git --git-dir=~/.cfg
  --work-tree=~`—which `--scan` intentionally skips during a recursive sweep.
  `committy install-hook` already accepted such a directory as its `repo`
  argument; the description now calls that out explicitly.

### Fixed

- **`committy install-hook` ignored a repository's `core.hooksPath`.** It always
  wrote the `prepare-commit-msg` trampoline to `<gitdir>/hooks`, so in a
  repository configured with a custom hooks directory (e.g. a repo-tracked
  `.githooks` folder) the hook was installed somewhere git never looks and
  silently never ran—even though `committy repair-hooks` already handled this
  case correctly. Installation now resolves the hooks directory the same way
  git does (respecting `core.hooksPath`) and prints a note when that directory
  differs from the default, since a custom hooks path may be tracked by git and
  shared with other contributors.

## [1.1.0]

A feature release. It rearchitects committy's chat generation onto the
**`JDMallen.Toolbox.AI`** package, adds an **Anthropic (Claude)** provider
alongside Azure OpenAI, and moves hook installation and credential storage out of
standalone shell scripts and into the binary itself via new `config`,
`install-hook`, and `repair-hooks` subcommands. It also adds a titles-only mode.
The CLI contract is backward compatible—the existing flags (`--api-key`,
`--endpoint`, `--deployment`, `--no-git`, `--clipboard`) and `AZURE_OPENAI_*`
environment variables still work, and everything new is additive—so this is a
minor bump despite the internal provider rearchitecture and the removal of the
`Azure.AI.OpenAI` / `OpenAI` SDKs and the hand-rolled HTTP service layer.

Note: the `install-git-hook.sh` / `install-git-hook.ps1` scripts are gone (use
`committy install-hook`), and hooks installed by 1.0.0 should be refreshed with
`committy repair-hooks` to pick up the new binary-owned trampoline.

### Added

- **Anthropic (Claude) provider.** committy can now target Anthropic in addition
  to Azure OpenAI, selected by the `committy.provider` git config (or the
  `COMMITTY_PROVIDER` environment variable, or `--provider`/`-p`). Anthropic
  credentials and model come from `ANTHROPIC_API_KEY_COMMITTY` / `ANTHROPIC_MODEL` (or
  `--api-key` / `--model`), defaulting to `claude-haiku-4-5-20251001`.
- **`committy config` subcommand.** Persists the provider and credentials to git
  config—global by default, or `--local` for the current repository—prompting
  interactively for any values not supplied as flags, masking secrets, and
  printing the equivalent manual `git config` commands. committy reads these at
  commit time.
- **`committy install-hook` subcommand.** Installs the `prepare-commit-msg`
  trampoline hook locally or globally (`--global`). The binary owns the hook text,
  so installation works from any directory as long as `committy` is on `PATH`;
  this replaces the old `install-git-hook.sh` / `install-git-hook.ps1` scripts.
- **`committy repair-hooks` subcommand.** Replaces stale committy-managed hooks
  with the current trampoline: the current repository by default, `--scan <dir>`
  to sweep a tree recursively (repeatable), and `--global` to re-stamp the global
  template, with `--dry-run` and `--backup`. Only committy-managed hooks are
  touched; foreign hooks are reported and left alone.
- **`committy prepare-commit-msg` subcommand.** The git hook entry point is now a
  first-class subcommand. It no-ops on an empty staged diff, validates the
  resolved config, composes suggestions into the commit message file, and never
  returns a non-zero exit code—a hook failure must not block a commit.
- **Titles-only mode.** `--titles-only` / `-t` (or the `committy.titlesonly` git
  config / `COMMITTY_TITLES_ONLY` environment variable) generates five title-only
  suggestions instead of a single title-plus-body message.
- **Layered configuration.** A `CommittyConfigResolver` resolves every setting
  with the precedence CLI flag → environment variable → git config → built-in
  default, with keys living under the `committy.*` git config section.
- **`global.json`** pinning the .NET SDK and a **`nuget.config`** pinning
  `nuget.org` as the sole package source, for reproducible builds.
- **CI/release automation.** A GitHub Actions release workflow
  (`.github/workflows/release.yml`) that builds the per-RID archives, generates
  `SHA256SUMS.txt`, tags the pushed commit with the raw semver (no `v` prefix),
  and creates a GitHub release whose notes are this version's `CHANGELOG.md`
  section (extracted with an `awk` script).
- **`LICENSE`** file.
- Expanded **README**, and new unit tests covering the commit-message pipeline,
  config resolution, and the hook installer and repairer.

### Changed

- **Chat generation rearchitected onto `JDMallen.Toolbox.AI` (3.0.0).** The
  hand-rolled Azure service (`AzureOpenAIService`) and HTTP plumbing (`Http`,
  `HttpService`, `IHttpService`) are replaced by a `ChatCompletionClientFactory`
  that maps committy's config onto Toolbox.AI clients (`AzureOpenAIChatClient` /
  `AnthropicChatClient`). All completions now share a single
  `IHttpClientFactory`-backed `HttpClient` (30s timeout).
- **CLI restructured into subcommands.** The root command still generates messages
  from a staged diff (read from stdin, or `git diff --cached` directly unless
  `--no-git`, with `--clipboard`/`-c` to copy the first suggestion), but hook and
  credential management now live under dedicated `config`, `install-hook`,
  `repair-hooks`, and `prepare-commit-msg` subcommands instead of standalone shell
  scripts.
- **Publishing refactored to the shared engine.** `publish.sh` is now a thin
  wrapper that sets per-app variables (exe name, runtimes) and sources a vendored,
  shared `scripts/publish-dotnet.sh` engine; the default RID set (`linux-x64`,
  `linux-arm64`, `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`) is unchanged.
- **Solution migrated to `.slnx`.** The `Committy.sln` MSBuild solution is
  replaced by the XML-format `Committy.slnx`.
- **Single version source of truth in `Directory.Build.props`.** `<Version>` is
  set there (so it applies to every project, app and tests) and read by both
  `publish.sh` and the release workflow; it is bumped to `1.1.0`.

### Removed

- **`Azure.AI.OpenAI` (2.8.0-beta.1) and `OpenAI` (2.8.0) SDK dependencies**,
  replaced by the lightweight Toolbox.AI clients.
- **Hand-rolled service layer**: `AzureOpenAIService`, `IAzureOpenAIService`,
  `Http`, `HttpService`, and `IHttpService`.
- **`install-git-hook.sh`, `install-git-hook.ps1`, and `publish.ps1` scripts**,
  replaced by the `install-hook` / `repair-hooks` subcommands and the shared bash
  publish engine.

## [1.0.0]

Initial release. A CLI that turns a staged git diff into AI-generated commit
message suggestions using Azure OpenAI, usable directly from the terminal or as a
`prepare-commit-msg` git hook.

### Added

- **Diff-to-commit-message CLI.** Reads the staged diff—from stdin, or by calling
  `git diff --cached` directly unless `--no-git` is given—and prints AI-generated
  commit message suggestions; `--clipboard` / `-c` copies the first suggestion to
  the clipboard (via `TextCopy`).
- **Azure OpenAI backend.** Built on the `Azure.AI.OpenAI` / `OpenAI` SDKs behind
  an `AzureOpenAIService`, configured via the `--api-key`, `--endpoint`, and
  `--deployment` flags or the corresponding environment variables.
- **`prepare-commit-msg` git hook.** A `hooks/prepare-commit-msg` script plus
  `install-git-hook.sh` / `install-git-hook.ps1` installers (per-repository or a
  `--global` template) that drop AI suggestions into the commit message buffer.
- **Self-contained distribution.** .NET 10, published as single-file,
  self-contained binaries via `publish.sh` / `publish.ps1`.
- **Unit test suite** (`test/Committy.Tests`, xunit) covering the Azure OpenAI
  service, the committy service, and git interaction.
- A **README** documenting installation, configuration, and usage.
