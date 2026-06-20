param([Parameter(Mandatory)][int]$AppPid)

$ErrorActionPreference = 'Continue'
$pass = 0
$fail = 0
$results = @()

function Add-Result {
    param(
        [string]$Name,
        [string]$Status,
        [string]$Detail = ""
    )

    $script:results += [ordered]@{
        name = $Name
        status = $Status
        detail = $Detail
    }
}

function Test-UI {
    param([string]$Name, [scriptblock]$Script)

    try {
        $global:LASTEXITCODE = 0
        $output = & $Script 2>&1
        if ($LASTEXITCODE -eq 0) {
            $script:pass++
            Add-Result $Name "PASS"
        } else {
            $script:fail++
            Add-Result $Name "FAIL" "$output"
        }
    } catch {
        $script:fail++
        Add-Result $Name "FAIL" "$_"
    }
}

function Invoke-WithRetry {
    param(
        [Parameter(Mandatory)][scriptblock]$Script,
        [int]$Retries = 3,
        [int]$DelayMilliseconds = 500
    )

    $lastOutput = ""
    for ($attempt = 1; $attempt -le $Retries; $attempt++) {
        $global:LASTEXITCODE = 0
        $lastOutput = & $Script 2>&1
        if ($LASTEXITCODE -eq 0) {
            return $lastOutput
        }

        Start-Sleep -Milliseconds $DelayMilliseconds
    }

    throw "$lastOutput"
}

function Get-IssuesDataFile {
    $packageRoot = Get-ChildItem "$env:LOCALAPPDATA\Packages" -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "411F6E8C-8F04-422B-B511-2732DEB9D0C5_*" } |
        Select-Object -First 1

    if (-not $packageRoot) {
        throw "TodoDesk package folder was not found."
    }

    $dataFile = Join-Path $packageRoot.FullName "LocalState\issues.json"
    if (-not (Test-Path $dataFile)) {
        throw "issues.json was not found at $dataFile"
    }

    return $dataFile
}

New-Item -ItemType Directory -Force -Path "test-artifacts" | Out-Null

Test-UI "Main window exists" {
    $windows = winapp ui list-windows -a $AppPid --json 2>$null | ConvertFrom-Json
    if (-not ($windows | Where-Object { $_.title -eq "TodoDesk" })) {
        throw "TodoDesk window not found"
    }
}

Test-UI "Fluent quick add textbox exists" {
    winapp ui wait-for "FluentQuickIssueTextBox" -a $AppPid -t 3000
}

Test-UI "Fluent search textbox exists" {
    winapp ui wait-for "FluentSearchTextBox" -a $AppPid -t 3000
}

Test-UI "Fluent issue list exists" {
    winapp ui wait-for "FluentIssueListView" -a $AppPid -t 3000
}

$testTitle = "자동화 확인 이슈 " + (Get-Date -Format "HHmmss")

Test-UI "Create quick issue" {
    winapp ui set-value "FluentQuickIssueTextBox" $testTitle -a $AppPid
    winapp ui invoke "FluentAddQuickIssueButton" -a $AppPid
    winapp ui wait-for "FluentIssueTitleTextBox" -a $AppPid --value $testTitle -t 3000
}

Test-UI "Search filters created issue" {
    winapp ui set-value "FluentSearchTextBox" $testTitle -a $AppPid
    winapp ui wait-for "FluentSearchTextBox" -a $AppPid --value $testTitle -t 2000
    winapp ui wait-for "FluentIssueTitleTextBox" -a $AppPid --value $testTitle -t 3000
}

$updatedTitle = $testTitle + " 저장"

Test-UI "Edit and save selected issue" {
    winapp ui set-value "FluentIssueTitleTextBox" $updatedTitle -a $AppPid
    winapp ui set-value "FluentIssueDescriptionTextBox" "UI 자동화 테스트에서 저장된 설명입니다." -a $AppPid
    winapp ui invoke "FluentSaveIssueButton" -a $AppPid
    winapp ui wait-for "FluentIssueTitleTextBox" -a $AppPid --value $updatedTitle -t 3000
}

Test-UI "Update search after renamed issue" {
    winapp ui wait-for "FluentSearchTextBox" -a $AppPid -t 3000
    Invoke-WithRetry { winapp ui set-value "FluentSearchTextBox" $updatedTitle -a $AppPid }
    winapp ui wait-for "FluentSearchTextBox" -a $AppPid --value $updatedTitle -t 2000
    winapp ui wait-for "FluentIssueTitleTextBox" -a $AppPid --value $updatedTitle -t 3000
}

Test-UI "Saved issue persists to local data" {
    $dataFile = Get-IssuesDataFile
    $issues = Get-Content $dataFile -Raw | ConvertFrom-Json
    if (-not ($issues | Where-Object { $_.Title -eq $updatedTitle })) {
        throw "Saved issue title was not found in $dataFile"
    }
}

Test-UI "Delete test issue" {
    winapp ui wait-for "FluentIssueTitleTextBox" -a $AppPid --value $updatedTitle -t 3000
    winapp ui invoke "FluentDeleteIssueButton" -a $AppPid
    Start-Sleep -Milliseconds 800

    $dataFile = Get-IssuesDataFile
    $issues = Get-Content $dataFile -Raw | ConvertFrom-Json
    if ($issues | Where-Object { $_.Title -eq $updatedTitle }) {
        throw "Deleted issue still exists in $dataFile"
    }
}

Test-UI "Reset search for visual review" {
    winapp ui wait-for "FluentSearchTextBox" -a $AppPid -t 3000
    Invoke-WithRetry { winapp ui set-value "FluentSearchTextBox" " " -a $AppPid }
}

Test-UI "Capture final screenshot" {
    winapp ui screenshot -a $AppPid -o "test-artifacts/final-state.png"
}

Test-UI "Interactive controls expose AutomationId" {
    $tree = winapp ui inspect -a $AppPid --interactive --json 2>$null | ConvertFrom-Json
    $elements = @($tree.elements | Where-Object {
        $_.type -match 'Button|TextBox|ComboBox|List|Edit' -and
        $_.name -notmatch 'Minimize|Maximize|Close|System' -and
        $_.className -notmatch 'Windows.UI.Core.CoreWindow|Popup'
    })

    $missing = @($elements | Where-Object { -not $_.automationId })
    if ($missing.Count -gt 0) {
        $sample = ($missing | Select-Object -First 12 | ForEach-Object { "$($_.type) '$($_.name)'" }) -join "; "
        throw "Missing AutomationId count=$($missing.Count): $sample"
    }
}

Write-Host ""
Write-Host "Passed: $pass | Failed: $fail"
$results | Where-Object { $_.status -eq "FAIL" } | ForEach-Object {
    Write-Host "FAIL: $($_.name) - $($_.detail)" -ForegroundColor Red
}

$results | ConvertTo-Json -Depth 4 | Out-File "test-artifacts/test-results.json" -Encoding utf8
if ($fail -gt 0) {
    exit 1
}

exit 0
