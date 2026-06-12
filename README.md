# Committy

> AI-powered git commit messages from your staged diff, via Azure OpenAI or
> Anthropic (Claude).

Committy is a cross-platform .NET 10 console app that reads your staged changes
and
writes a Conventional Commits message for you — either a single title + body
that it
pre-fills into your editor, or five title-only suggestions to pick from. It
installs
as a
`prepare-commit-msg` git hook, so suggestions appear automatically when you
run
`git commit`.

<!-- shields -->
<!--
[![Build](https://img.shields.io/badge/build-passing-brightgreen)](#)
[![License](https://img.shields.io/badge/license-TBD-blue)](#)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](#)
-->

## Install

*

*Prerequisites
**

- .NET 10 Runtime or SDK
- Git
- Access to one supported LLM provider:
  -
  *
  *Azure
  OpenAI
  ** — endpoint, API key, and a deployment name, or
  -

  *
  *Anthropic (
  Claude)
  ** — an API key

### 1. Obtain the executable

Multiple options are available.

#### Download a release binary

Pre-built binaries for common platforms are available on the [releases page](https://github.com/jdmallen/committy/releases). Download the appropriate archive for your platform, extract it, and use the contained
`committy` executable.

#### Publish a self-contained binary for all platforms

This is what the project uses to build the binaries for releases. Output
lands in
`dist/<runtime>/`:

```bash
./publish.sh          # builds linux-x64, win-x64, osx-x64, osx-arm64
```

#### Publish a self-contained binary for only

_your_ platforms

```bash
dotnet publish -c Release -r linux-x64 -o ./dist/linux-x64
```

### 2. Put

`committy` on your
`PATH`

Copy the built executable (
`dist/<runtime>/committy`, or
`committy.exe` on Windows) to a directory on your
`PATH`, e.g.:

```bash
sudo cp ./dist/linux-x64/committy /usr/local/bin/
```

### 3. Install the git hook

Once
`committy` is on your
`PATH`, it installs its own hook from any directory:

```bash
committy install-hook               # current repository
committy install-hook /path/to/repo # a specific repository
committy install-hook --global      # template for all future repositories
```

The installed hook is a thin

*

*trampoline
** that only calls
`committy prepare-commit-msg`;
all logic lives in the binary, so upgrading committy upgrades its behavior
everywhere and
a per-repo hook can't drift out of sync. The hook always exits 0, so it can
never block a
commit. On Windows, git runs the hook through its bundled bash, so the same
command works
from PowerShell, Command Prompt, and Git Bash.

>

*

*Upgrading
from
an
older
committy?
** Earlier versions installed a logic-heavy
> hook
> that can fall out of sync with a newer binary. Run
`committy repair-hooks` to
> replace
> stale hooks with the current trampoline:
>
> ```bash
> committy repair-hooks --global          # re-stamp the global template
> committy repair-hooks                    # repair the current repo
> committy repair-hooks --scan ~/code      # sweep every repo under a directory
> committy repair-hooks --scan ~/code --dry-run   # preview first
> ```
>
> It only replaces hooks it recognizes as committy-managed (any version); hooks
> you wrote
> yourself are reported and left untouched. Add
`--backup` to keep the previous
> hook as
>
`prepare-commit-msg.bak`.

*

*4.
Configure
a
provider
**

Run
`committy config` to choose a provider and store credentials. With no flags
it
prompts interactively; pass flags to script it:

```bash
# Interactive (prompts for provider, key, and any provider-specific fields)
committy config

# Azure OpenAI, non-interactive
committy config --provider azure \
  --api-key "your_api_key_here" \
  --endpoint "https://your-endpoint.openai.azure.com" \
  --deployment "your-deployment-name"

# Anthropic (Claude), non-interactive
committy config --provider anthropic --api-key "sk-ant-..."
```

By default this writes to your

*

*global
** git config (
`~/.gitconfig`); add
`--local` to
scope it to the current repository. committy reads these values at commit time,
and prints
exactly which keys it set. See [Configuration](#configuration) for the full key
list and
environment-variable overrides.

> For Azure, the deployment name often matches the model you selected (e.g.
`gpt-4.1-mini`,
> committy's default), but verify it in the Azure portal — it isn't guaranteed.

## Usage

With the hook installed and a provider configured, just commit:

```bash
git add .
git commit
```

Committy reads the staged diff and pre-fills your editor with a suggested commit
message
that you can edit or accept.

### Output modes

-

*

*Title +
body
** (default): one Conventional Commits message — a short title
plus a body
summarizing the change. Pre-filled into your commit message.

-

*

*Titles
only
** (
`--titles-only` /
`COMMITTY_TITLES_ONLY=1`): five title-only
suggestions,
inserted as comments at the top of the commit message; uncomment the one you
want.

To make titles-only the default, set
`COMMITTY_TITLES_ONLY=1` (accepted truthy
values:
`1`,
`true`,
`yes`,
`on`, case-insensitive) or run
`committy config --titles-only`, which
persists
`committy.titlesonly=true` in git config.

### Manual usage

You can also run committy directly, without the hook:

```bash
# Title + body from staged changes (default)
committy

# Five title-only suggestions
committy --titles-only

# Read the diff from stdin instead of calling git
git diff --cached | committy

# Read from stdin only, never fall back to calling git
git diff --cached | committy --no-git

# Copy the first suggestion to the clipboard
committy --clipboard

# One-shot titles-only mode
COMMITTY_TITLES_ONLY=1 committy
```

Settings come from your saved config, but any of them can be overridden
per-invocation
with a flag or environment variable:

```bash
# Override the configured provider for one run
committy --provider anthropic --api-key "sk-ant-..."

# Override Azure settings explicitly
committy --provider azure \
         --api-key "your_key" \
         --endpoint "https://your-endpoint.openai.azure.com" \
         --deployment "your-deployment"
```

### Subcommands

| Command                                 | Description                                                                                                                                    |
|-----------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------|
| `committy`                              | Generate a commit message from staged changes (or stdin) and print it.                                                                         |
| `committy config [--local]`             | Configure the provider and credentials (stored in git config). Prompts interactively, or takes flags.                                          |
| `committy prepare-commit-msg <file>`    | Git hook entry point; writes suggestions into the given commit message file. (Normally invoked by the hook, not by hand.)                      |
| `committy install-hook [repo]`          | Install the hook into a repository (defaults to the current directory).                                                                        |
| `committy install-hook --global`        | Install the hook as a global template for all future repositories.                                                                             |
| `committy repair-hooks [--scan <dir>]…` | Replace stale committy hooks with the current trampoline (current repo by default; `--scan` sweeps a tree; `--global` re-stamps the template). |

## Options

| Option          | Alias | Environment variable                         | Default                     | Description                                                                    |
|-----------------|-------|----------------------------------------------|-----------------------------|--------------------------------------------------------------------------------|
| `--provider`    | `-p`  | `COMMITTY_PROVIDER`                          | `azure`                     | LLM provider: `azure` or `anthropic`.                                          |
| `--api-key`     | `-k`  | `AZURE_OPENAI_API_KEY` / `ANTHROPIC_API_KEY_COMMITTY` | —                   | API key for the selected provider.                                             |
| `--endpoint`    | `-e`  | `AZURE_OPENAI_ENDPOINT_HOST`                 | —                           | Azure OpenAI endpoint host URL (domain only — omit everything after it).       |
| `--deployment`  | `-d`  | `AZURE_OPENAI_DEPLOYMENT`                    | `gpt-4.1-mini`              | Azure OpenAI deployment name.                                                  |
| `--model`       | `-m`  | `ANTHROPIC_MODEL`                            | `claude-haiku-4-5-20251001` | Anthropic model name.                                                          |
| `--titles-only` | `-t`  | `COMMITTY_TITLES_ONLY`                       | off                         | Generate five title-only suggestions instead of a single title + body message. |
| `--clipboard`   | `-c`  | —                                            | off                         | Copy the first suggestion to the clipboard.                                    |
| `--no-git`      | —     | —                                            | off                         | Rely solely on stdin; never call `git diff --cached` as a fallback.            |
| `--local`       | —     | —                                            | off                         | (`config` only) Write to the current repo's git config instead of global.      |
| `--global`      | `-g`  | —                                            | off                         | (`install-hook` / `repair-hooks`) Target the global hook template.             |
| `--scan <dir>`  | —     | —                                            | current repo                | (`repair-hooks` only) Recursively sweep a directory for repos; repeatable.     |
| `--dry-run`     | —     | —                                            | off                         | (`repair-hooks` only) Report what would change without modifying anything.     |
| `--backup`      | —     | —                                            | off                         | (`repair-hooks` only) Save the previous hook as `prepare-commit-msg.bak`.      |

For every setting, the precedence is

*

*flag →
environment
variable →
git
config →
default
**.

*

*Clipboard
support
** is optional: Linux needs
`xsel` or
`xclip`; macOS uses
built-in
`pbcopy`/
`pbpaste`; Windows uses the built-in clipboard APIs. If a clipboard
tool isn't
available, committy shows a one-time warning and continues normally.

## Configuration

`committy config` stores settings in

*

*git
config
** under the
`committy.*`
section, so the
repo-vs-global distinction is native and committy can read it immediately at
commit time
(no shell restart needed). Use
`--local` to scope to the current repository; the
default is
global (
`~/.gitconfig`).

| git config key              | Provider  | Purpose                               |
|-----------------------------|-----------|---------------------------------------|
| `committy.provider`         | —         | `azure` or `anthropic`                |
| `committy.titlesonly`       | —         | `true` to default to titles-only mode |
| `committy.azure.apikey`     | Azure     | API key                               |
| `committy.azure.endpoint`   | Azure     | Endpoint host URL                     |
| `committy.azure.deployment` | Azure     | Deployment name                       |
| `committy.anthropic.apikey` | Anthropic | API key                               |
| `committy.anthropic.model`  | Anthropic | Model name                            |

These are plain
`git config` keys, so you can also set them by hand — e.g.
`git config --global committy.provider anthropic`. Environment variables (the
names in the
Options table) override git config for a single invocation, which is handy in
CI.

>

*

*Note:
** API keys are stored in plaintext in your git config file (
`~/.gitconfig` or the
> repo's
`.git/config`). These files are not committed, but treat them like any
> other local
> secret.

### Troubleshooting

-

*

*"
Committy
not
found
in
PATH"
** — ensure the executable is on your
`PATH`;
test with
`committy --help`.

-

*

*"…
configuration
incomplete /
missing"
** — run
`committy config` to set up a
provider, or check the relevant environment variables.

-

*

*Hook
not
running
** — confirm you're in a git repo with staged changes (
`git diff --cached`); re-run
`committy install-hook` if needed.

-

*

*Old/odd
hook
behavior
after
upgrading
** — run
`committy repair-hooks` (add
`--scan <dir>` to sweep many repos) to replace stale hooks with the current
trampoline.

## LLM Usage Disclaimer

Committy sends your

*

*staged
diff
** to your configured LLM provider (Azure
OpenAI or
Anthropic) to generate commit message suggestions. Be mindful of what you stage:
secrets,
credentials, or proprietary code in a diff will be transmitted to that
provider's API.
Review your organization's data-handling policies before use.

Generative LLM tools were used to help modify tests and scripts during
development, but this
project was generally not "vibe coded."
