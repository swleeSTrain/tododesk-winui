param([Parameter(Mandatory)][int]$AppPid)
$ErrorActionPreference='Stop'
$t='C:\Users\swlee\AppData\Local\Microsoft\WindowsApps\winapp.exe'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root=[System.Windows.Automation.AutomationElement]::FromHandle((Get-Process -Id $AppPid).MainWindowHandle)
function Invoke-UI([string]$id){
    & $t ui wait-for $id -a $AppPid -t 3000 | Out-Null
    if($LASTEXITCODE -ne 0){throw "Missing: $id"}
    & $t ui invoke $id -a $AppPid | Out-Null
    if($LASTEXITCODE -ne 0){throw "Invoke failed: $id"}
}
function Assert-Alive {
    $app=Get-Process -Id $AppPid -ErrorAction SilentlyContinue
    if(!$app -or !$app.Responding){throw 'Application exited or stopped responding'}
}
$native=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'FluentThemeComboBox'))
if($native){
    Invoke-UI FluentThemeComboBox
    $hit=(& $t ui search 'Liquid Glass' -a $AppPid --json|ConvertFrom-Json).matches|Where-Object {$_.type -eq 'ListItem'}|Select-Object -First 1
    Invoke-UI $hit.selector
}
$results=@()
foreach($theme in @('liquid','xp','aero')){
    Invoke-UI ThemeDropDownButton
    Invoke-UI "ThemeOption$theme"
    foreach($width in @(1440,1000,560,1280)){
        $root.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Resize($width,860)
        Start-Sleep -Milliseconds 200
        foreach($target in @('ThemeDropDownButton','NewIssueButton','ListModeButton','BoardModeButton','StatusFilterDropDownButton')){
            # Real keyboard input is required: InvokePattern does not draw system focus rings.
            & $t ui send-keys 'tab shift+tab' --target $target --via send-input -a $AppPid | Out-Null
            if($LASTEXITCODE -ne 0){throw "Keyboard focus failed: $target"}
            Start-Sleep -Milliseconds 100
            Assert-Alive
            $root.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Resize($width,780)
            $root.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Resize($width,860)
            Assert-Alive
        }
        $results+=@{theme=$theme;width=$width;keyboardFocus='PASS';resizeWithFocus='PASS'}
    }
}
Invoke-UI ThemeDropDownButton
Invoke-UI ThemeOptionfluent
$root.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Resize(1440,860)
$results | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'test-artifacts\focus-resize-results.json')
'PASS: 60 keyboard focus cases with resize across Liquid Glass, XP, and Aero.'
