# Committy
## Get Started

Committy is a cross-platform .NET 10 console application that generates AI-powered commit messages using Azure OpenAI.

### Prerequisites

- .NET 10 Runtime or SDK
- Git
- Azure OpenAI API access with endpoint, API key, and deployment name

### Platform-Specific Installation

#### Linux / macOS / Git Bash (Windows)

1. **Install Git Hook (Bash script):**
   ```bash
   # Local installation (current repository)
   ./install-git-hook.sh
   
   # Global installation (all future repositories)
   ./install-git-hook.sh --global
   ```

2. **Set Environment Variables:**
   ```bash
   export AZURE_OPENAI_API_KEY="your_api_key_here"
   export AZURE_OPENAI_ENDPOINT_HOST="https://your-endpoint.openai.azure.com"
   export AZURE_OPENAI_DEPLOYMENT="your-deployment-name"
   ```
   When you generate the deployment in Azure, it defaults to the name of the model you selected (e.g., `gpt-4.1-mini`, Committy's default value). However, this is not guaranteed, so please verify the deployment name in the Azure portal.

3. **Make environment variables persistent:**
   
   **Linux/WSL:**
   ```bash
   echo 'export AZURE_OPENAI_API_KEY="your_api_key_here"' >> ~/.bashrc
   echo 'export AZURE_OPENAI_ENDPOINT_HOST="https://your-endpoint.openai.azure.com"' >> ~/.bashrc
   echo 'export AZURE_OPENAI_DEPLOYMENT="your-deployment-name"' >> ~/.bashrc
   source ~/.bashrc
   ```
   
   **macOS (Bash):**
   ```bash
   echo 'export AZURE_OPENAI_API_KEY="your_api_key_here"' >> ~/.bash_profile
   echo 'export AZURE_OPENAI_ENDPOINT_HOST="https://your-endpoint.openai.azure.com"' >> ~/.bash_profile
   echo 'export AZURE_OPENAI_DEPLOYMENT="your-deployment-name"' >> ~/.bash_profile
   source ~/.bash_profile
   ```
   
   **macOS (Zsh - default shell):**
   ```zsh
   echo 'export AZURE_OPENAI_API_KEY="your_api_key_here"' >> ~/.zshrc
   echo 'export AZURE_OPENAI_ENDPOINT_HOST="https://your-endpoint.openai.azure.com"' >> ~/.zshrc
   echo 'export AZURE_OPENAI_DEPLOYMENT="your-deployment-name"' >> ~/.zshrc
   source ~/.zshrc
   ```
   You might consider using a tool like `direnv` to automatically load them per project.
#### Windows (PowerShell / Command Prompt)

1. **Install Git Hook (PowerShell script):**
   ```powershell
   # Local installation (current repository)
   .\install-git-hook.ps1
   
   # Global installation (all future repositories)
   .\install-git-hook.ps1 -Global
   ```

2. **Set Environment Variables (Current Session):**
   ```powershell
   $env:AZURE_OPENAI_API_KEY="your_api_key_here"
   $env:AZURE_OPENAI_ENDPOINT_HOST="https://your-endpoint.openai.azure.com"
   $env:AZURE_OPENAI_DEPLOYMENT="your-deployment-name"
   ```

3. **Make environment variables persistent:**
   ```powershell
   [Environment]::SetEnvironmentVariable('AZURE_OPENAI_API_KEY', 'your_api_key_here', 'User')
   [Environment]::SetEnvironmentVariable('AZURE_OPENAI_ENDPOINT_HOST', 'https://your-endpoint.openai.azure.com', 'User')
   [Environment]::SetEnvironmentVariable('AZURE_OPENAI_DEPLOYMENT', 'your-deployment-name', 'User')
   ```

## Manual Usage

If you prefer not to use git hooks, you can run committy manually:

#### Generate suggestions from staged changes:
```bash
committy --api-key "your_key" --endpoint "https://your-endpoint.openai.azure.com" --deployment "your-deployment"
```

#### Generate suggestions from stdin:
```bash
git diff --cached | committy --api-key "your_key" --endpoint "https://your-endpoint.openai.azure.com" --deployment "your-deployment"
```

#### Generate suggestions from stdin and force committy to NOT call git, even on failure:
```bash
git diff --cached | committy --no-git --api-key "your_key" --endpoint "https://your-endpoint.openai.azure.com" --deployment "your-deployment"
```

#### Copy first suggestion to clipboard:
```bash
committy --clipboard --api-key "your_key" --endpoint "https://your-endpoint.openai.azure.com" --deployment "your-deployment"
```

#### Use environment variables:
```bash
# After setting environment variables
committy
git diff --cached | committy
committy --clipboard
```

## Clipboard Support

Committy includes optional clipboard functionality:
- **Linux**: Requires `xsel` or `xclip` packages
- **macOS**: Uses built-in `pbcopy`/`pbpaste`
- **Windows**: Uses built-in clipboard APIs

If clipboard tools aren't available, committy will show a one-time warning but continue to function normally.

### Testing Installation

1. Stage some changes:
   ```bash
   git add .
   ```

2. Start a commit:
   ```bash
   git commit
   ```

3. You should see AI-generated suggestions as comments in your commit message editor.

## Troubleshooting

### Common Issues:

1. **"Committy not found in PATH"**
   - Ensure the committy executable is in your PATH
   - Try running `committy --help` to test from your test repo

2. **"Azure OpenAI configuration incomplete"**
   - Verify all three environment variables are set (though `AZURE_OPENAI_DEPLOYMENT` has a default value)
   - Check for typos in variable names

3. **Git hook not executing**
   - Ensure hook file is executable: `chmod +x .git/hooks/prepare-commit-msg`
   - Check if you're in a git repository
   - Verify you have staged changes: `git diff --cached`

4. **Clipboard warnings**
   - Install clipboard tools on Linux: e.g. for Debian-based systems, `sudo apt install xsel` or `sudo apt install xclip`
   - These warnings don't affect core functionality

### Platform-Specific Notes:

- **Windows Git Bash**: Use the `.sh` script, not `.ps1`
- **Windows PowerShell**: Use the `.ps1` script
- **WSL**: Treat as Linux, use the `.sh` script
- **macOS**: Works with both Bash and Zsh shells. I guess you can use PowerShell if you _really_ want to.