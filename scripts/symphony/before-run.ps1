$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath 'TodoApp/TodoApp.csproj')) {
    throw 'TodoApp/TodoApp.csproj was not found. The Symphony workspace is not ready.'
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw '.NET SDK was not found on PATH. TodoDesk requires .NET SDK 10 for WinUI development.'
}

dotnet restore .\TodoApp\TodoApp.csproj -p:Platform=x64

Write-Host 'TodoDesk dependencies restored for Symphony run.'
