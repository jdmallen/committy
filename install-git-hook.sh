#!/bin/bash

# Install committy as a git hook
# Usage: ./install-git-hook.sh <repo-path>
#        ./install-git-hook.sh --global

# Check if arguments are provided
if [ $# -eq 0 ]; then
    echo "Error: Missing required argument"
    echo ""
    echo "Usage: ./install-git-hook.sh <repo-path>"
    echo "   or: ./install-git-hook.sh --global"
    echo ""
    echo "Examples:"
    echo "  ./install-git-hook.sh .              # Install in current directory"
    echo "  ./install-git-hook.sh /path/to/repo  # Install in specific repo"
    echo "  ./install-git-hook.sh --global       # Install as global template"
    exit 1
fi

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
    REPO_PATH="$1"
    echo "Installing local git hook to: $REPO_PATH"
    
    # Check if repo path exists
    if [ ! -d "$REPO_PATH" ]; then
        echo "Error: Repository path does not exist: $REPO_PATH"
        exit 1
    fi
    
    # Convert to absolute path
    REPO_PATH="$(cd "$REPO_PATH" && pwd)"
    
    # Check if it's a git repository
    if ! git -C "$REPO_PATH" rev-parse --git-dir > /dev/null 2>&1; then
        echo "Error: Not a valid git repository: $REPO_PATH"
        exit 1
    fi
    
    # Get git dir and convert to absolute path if needed
    GIT_DIR="$(git -C "$REPO_PATH" rev-parse --git-dir)"
    if [[ ! "$GIT_DIR" = /* ]]; then
        GIT_DIR="$REPO_PATH/$GIT_DIR"
    fi
    HOOKS_DIR="$GIT_DIR/hooks"
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
    echo    "  ./install-git-hook.sh /path/to/repo"
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
