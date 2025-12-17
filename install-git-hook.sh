#!/bin/bash

# Install committy as a git hook
# Usage: ./install-git-hook.sh [--global]

HOOK_TYPE="prepare-commit-msg"
HOOK_SOURCE="./hooks/$HOOK_TYPE"

# Verify hook source file exists
if [ ! -f "$HOOK_SOURCE" ]; then
    echo "Error: Hook file not found at $HOOK_SOURCE"
    exit 1
fi

if [ "$1" = "--global" ]; then
    echo "Installing global git hook template..."
    
    # Create global hooks directory
    HOOKS_DIR="$HOME/.git-templates/hooks"
    mkdir -p "$HOOKS_DIR"
    
    # Set global template directory
    git config --global init.templateDir "$HOME/.git-templates"
else
    echo "Installing local git hook..."
    
    # Check if we're in a git repository
    if ! git rev-parse --git-dir > /dev/null 2>&1; then
        echo "Error: Not in a git repository. Run from repo root or use --global flag."
        exit 1
    fi
    
    HOOKS_DIR="$(git rev-parse --git-dir)/hooks"
fi

mkdir -p "$HOOKS_DIR"
HOOK_FILE="$HOOKS_DIR/$HOOK_TYPE"

# Copy the hook file
cp "$HOOK_SOURCE" "$HOOK_FILE"
chmod +x "$HOOK_FILE"

echo "✓ Hook installed: $HOOK_FILE"

if [ "$1" = "--global" ]; then
    echo -e "\nGlobal hook template installed. New repositories will automatically"
    echo    "include this hook. For existing repos, run:"
    echo    "  git init  # in each existing repository"
    echo -e "\nOr install locally in existing repos:"
    echo    "  ./install-git-hook.sh"
else
    echo -e "\nHook installed for current repository."
    echo    "Now when you run 'git commit', AI suggestions will appear in your editor."
fi

echo -e "\nMake sure to set your Azure OpenAI configuration:"
echo    "  export AZURE_OPENAI_API_KEY=your_api_key_here"
echo    "  export AZURE_OPENAI_ENDPOINT_HOST=https://your-endpoint.openai.azure.com"
echo    "  export AZURE_OPENAI_DEPLOYMENT=your-deployment-name"
echo -e "\nTest it by staging some changes and running:"
echo    "  git commit"
