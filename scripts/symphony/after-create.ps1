$ErrorActionPreference = 'Stop'

function Test-WorkspaceHasContent {
    Get-ChildItem -LiteralPath (Get-Location) -Force -ErrorAction Stop |
        Where-Object { $_.Name -ne '.git' } |
        Select-Object -First 1
}

if (Test-Path -LiteralPath 'TodoApp/TodoApp.csproj') {
    Write-Host 'TodoDesk workspace is already populated.'
    exit 0
}

if (Test-WorkspaceHasContent) {
    throw 'Symphony workspace is not empty and does not contain TodoApp/TodoApp.csproj.'
}

$repoUrl = $env:SYMPHONY_REPO_URL
if ([string]::IsNullOrWhiteSpace($repoUrl)) {
    throw 'Set SYMPHONY_REPO_URL to the Git URL for this repository before Symphony creates workspaces.'
}

git clone --depth 1 $repoUrl .

if (-not (Test-Path -LiteralPath 'TodoApp/TodoApp.csproj')) {
    throw 'Clone completed, but TodoApp/TodoApp.csproj was not found.'
}

Write-Host 'TodoDesk workspace populated for Symphony.'
