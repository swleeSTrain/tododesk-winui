param([Parameter(Mandatory)][int]$AppPid)
$ErrorActionPreference='Stop'
$t='C:\Users\swlee\AppData\Local\Microsoft\WindowsApps\winapp.exe'
function Invoke-UI([string]$id) {
    & $t ui wait-for $id -a $AppPid -t 3000 | Out-Null
    if($LASTEXITCODE -ne 0){throw "Missing $id"}
    & $t ui invoke $id -a $AppPid | Out-Null
    if($LASTEXITCODE -ne 0){throw "Failed $id"}
}
function Choose([string]$button,[string]$option){Invoke-UI $button;Invoke-UI $option}
Invoke-UI FluentThemeComboBox
$hit=(& $t ui search 'Liquid Glass' -a $AppPid --json|ConvertFrom-Json).matches|Where-Object {$_.type -eq 'ListItem'}|Select-Object -First 1
Invoke-UI $hit.selector
$title='menu-check-'+[guid]::NewGuid().ToString('N')
& $t ui set-value QuickIssueTextBox $title -a $AppPid | Out-Null
Invoke-UI AddQuickIssueButton
& $t ui wait-for IssueTitleTextBox -a $AppPid --value $title -t 3000 | Out-Null
if($LASTEXITCODE -ne 0){throw 'Test issue not selected'}
Choose DetailStatusDropDownButton DetailStatusOptionDone
Choose DetailPriorityDropDownButton DetailPriorityOptionUrgent
Choose DetailProjectDropDownButton DetailProjectOptionMobile
Choose DetailAssigneeDropDownButton DetailAssigneeOptionQA
Invoke-UI SaveIssueButton
Start-Sleep -Milliseconds 300
$data=Join-Path $env:LOCALAPPDATA 'Packages\411F6E8C-8F04-422B-B511-2732DEB9D0C5_1z32rh13vfry6\LocalState\issues.json'
$issue=Get-Content $data -Raw|ConvertFrom-Json|Where-Object {$_.Title -eq $title}
if(!$issue -or $issue.Status -ne 'Done' -or $issue.Priority -ne 'Urgent' -or $issue.Project -ne 'Mobile' -or $issue.Assignee -ne 'QA'){throw 'Menu values did not persist'}
Choose StatusFilterDropDownButton StatusFilterOptionDone
Choose PriorityFilterDropDownButton PriorityFilterOptionUrgent
& $t ui wait-for IssueTitleTextBox -a $AppPid --value $title -t 3000 | Out-Null
if($LASTEXITCODE -ne 0){throw 'Filtered test issue not selected'}
Invoke-UI DeleteIssueButton
Start-Sleep -Milliseconds 300
if(Get-Content $data -Raw|ConvertFrom-Json|Where-Object {$_.Title -eq $title}){throw 'Test issue was not deleted'}
Choose StatusFilterDropDownButton StatusFilterOptionAll
Choose PriorityFilterDropDownButton PriorityFilterOptionAll
Choose ThemeDropDownButton ThemeOptionfluent
'PASS: four editor menus persist values; two filters and test issue deletion work.'
