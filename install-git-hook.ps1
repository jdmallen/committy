# Install committy as a git hook (PowerShell version)
# Usage: .\install-git-hook.ps1 [-Global]

param(
    [switch]$Global
)

$HOOK_TYPE = "prepare-commit-msg"
$HOOK_SOURCE = Join-Path (Get-Location) "hooks" $HOOK_TYPE

# Verify hook source file exists
if (-not (Test-Path $HOOK_SOURCE)) {
    Write-Error "Error: Hook file not found at $HOOK_SOURCE"
    exit 1
}

if ($Global) {
    Write-Host "Installing global git hook template..."
    
    # Create global hooks directory
    $HOOKS_DIR = Join-Path $env:USERPROFILE ".git-templates\hooks"
    New-Item -ItemType Directory -Path $HOOKS_DIR -Force | Out-Null
    
    # Set global template directory
    git config --global init.templateDir (Join-Path $env:USERPROFILE ".git-templates")
} else {
    Write-Host "Installing local git hook..."
    
    # Check if we're in a git repository
    try {
        $gitDir = git rev-parse --git-dir 2>$null
        if ($LASTEXITCODE -ne 0) {
            throw "Not in git repository"
        }
        $HOOKS_DIR = Join-Path $gitDir "hooks"
    }
    catch {
        Write-Error "Error: Not in a git repository. Run from repo root or use -Global flag."
        exit 1
    }
}

New-Item -ItemType Directory -Path $HOOKS_DIR -Force | Out-Null
$HOOK_FILE = Join-Path $HOOKS_DIR $HOOK_TYPE

# Copy the hook file
Copy-Item -Path $HOOK_SOURCE -Destination $HOOK_FILE -Force

# Make hook executable (works with Git Bash on Windows)
if (Get-Command "git" -ErrorAction SilentlyContinue) {
    git update-index --chmod=+x $HOOK_FILE 2>$null
}

Write-Host "✓ Hook installed: $HOOK_FILE"

if ($Global) {
    Write-Host ""
    Write-Host "Global hook template installed. New repositories will automatically"
    Write-Host "include this hook. For existing repos, run:"
    Write-Host "  git init  # in each existing repository"
    Write-Host ""
    Write-Host "Or install locally in existing repos:"
    Write-Host "  .\install-git-hook.ps1"
} else {
    Write-Host ""
    Write-Host "Hook installed for current repository."
    Write-Host "Now when you run 'git commit', AI suggestions will appear in your editor."
}

Write-Host ""
Write-Host "Make sure to set your Azure OpenAI configuration:"
Write-Host "  `$env:AZURE_OPENAI_API_KEY='your_api_key_here'"
Write-Host "  `$env:AZURE_OPENAI_ENDPOINT_HOST='https://your-endpoint.openai.azure.com'"
Write-Host "  `$env:AZURE_OPENAI_DEPLOYMENT='your-deployment-name'"
Write-Host ""
Write-Host "Or for persistent environment variables, use:"
Write-Host "  [Environment]::SetEnvironmentVariable('AZURE_OPENAI_API_KEY', 'your_api_key_here', 'User')"
Write-Host ""
Write-Host "Test it by staging some changes and running:"
Write-Host "  git commit"
