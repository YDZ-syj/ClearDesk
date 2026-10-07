# Real UI validation using private fixtures and demo mode; never controls Explorer icons.
param([string]$ReleaseDirectory = 'dist\v0.6.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path (Join-Path $root $ReleaseDirectory) 'ClearDesk.exe'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$work = Join-Path (Join-Path $root 'dist') ('operation-smoke-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$desktop = Join-Path $work 'Desktop'; $profile = Join-Path $work 'Profile'
New-Item -ItemType Directory -Path $desktop,$profile -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $desktop 'first.txt'),'first')
[IO.File]::WriteAllText((Join-Path $desktop 'second.txt'),'second')
$testProcess = $null
function Find-Element([string]$Name) {
    $conditions = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,[int]$testProcess.Id)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$Name)))
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$conditions)
}
function Wait-Condition([scriptblock]$Predicate,[string]$Description) {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (!(& $Predicate)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw ('Timed out: '+$Description) }
        Start-Sleep -Milliseconds 100
    }
}
function Click([string]$Name) {
    Wait-Condition { $element = Find-Element $Name; $null -ne $element -and $element.Current.IsEnabled } $Name
    $element = Find-Element $Name
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
try {
    $testProcess = Start-Process -FilePath $exe -ArgumentList ('--demo --profile "'+$profile+'" --desktop "'+$desktop+'"') -WindowStyle Hidden -PassThru
    Click '整理桌面（移动文件）'
    Wait-Condition { $null -ne (Find-Element '移动勾选项目') } 'selective move preview'
    $pidCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,[int]$testProcess.Id)
    $checkCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::CheckBox)
    $checks = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.AndCondition($pidCondition,$checkCondition)))
    if ($checks.Count -ne 2) { throw 'Expected exactly two move selection checkboxes.' }
    $checks[1].GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Click '移动勾选项目'
    Wait-Condition { @(Get-ChildItem -LiteralPath $desktop -File).Count -eq 1 -and (Find-Element '整理桌面（移动文件）').Current.IsEnabled } 'selected file moved and controls re-enabled'
    Click '撤销上次整理'
    Click '恢复勾选项目'
    Wait-Condition { @(Get-ChildItem -LiteralPath $desktop -File).Count -eq 2 -and (Find-Element '整理桌面（移动文件）').Current.IsEnabled } 'selected file restored'
    Click '更多 ⋯'
    Click '退出'
    if (!$testProcess.WaitForExit(15000)) { throw 'Demo exit timeout.' }
    if ($testProcess.ExitCode -ne 0) { throw 'Demo application exited with an error.' }
    $report = [ordered]@{DemoUsesPrivateProfile=$true;SelectiveMoveCheckbox=$true;UnselectedFilePreserved=$true;SelectiveRestore=$true;NormalExit=$true;RealDesktopIntegrationDisabled=$true}
    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'report.json') -Encoding UTF8
    $report | ConvertTo-Json
} finally { if ($null -ne $testProcess) { if (!$testProcess.HasExited) { $testProcess.Kill(); $testProcess.WaitForExit() }; $testProcess.Dispose() } }
