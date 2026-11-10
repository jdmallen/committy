# Committy

> AI-powered git commit messages from your staged diff, via Azure OpenAI or
> Anthropic (Claude).

Committy is a cross-platform .NET 10 console app that reads your staged changes
and writes a Conventional Commits message for you — either a single title + body
that it pre-fills into your editor, or five title-only suggestions to pick from.
It installs as a `prepare-commit-msg` git hook, so suggestions appear
automatically when you run `git commit`.

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
- Access to one supported LLM provider:
  - **Azure OpenAI** — endpoint, API key, and a deployment name,
  - **Anthropic (Claude)** — an API key, or
  - **OpenAI-compatible** — a base URL and model name; covers openai.com and
    self-hosted runners (llama.cpp, llama-swap, Ollama, vLLM, LM Studio). An
    API key is optional, since self-hosted servers usually accept anonymous
    requests.

**1. Build the executable**

Publish a self-contained binary for your platform (output lands in
`dist/<runtime>/`):

```bash
./scripts/publish.sh          # builds linux-x64, win-x64, osx-x64, osx-arm64
```

Or build a single runtime directly:

```bash
dotnet publish -c Release -r linux-x64 -o ./dist/linux-x64
```

**2. Put `committy` on your `PATH`**

Copy the built executable (`dist/<runtime>/committy`, or `committy.exe` on
Windows) to a directory on your `PATH`, e.g.:

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

The installed hook is a thin **trampoline** that only calls
`committy prepare-commit-msg`; all logic lives in the binary, so upgrading
committy upgrades its behavior everywhere and a per-repo hook can't drift out of
sync. The hook always exits 0, so it can never block a commit. On Windows, git
runs the hook through its bundled bash, so the same command works from
PowerShell, Command Prompt, and Git Bash.

> **Upgrading from an older committy?** Earlier versions installed a logic-heavy
> hook that can fall out of sync with a newer binary. Run
> `committy repair-hooks` to replace stale committy-managed hooks with the
> current trampoline:
>
> ```bash
> committy repair-hooks                          # repair the current repo
> committy repair-hooks --global                 # re-stamp the global template
> committy repair-hooks --scan ~/code            # sweep every repo under a directory
> committy repair-hooks --scan ~/code --dry-run  # preview without changing anything
> committy repair-hooks --git-dir ~/.cfg         # a headless repo (e.g. dotfiles)
> ```
>
> Headless repositories — a separate git directory with no in-tree `.git`, such
> as a `~/.cfg` dotfiles repo driven by `git --git-dir=~/.cfg --work-tree=~` —
> are intentionally skipped by `--scan`. Point committy straight at the git
> directory with `--git-dir <dir>` (repeatable) to install
> (`committy install-hook ~/.cfg`) or repair their hooks.
>
> Only hooks it recognizes as committy-managed are touched; hooks you wrote
> yourself are reported and left untouched. Add `--backup` to keep the previous
> hook as `prepare-commit-msg.bak`.

**4. Configure a provider**

Run `committy config` to choose a provider and its settings. With no flags it
prompts interactively; pass flags to script it:

```bash
# Interactive (prompts for provider and any provider-specific fields)
committy config

# Azure OpenAI, non-interactive
committy config --provider azure \
  --endpoint "https://your-endpoint.openai.azure.com" \
  --deployment "your-deployment-name"

# Azure OpenAI without an API key (Microsoft Entra ID via a signed-in az)
committy config --provider azure --azure-auth azurecli \
  --endpoint "https://your-endpoint.openai.azure.com" \
  --deployment "your-deployment-name"

# Azure OpenAI without an API key or az (signs in through your browser)
committy config --provider azure --azure-auth azureidentity \
  --endpoint "https://your-endpoint.openai.azure.com" \
  --deployment "your-deployment-name"

# Anthropic (Claude), non-interactive
committy config --provider anthropic

# Local / OpenAI-compatible, non-interactive (--endpoint is the base URL)
committy config --provider openai \
  --endpoint "http://10.10.0.20:8080/v1" \
  --model "thinkingcap-27b-q4km"
```

By default this writes to your **global** git config (`~/.gitconfig`); add
`--local` to scope it to the current repository. committy reads these values at
commit time, and prints exactly which keys it set. See
[Configuration](#configuration) for the full key list and environment-variable
fallbacks.

**5. Set your API key**

(Using `--azure-auth azurecli` or `azureidentity`? There is no key:
`committy config` checks your `az` sign-in, or signs you in, instead. See
[Keyless sign-in (Microsoft Entra ID)](#keyless-sign-in-microsoft-entra-id).)

API keys live in environment variables, not git config:

| Provider  | Variable                     |
|-----------|------------------------------|
| Azure     | `AZURE_OPENAI_API_KEY`       |
| Anthropic | `ANTHROPIC_API_KEY_COMMITTY` |
| OpenAI    | `OPENAI_API_KEY_COMMITTY` (optional) |

`committy config` finishes by saying whether the variable is set, never showing
its value:

- **Windows:** it offers to save the key as a user environment variable, typed
  with `*` shown. `--api-key` saves it without asking. It is written after the
  git config, and only terminals opened afterwards see it.
- **Linux and macOS:** it prints the line to add to your shell's startup file
  (`~/.zshrc`, `~/.bashrc`, fish's `set -Ux`, and so on, from `$SHELL`). It
  never edits startup files, and `--api-key` is not saved.

> **Commits from desktop apps:** committy runs from git's commit hook, so it
> also runs when a git GUI or IDE commits. Those apps may not load your shell's
> startup file, and so may see neither the key nor committy on `PATH`. VS Code
> and JetBrains IDEs usually copy the shell's environment at startup; plain git
> GUIs may not. On Linux with a systemd desktop session (e.g. GNOME), also put
> `AZURE_OPENAI_API_KEY=<your-key>` (or your provider's variable) in
> `~/.config/environment.d/committy.conf`, `chmod 600` it, and log in again.
> Windows user variables reach desktop apps already.

> For Azure, the deployment name often matches the model you selected (e.g.
> `gpt-4.1-mini`, committy's default), but verify it in the Azure portal — it
> isn't guaranteed.

## Usage

With the hook installed and a provider configured, just commit:

```bash
git add .
git commit
```

Committy reads the staged diff and pre-fills your editor with a suggested commit
message that you can edit or accept.

### Output modes

- **Title + body** (default): one Conventional Commits message — a short title
  plus a body summarizing the change. Pre-filled into your commit message.
- **Titles only** (`--titles-only` / `COMMITTY_TITLES_ONLY=1`): five title-only
  suggestions, inserted as comments at the top of the commit message; uncomment
  the one you want.

To make titles-only the default, set `COMMITTY_TITLES_ONLY=1` (accepted truthy
values: `1`, `true`, `yes`, `on`, case-insensitive) or run
`committy config --titles-only`, which persists `committy.titlesonly=true` in
git config.

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
per-invocation with a flag or environment variable:

```bash
# Override the configured provider for one run
committy --provider anthropic --api-key "sk-ant-..."

# Use a local model for one run, without touching git config
committy --provider openai \
         --endpoint "http://10.10.0.20:8080/v1" \
         --model "thinkingcap-27b-q4km"

# Override Azure settings explicitly
committy --provider azure \
         --api-key "your_key" \
         --endpoint "https://your-endpoint.openai.azure.com" \
         --deployment "your-deployment"
```

### Token usage

When the provider reports token usage, the hook adds a comment at the bottom
of the commit message (above the diff, with `git commit --verbose`):

```text
# Committy: 1,234 tokens (1,200 in, 34 out)
```

Git drops comment lines, so it never ends up in the commit. Every run, from the
hook or by hand, is added to lifetime totals in `usage.json`
(`~/.local/share/committy/` on Linux, `%LOCALAPPDATA%\committy\` on Windows,
`~/Library/Application Support/committy/` on macOS). Runs whose provider
reported no usage are not counted. Delete `usage.json` to start over.

### Checking your configuration

`committy --info` shows the settings committy would use if run in the current
directory, where each one comes from, and the lifetime token totals. Inside a
repository with its own `committy config --local` settings, those are what it
shows. API keys are reported as set or not set, never shown.

```text
committy 1.5.0

  repository:  /home/me/git/project
  provider:    openai (git config --local)
  base url:    http://10.10.0.20:8080/v1 (git config --local)
  model:       thinkingcap-27b-q4km (git config --global)
  timeout:     300s (default)
  max tokens:  2,048 (default)
  api key:     not set (optional)
  titles only: no (default)

  usage log:   /home/me/.local/share/committy/usage.json
  total:       12,345 tokens (10,000 in, 2,345 out) over 42 runs since 2026-11-06
  average:     294 tokens per run (238 in, 56 out)
```

Sources are a command-line flag, `git config --local` / `--global` /
`--system`, an environment variable (shown as `$NAME`), or `default`. Flags
given with `--info`, such as `--provider`, are applied the same way as for a
normal run.

### Subcommands

| Command                                 | Description                                                                                                                                     |
|-----------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------|
| `committy`                              | Generate a commit message from staged changes (or stdin) and print it.                                                                         |
| `committy config [--local]`             | Configure the provider and credentials (stored in git config). Prompts interactively, or takes flags.                                          |
| `committy prepare-commit-msg <file>`    | Git hook entry point; writes suggestions into the given commit message file. (Normally invoked by the hook, not by hand.)                      |
| `committy install-hook [repo]`          | Install the hook into a repository (defaults to the current directory).                                                                        |
| `committy install-hook --global`        | Install the hook as a global template for all future repositories.                                                                             |
| `committy repair-hooks [--scan <dir>]…` | Replace stale committy hooks with the current trampoline (current repo by default; `--scan` sweeps a tree; `--git-dir` targets a headless repo; `--global` re-stamps the template). |

## Options

| Option          | Alias | Environment variable                                  | Default                     | Description                                                                     |
|-----------------|-------|-------------------------------------------------------|-----------------------------|---------------------------------------------------------------------------------|
| `--provider`    | `-p`  | `COMMITTY_PROVIDER`                                   | `azure`                     | LLM provider: `azure`, `anthropic`, or `openai` (alias `local`).                |
| `--api-key`     | `-k`  | `AZURE_OPENAI_API_KEY` / `ANTHROPIC_API_KEY_COMMITTY` / `OPENAI_API_KEY_COMMITTY` | —       | API key for one run; with `config`, saved as a user env var (Windows only).     |
| `--endpoint`    | `-e`  | `AZURE_OPENAI_ENDPOINT_HOST` / `OPENAI_BASE_URL`      | —                           | Azure endpoint host URL (domain only), or the `openai` base URL including `/v1`. |
| `--deployment`  | `-d`  | `AZURE_OPENAI_DEPLOYMENT`                             | `gpt-4.1-mini`              | Azure OpenAI deployment name.                                                   |
| `--model`       | `-m`  | `ANTHROPIC_MODEL` / `OPENAI_MODEL`                    | `claude-haiku-4-5-20251001` | Anthropic or `openai` model name.                                               |
| `--azure-auth`  |       |                                                       | `apikey`                    | Azure auth: `apikey`, `azurecli` (Entra ID via the Azure CLI), or `azureidentity` (Entra ID via a browser sign-in). |
| `--tenant`      |       |                                                       | az's current tenant         | Entra tenant ID or domain for `azurecli` or `azureidentity`; with `config`, `-` clears it. |
| `--credential`  |       |                                                       | `interactivebrowser`        | Sign-in for `azureidentity`: `interactivebrowser`, `devicecode`, `default`, `azurecli` or `azd`. |
| `--titles-only` | `-t`  | `COMMITTY_TITLES_ONLY`                                | off                         | Generate five title-only suggestions instead of a single title + body message. |
| `--clipboard`   | `-c`  | —                                                     | off                         | Copy the first suggestion to the clipboard.                                     |
| `--no-git`      | —     | —                                                     | off                         | Rely solely on stdin; never call `git diff --cached` as a fallback.             |
| `--info`        |       |                                                       | off                         | Show the configuration in effect here, its sources, and lifetime token usage.  |
| `--local`       | —     | —                                                     | off                         | (`config` only) Write to the current repo's git config instead of global.       |
| `--global`      | `-g`  | —                                                     | off                         | (`install-hook` / `repair-hooks`) Target the global hook template.              |
| `--scan <dir>`  | —     | —                                                     | current repo                | (`repair-hooks` only) Recursively sweep a directory for repos; repeatable.      |
| `--git-dir <dir>` | —   | —                                                     | current repo                | (`repair-hooks` only) Target a headless git directory directly (e.g. `~/.cfg`); repeatable. |
| `--dry-run`     | —     | —                                                     | off                         | (`repair-hooks` only) Report what would change without modifying anything.      |
| `--backup`      | —     | —                                                     | off                         | (`repair-hooks` only) Save the previous hook as `prepare-commit-msg.bak`.       |

Settings are taken from the **flag, then git config, then the environment
variable, then the default**. API keys are the exception: **flag, then
environment variable, then git config**. See
[Configuration](#configuration) for details, or run `committy --info` to see
which source each setting came from.

**Clipboard support** is optional: Linux needs `xsel` or `xclip`; macOS uses
built-in `pbcopy`/`pbpaste`; Windows uses the built-in clipboard APIs. If a
clipboard tool isn't available, committy shows a one-time warning and continues
normally.

## Configuration

`committy config` stores settings in **git config** under the `committy.*`
section, so the repo-vs-global distinction is native and committy can read it
immediately at commit time (no shell restart needed). Use `--local` to scope to
the current repository; the default is global (`~/.gitconfig`).

| git config key              | Provider  | Purpose                               |
|-----------------------------|-----------|---------------------------------------|
| `committy.provider`             | —         | `azure`, `anthropic`, or `openai`         |
| `committy.titlesonly`           | —         | `true` to default to titles-only mode     |
| `committy.azure.apikey`         | Azure     | API key; legacy, use `AZURE_OPENAI_API_KEY` |
| `committy.azure.endpoint`       | Azure     | Endpoint host URL                         |
| `committy.azure.deployment`     | Azure     | Deployment name                           |
| `committy.azure.reasoningmodel` | Azure     | `true` for a reasoning deployment         |
| `committy.azure.maxtokens`      | Azure     | Generation budget; see below              |
| `committy.azure.auth`           | Azure     | `apikey` (default), `azurecli`, or `azureidentity` |
| `committy.azure.credential`     | Azure     | Sign-in for `azureidentity`; see below    |
| `committy.azure.tenant`         | Azure     | Entra tenant; optional                    |
| `committy.azure.scope`          | Azure     | Token scope; optional                     |
| `committy.azure.azpath`         | Azure     | Full path to `az`, if not on the hook's `PATH` |
| `committy.anthropic.apikey`     | Anthropic | API key; legacy, use the env var          |
| `committy.anthropic.model`      | Anthropic | Model name                                |
| `committy.openai.baseurl`       | OpenAI    | Base URL including `/v1`                  |
| `committy.openai.model`         | OpenAI    | Model name                                |
| `committy.openai.apikey`        | OpenAI    | API key; legacy, use the env var          |
| `committy.openai.timeoutseconds`| OpenAI    | Request timeout; default `300`            |
| `committy.openai.maxtokens`     | OpenAI    | Generation budget; default `2048`         |

These are plain `git config` keys, so you can also set them by hand — e.g.
`git config --global committy.provider anthropic`.

Precedence, highest first:

1. **Settings:** CLI flag, then git config, then environment variable (the
   names in the Options table), then the built-in default. A leftover shell
   export never overrides what `committy config` wrote; use a flag for a
   one-off change.
2. **API keys:** CLI flag, then environment variable, then git config. The
   git config keys are only read for configs written by committy 1.3.0 or
   earlier; `committy config` no longer writes them.
3. **Azure auth settings** (`auth`, `tenant`, `scope`, `azpath`): CLI flag
   (for `auth` and `tenant`), then git config. They have no environment
   variable, so a stray export cannot switch a working key setup over.

### Azure reasoning deployments

Reasoning models (o-series, gpt-5 and later) reject `temperature`, and spend
their hidden reasoning from the same token budget as the answer. For such a
deployment, set `committy.azure.reasoningmodel` to `true` (`committy config`
asks for it):

```sh
git config --global committy.azure.reasoningmodel true
```

committy then leaves out `temperature` and raises the budget from the built-in
100 (titles) or 500 (title + body) to `2048`. Set `committy.azure.maxtokens` to
use a different budget, for a reasoning deployment or not. If a reasoning model
returns an empty message, raise it.

### Keyless sign-in (Microsoft Entra ID)

If your Azure OpenAI resource does not allow API keys, committy can use the
Azure CLI's sign-in instead. Each request then carries an Entra ID bearer
token from `az account get-access-token`, and no key is read or sent.

1. Install the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
   (2.54 or later).
2. Have an admin grant your account the **Cognitive Services OpenAI User**
   role on the resource (or its resource group). It can take a few minutes to
   apply.
3. Sign in for the token's scope:

   ```sh
   az login --scope https://ai.azure.com/.default
   ```

4. Point committy at it:

   ```sh
   committy config --provider azure --azure-auth azurecli \
     --endpoint "https://your-resource.openai.azure.com" \
     --deployment "your-deployment-name"
   ```

   Instead of the API key step, `committy config` shows which `az` it found
   and who is signed in, or the `az login` command to run.

The endpoint stays host only; both `https://<name>.openai.azure.com` and
`https://<name>.services.ai.azure.com` work.

Optional settings:

- **`committy.azure.tenant`** (`--tenant`): the tenant to get tokens from,
  when your account is in more than one. Unset, `az` uses its current tenant.
- **`committy.azure.scope`**: the token scope. The default,
  `https://ai.azure.com/.default`, works for Foundry and Azure OpenAI
  resources; set `https://cognitiveservices.azure.com/.default` if your
  resource requires it.
- **`committy.azure.azpath`**: the full path to `az`. The commit hook runs
  with the `PATH` of whatever started git, and a desktop git client may not
  include `az` (common with Homebrew and on Windows). `committy --info` shows
  the `az` that will be used.

To use a different method for one run, pass `--azure-auth apikey` (or
`azurecli`, or `azureidentity`).

#### Without the Azure CLI (`azureidentity`)

`azureidentity` signs in through your browser instead, with no `az` needed.
`committy config` does the sign-in once; committy then remembers it, so commit
hooks run silently. Steps 1 and 2 above (the role, and the endpoint) still
apply.

```sh
committy config --provider azure --azure-auth azureidentity \
  --endpoint "https://your-resource.openai.azure.com" \
  --deployment "your-deployment-name"
```

`committy config` asks which sign-in to use (`committy.azure.credential`,
`--credential`):

| Value                          | Signs in with                                                      |
|--------------------------------|--------------------------------------------------------------------|
| `interactivebrowser` (default) | A browser window.                                                  |
| `devicecode`                   | A code you enter at a sign-in page on any device (no local browser needed). |
| `default`                      | Azure.Identity's `DefaultAzureCredential`: environment variables, managed identity, then developer tools. |
| `azurecli` / `azd`             | The signed-in Azure CLI or Azure Developer CLI.                    |

Then it asks for the tenant. **A personal Microsoft account (outlook.com, or a
gmail address added to a tenant) needs one**, or sign-in fails with "Selected
user account does not exist in tenant 'Microsoft Services'". If `az` is
signed in, its tenant is offered as the default.

Where things are kept:

- **Tokens:** in an encrypted cache in your system keyring (libsecret on
  Linux, Keychain on macOS, DPAPI on Windows), never in plain text. Without a
  keyring (over SSH, or in a container), use `azurecli` instead.
- **The account and tenant ids** (no tokens): `~/.config/committy/auth-record.json`
  (`%APPDATA%\committy\auth-record.json` on Windows).

A commit hook never opens a browser. If the sign-in is missing or has expired,
the hook adds a comment to the commit message saying to run `committy config`,
and the commit goes ahead. `committy --info` shows whether a sign-in is saved.

### Local and other OpenAI-compatible models

The `openai` provider (alias `local`) works with anything that speaks the OpenAI
`/v1/chat/completions` API. Two defaults differ from the hosted providers, both
because of how self-hosted models behave:

- **`timeoutseconds` defaults to `300`**, not 30. A cold model loads its weights
  from disk before generating a single token, which can take minutes.
- **`maxtokens` defaults to `2048`**, replacing the built-in 100 (titles) and
  500 (title + body) budgets. A reasoning model spends most of its output on a
  thinking block that committy discards, so a small budget truncates the
  thought and leaves no message behind.

Reasoning models are supported: the thinking block is stripped whether the
server splits it into `reasoning_content` or inlines it as `<think>…</think>`.

> **Quality note:** output quality tracks the model you point at. A small
> quantized model generally follows the prompt less reliably than the hosted
> providers, so review the message before committing.

> **Note:** committy 1.3.0 and earlier stored API keys in plaintext in git
> config, and committy still reads one when the environment variable is unset.
> That fallback also keeps desktop-app commits working (see step 5, "Set your
> API key", under [Install](#install)). If you only commit from terminals,
> remove it once the variable is set, e.g.
> `git config --global --unset committy.azure.apikey`.

### Troubleshooting

- **"Committy not found in PATH"** — ensure the executable is on your `PATH`;
  test with `committy --help`.
- **Azure `400` mentioning `temperature` or `max_tokens`** (e.g. "Unsupported
  parameter") means a reasoning deployment: set
  `committy.azure.reasoningmodel true` (see
  [Azure reasoning deployments](#azure-reasoning-deployments)).
- **"… configuration incomplete / missing"** — run `committy config` to set up a
  provider, or check the relevant environment variables.
- **Azure `401` with `azurecli`:** the token was refused. Check that the
  endpoint is your resource's own subdomain (not a regional endpoint), that
  you signed in for the right scope and tenant (`committy --info` shows the
  `az login` command), and give a newly granted role a few minutes to apply.
- **Azure `403` with `azurecli`:** the token is valid but lacks access. Your
  account needs the **Cognitive Services OpenAI User** role on the resource.
- **"not signed in to Azure"** in the commit message with `azureidentity`:
  run `committy config` in a terminal to sign in again.
- **"The persistent token cache needs the system keyring"** with
  `azureidentity`: no keyring is available (common over SSH). Switch to
  `--azure-auth azurecli`.
- **"Azure CLI ('az') not found on PATH"** in a commit made from a desktop
  app: set `committy.azure.azpath` to the output of `command -v az` (or
  `where az` on Windows).
- **Hook not running** — confirm you're in a git repo with staged changes
  (`git diff --cached`); re-run `committy install-hook` if needed.
- **Old/odd hook behavior after upgrading** — run `committy repair-hooks` (add
  `--scan <dir>` to sweep many repos) to replace stale hooks with the current
  trampoline.

## LLM Usage Disclaimer

Committy sends your **staged diff** to your configured LLM provider (Azure
OpenAI, Anthropic, or local) to generate commit message suggestions. Be mindful
of what you stage: secrets, credentials, or proprietary code in a diff will be
transmitted to that provider's API. Review your organization's data-handling
policies before use.

Generative LLM tools were used to help develop this project; mostly Claude
Opus 5.5. That said, every line of code, commit message, and documentation was
reviewed and edited by a human (yours truly) before being committed to the
repository.
