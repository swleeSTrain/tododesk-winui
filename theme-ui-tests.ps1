param([Parameter(Mandatory)][int]$AppPid, [string]$Mode='light')
$ErrorActionPreference='Stop'
$t='C:\Users\swlee\AppData\Local\Microsoft\WindowsApps\winapp.exe'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root=[System.Windows.Automation.AutomationElement]::FromHandle((Get-Process -Id $AppPid).MainWindowHandle)
function Find-Id([string]$id) {
    $found=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
    if(!$found){throw "Missing control: $id"}
    return $found
}
function Switch-Theme([string]$name) {
    & $t ui invoke ThemeDropDownButton -a $AppPid | Out-Null
    Start-Sleep -Milliseconds 200
    $tag=@{'Liquid Glass'='liquid';'Windows XP'='xp';'Aero'='aero';'WinUI Fluent'='fluent'}[$name]
    & $t ui wait-for "ThemeOption$tag" -a $AppPid -t 3000 | Out-Null
    if($LASTEXITCODE -ne 0){throw "Missing theme: $name"}
    & $t ui invoke "ThemeOption$tag" -a $AppPid | Out-Null
    if($LASTEXITCODE -ne 0){throw "Failed theme: $name"}
    Start-Sleep -Milliseconds 400
    if(!(Get-Process -Id $AppPid -ErrorAction SilentlyContinue)){throw "App exited on $name"}
}
try {$null=Find-Id 'FluentThemeComboBox';$native=$true} catch {$native=$false}
if($native){
    & $t ui invoke FluentThemeComboBox -a $AppPid | Out-Null
    Start-Sleep -Milliseconds 200
    $hits=& $t ui search 'Liquid Glass' -a $AppPid --json | ConvertFrom-Json
    $hit=$hits.matches | Where-Object {$_.name -eq 'Liquid Glass' -and $_.type -eq 'ListItem'} | Select-Object -First 1
    & $t ui invoke $hit.selector -a $AppPid | Out-Null
    Start-Sleep -Milliseconds 400
}
$list=Find-Id 'IssueListView'
$item=$list.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem))
$item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 200
$title=Find-Id 'IssueTitleTextBox'
$original=$title.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
$draft='theme-draft-check'
$title.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($draft)
$rows=@()
try {
foreach($theme in @('Liquid Glass','Windows XP','Aero')){
    Switch-Theme $theme
    foreach($width in @(1440,1000,560)){
        $root.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Resize($width,860)
        Start-Sleep -Milliseconds 400
        if((Find-Id 'IssueTitleTextBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $draft){throw "Draft lost: $theme"}
        foreach($id in @('SearchTextBox','StatusFilterDropDownButton','PriorityFilterDropDownButton','AddQuickIssueButton','SaveIssueButton','DeleteIssueButton')){
            $control=Find-Id $id
            $rect=$control.Current.BoundingRectangle;$bounds=$root.Current.BoundingRectangle
            if($control.Current.IsOffscreen -or $rect.Width -le 0 -or $rect.Right -gt $bounds.Right -or $rect.Bottom -gt $bounds.Bottom){throw "Clipped control $id in $theme/$width"}
        }
        $tag=$theme.Replace(' ','').ToLowerInvariant()
        & $t ui screenshot -a $AppPid -o "A:\Repos\winuitest\test-artifacts\theme-$Mode-$tag-$width.png" | Out-Null
        $rows+=@{theme=$theme;width=$width;draft='PASS';commands='PASS'}
    }
}
& $t ui invoke BoardModeButton -a $AppPid | Out-Null
Start-Sleep -Milliseconds 300
& $t ui screenshot -a $AppPid -o "A:\Repos\winuitest\test-artifacts\theme-$Mode-aero-board.png" | Out-Null
& $t ui invoke ListModeButton -a $AppPid | Out-Null
Switch-Theme 'WinUI Fluent'
Start-Sleep -Milliseconds 200
if((Find-Id 'FluentIssueTitleTextBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $draft){throw 'Draft lost returning to Fluent'}
(Find-Id 'FluentIssueTitleTextBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($original)
$root.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Resize(1440,860)
$rows|ConvertTo-Json|Set-Content "test-artifacts/theme-$Mode-results.json"
Write-Output "PASS: 9 theme/width combinations; unsaved draft preserved across four themes."
} finally {
    foreach($id in @('IssueTitleTextBox','FluentIssueTitleTextBox')){
        try{(Find-Id $id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($original)}catch{}
    }
}
