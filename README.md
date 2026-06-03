# Committy

> AI-powered git commit messages from your staged diff, via Azure OpenAI.

Committy is a cross-platform .NET 10 console app that reads your staged changes and
writes a Conventional Commits message for you — either a single title + body that it
pre-fills into your editor, or five title-only suggestions to pick from. It installs
as a `prepare-commit-msg` git hook, so suggestions appear automatically when you run
`git commit`.

<!-- shields -->
<!--
[![Build](https://img.shields.io/badge/build-passing-brightgreen)](#)
[![License](https://img.shields.io/badge/license-TBD-blue)](#)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](#)
-->

## Install

**Prerequisites**

- .NET 10 Runtime or SDK
- Git
- Azure OpenAI access: endpoint, API key, and a deployment name

**1. Build the executable**

Publish a self-contained binary for your platform (output lands in `dist/<runtime>/`):

```bash
./publish.sh          # builds linux-x64, win-x64, osx-x64, osx-arm64
```

Or build a single runtime directly:

```bash
dotnet publish -c Release -r linux-x64 -o ./dist/linux-x64
```

**2. Put `committy` on your `PATH`**

Copy the built executable (`dist/<runtime>/committy`, or `committy.exe` on Windows)
to a directory on your `PATH`, e.g.:

```bash
sudo cp ./dist/linux-x64/committy /usr/local/bin/
```

**3. Install the git hook**

Once `committy` is on your `PATH`, it installs its own hook from any directory:

```bash
committy install-hook               # current repository
committy install-hook /path/to/repo # a specific repository
committy install-hook --global      # template for all future repositories
```

The installed hook is a thin **trampoline** that only calls `committy prepare-commit-msg`;
all logic lives in the binary, so upgrading committy upgrades its behavior everywhere and
a per-repo hook can't drift out of sync. The hook always exits 0, so it can never block a
commit. On Windows, git runs the hook through its bundled bash, so the same command works
from PowerShell, Command Prompt, and Git Bash.

**4. Set environment variables**

```bash
export AZURE_OPENAI_API_KEY="your_api_key_here"
export AZURE_OPENAI_ENDPOINT_HOST="https://your-endpoint.openai.azure.com"
export AZURE_OPENAI_DEPLOYMENT="your-deployment-name"
```

Add these to your shell profile (`~/.bashrc`, `~/.zshrc`, `~/.bash_profile`) to persist
them, or use a tool like [`direnv`](https://direnv.net/) to load them per project. On
Windows PowerShell, use `$env:AZURE_OPENAI_API_KEY="..."` for the session or
`[Environment]::SetEnvironmentVariable('AZURE_OPENAI_API_KEY', '...', 'User')` to persist.

> The deployment name often matches the model you selected in Azure (e.g. `gpt-4.1-mini`,
> Committy's default), but verify it in the Azure portal — it isn't guaranteed.

## Usage

With the hook installed and environment variables set, just commit:

```bash
git add .
git commit
```

Committy reads the staged diff and pre-fills your editor with a suggested commit message
that you can edit or accept.

### Output modes

- **Title + body** (default): one Conventional Commits message — a short title plus a body
  summarizing the change. Pre-filled into your commit message.
- **Titles only** (`--titles-only` / `COMMITTY_TITLES_ONLY=1`): five title-only suggestions,
  inserted as comments at the top of the commit message; uncomment the one you want.

Set `COMMITTY_TITLES_ONLY=1` to make titles-only the default for both the CLI and the hook.
Accepted truthy values: `1`, `true`, `yes`, `on` (case-insensitive).

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

Configuration can come from environment variables (above) or be passed explicitly:

```bash
committy --api-key "your_key" \
         --endpoint "https://your-endpoint.openai.azure.com" \
         --deployment "your-deployment"
```

### Subcommands

| Command | Description |
| --- | --- |
| `committy` | Generate a commit message from staged changes (or stdin) and print it. |
| `committy prepare-commit-msg <file>` | Git hook entry point; writes suggestions into the given commit message file. (Normally invoked by the hook, not by hand.) |
| `committy install-hook [repo]` | Install the hook into a repository (defaults to the current directory). |
| `committy install-hook --global` | Install the hook as a global template for all future repositories. |

## Options

| Option | Alias | Environment variable | Default | Description |
| --- | --- | --- | --- | --- |
| `--api-key` | `-k` | `AZURE_OPENAI_API_KEY` | — | Azure OpenAI API key. |
| `--endpoint` | `-e` | `AZURE_OPENAI_ENDPOINT_HOST` | — | Azure OpenAI endpoint host URL (domain only — omit everything after it). |
| `--deployment` | `-d` | `AZURE_OPENAI_DEPLOYMENT` | `gpt-4.1-mini` | Azure OpenAI deployment name. Precedence: flag → env var → default. |
| `--titles-only` | `-t` | `COMMITTY_TITLES_ONLY` | off | Generate five title-only suggestions instead of a single title + body message. |
| `--clipboard` | `-c` | — | off | Copy the first suggestion to the clipboard. |
| `--no-git` | — | — | off | Rely solely on stdin; never call `git diff --cached` as a fallback. |
| `--global` | `-g` | — | off | (`install-hook` only) Install as a global hook template. |

**Clipboard support** is optional: Linux needs `xsel` or `xclip`; macOS uses built-in
`pbcopy`/`pbpaste`; Windows uses the built-in clipboard APIs. If a clipboard tool isn't
available, committy shows a one-time warning and continues normally.

### Troubleshooting

- **"Committy not found in PATH"** — ensure the executable is on your `PATH`; test with `committy --help`.
- **"Azure OpenAI configuration incomplete"** — confirm `AZURE_OPENAI_API_KEY` and `AZURE_OPENAI_ENDPOINT_HOST` are set (deployment has a default) and check for typos.
- **Hook not running** — confirm you're in a git repo with staged changes (`git diff --cached`); re-run `committy install-hook` if needed.

## LLM Usage Disclaimer

Committy sends your **staged diff** to Azure OpenAI to generate commit message suggestions.
Be mindful of what you stage: secrets, credentials, or proprietary code in a diff will be
transmitted to your configured Azure OpenAI endpoint. Review your organization's data-handling
policies before use.

Generative LLM tools were used to help modify tests and scripts during development, but this
project was generally not "vibe coded."
