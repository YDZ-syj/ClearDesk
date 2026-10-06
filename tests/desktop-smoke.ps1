# Interactive Windows validation. Uses a separate profile and never moves user files.
param([string]$ReleaseDirectory = 'dist\v0.5.0')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$releaseRoot = Join-Path $projectRoot $ReleaseDirectory
$executable = Join-Path $releaseRoot 'ClearDesk.exe'
if (Get-Process ClearDesk -ErrorAction SilentlyContinue) { throw 'Exit the running ClearDesk instance before this test.' }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
[Reflection.Assembly]::LoadFrom($executable) | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class SmokeMouse {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
 public static void Down() { mouse_event(2,0,0,0,UIntPtr.Zero); }
 public static void Up() { mouse_event(4,0,0,0,UIntPtr.Zero); }
}
'@
$profileRoot = Join-Path $releaseRoot ('smoke-profile-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $profileRoot -Force | Out-Null
$fixtureDesktop = Join-Path $profileRoot 'Desktop'
$fixtureLibrary = Join-Path $profileRoot 'Library'
$exportFolder = Join-Path $profileRoot 'External'
New-Item -ItemType Directory -Path $fixtureDesktop,$exportFolder -Force | Out-Null
foreach ($name in @('drag-one.txt','drag-two.txt','drag-three.txt')) { Set-Content -LiteralPath (Join-Path $fixtureDesktop $name) -Value $name }
$fixtureSettings = [ClearDesk.Settings]::Default()
[ClearDesk.Catalog]::Classify($fixtureSettings, [string[]](Get-ChildItem -LiteralPath $fixtureDesktop | Select-Object -ExpandProperty FullName)) | Out-Null
$fixtureSettings.AutoArrangeZones = $false
$fixtureSettings.LayoutRevision = 3
$docKey = ([char]0x6587).ToString() + [char]0x6863
$sysKey = ([char]0x7cfb).ToString() + [char]0x7edf
$docZone = $fixtureSettings.Zones | Where-Object CategoryKey -eq $docKey
$docZone.X = 310; $docZone.Y = 180; $docZone.Width = 500; $docZone.Height = 350
$docZone.Items = New-Object 'System.Collections.Generic.List[ClearDesk.Entry]'
foreach ($name in @('drag-one.txt','drag-two.txt','drag-three.txt')) { $entry = New-Object ClearDesk.Entry; $entry.Name = $name; $entry.Path = Join-Path $fixtureDesktop $name; $docZone.Items.Add($entry) }
$sysZone = $fixtureSettings.Zones | Where-Object CategoryKey -eq $sysKey
$sysZone.X = 850; $sysZone.Y = 180
$fixtureStore = New-Object ClearDesk.SettingsStore((Join-Path $profileRoot 'settings.json'))
$fixtureOrganizer = New-Object ClearDesk.DesktopOrganizer((Join-Path $profileRoot 'moves.json'))
$fixtureBatch = $fixtureOrganizer.Plan($fixtureSettings, $fixtureDesktop, $fixtureLibrary, $null)
$fixtureOrganizer.Execute($fixtureSettings, $fixtureBatch, [Action]{ $fixtureStore.Save($fixtureSettings) }) | Out-Null
$fixtureStore.Save($fixtureSettings)
$script:testProcess = $null
$script:receiverProcess = $null
$script:desktopShell = $null
$report = [ordered]@{}
$prefix = ([char]0x6e05).ToString() + [char]0x684c + ' ' + [char]0xb7 + ' '
$moreName = ([char]0x66f4).ToString() + [char]0x591a + ' ' + [char]0x22ef
$exitName = ([char]0x9000).ToString() + [char]0x51fa
function Wait-Condition([scriptblock]$Condition, [string]$Description) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt 12000) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 100
    }
    throw ('Timed out: ' + $Description)
}
function Get-AppWindows {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:testProcess.Id)
    @([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window })
}
function Get-Manager { @(Get-AppWindows | Where-Object { $_.Current.Name -like '*ClearDesk*' }) | Select-Object -First 1 }
function Get-Widgets { @(Get-AppWindows | Where-Object { $_.Current.Name.StartsWith($prefix) }) }
function Find-Control($Root, [string]$Name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Invoke-Control($Element) {
    if ($null -eq $Element) { throw 'Required control was not found.' }
    $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Start-TestApp {
    $script:testProcess = Start-Process -FilePath $executable -ArgumentList @('--profile', ('"' + $profileRoot + '"'), '--desktop', ('"' + $fixtureDesktop + '"')) -WindowStyle Hidden -PassThru
    Wait-Condition { $null -ne (Get-Manager) -and @(Get-Widgets).Count -gt 0 } 'management and widget windows'
}
function Read-TestSettings {
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try { return Get-Content -LiteralPath (Join-Path $profileRoot 'settings.json') -Encoding UTF8 -Raw | ConvertFrom-Json }
        catch { if ($attempt -eq 19) { throw }; Start-Sleep -Milliseconds 30 }
    }
}
function Drag-Mouse($From, $To) {
    [SmokeMouse]::SetCursorPos([int]$From.X,[int]$From.Y) | Out-Null
    [SmokeMouse]::Down()
    try {
        for ($step = 1; $step -le 20; $step++) {
            [SmokeMouse]::SetCursorPos([int]($From.X + ($To.X - $From.X) * $step / 20),[int]($From.Y + ($To.Y - $From.Y) * $step / 20)) | Out-Null
            Start-Sleep -Milliseconds 35
        }
        Start-Sleep -Milliseconds 200
    } finally { [SmokeMouse]::Up() }
}
function Item-Point($Widget, [string]$Name) {
    $label = Find-Control $Widget $Name
    if ($null -eq $label) { throw ('Missing item: ' + $Name) }
    $bounds = $label.Current.BoundingRectangle
    New-Object System.Windows.Point(($bounds.Left + $bounds.Width/2), ($bounds.Top - 22 * $script:scale))
}
function Exit-TestApp {
    Invoke-Control (Find-Control (Get-Manager) $moreName)
    $script:exitControl = $null
    Wait-Condition {
        $pidCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:testProcess.Id)
        $nameCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $exitName)
        $both = New-Object System.Windows.Automation.AndCondition($pidCondition, $nameCondition)
        $script:exitControl = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $both)
        $null -ne $script:exitControl
    } 'exit menu'
    Invoke-Control $script:exitControl
    Wait-Condition { $script:testProcess.HasExited } 'normal shutdown'
}
$originalView = [ClearDesk.DesktopIconNative]::Find()
$iconsOriginallyVisible = [ClearDesk.DesktopIconNative]::IsWindowVisible($originalView)
$wallpaperIds = @(Get-Process wallpaper32,wallpaper64 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
try {
    Start-TestApp
    $widgets = @(Get-Widgets)
    $report.StartupWidgetCount = $widgets.Count
    $report.StartupFolded = @($widgets | Where-Object { $_.Current.BoundingRectangle.Height -gt 110 }).Count -eq 0
    $report.ToolWindowsExcludedFromAltTab = @($widgets | Where-Object { $style = [ClearDesk.DesktopWindows]::ExtendedStyle([IntPtr]$_.Current.NativeWindowHandle); ($style -band 0x80) -eq 0 -or ($style -band 0x40000) -ne 0 }).Count -eq 0
    if (!$report.ToolWindowsExcludedFromAltTab) { throw 'Widget styles still allow Alt+Tab entries.' }
    if (!$report.StartupFolded) { throw 'A startup widget is not folded.' }
    Wait-Condition { $view = [ClearDesk.DesktopIconNative]::Find(); $view -ne [IntPtr]::Zero -and ![ClearDesk.DesktopIconNative]::IsWindowVisible($view) } 'desktop icons hidden'
    $report.DesktopIconsHidden = $true
    $manager = Get-Manager
    $manager.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Minimized)
    $widget = $widgets | Where-Object { $_.Current.Name -eq ($prefix + $docKey) } | Select-Object -First 1
    $foldedHeight = $widget.Current.BoundingRectangle.Height
    $script:scale = $foldedHeight / 52
    Invoke-Control (Find-Control $widget ([char]0x25b8).ToString())
    Wait-Condition { $widget.Current.BoundingRectangle.Height -gt $foldedHeight * 2 } 'widget expanded'
    $report.ArrowExpands = $true
    $lockName = -join ([char[]]@(0x9501,0x5b9a,0x5206,0x533a))
    $unlockName = -join ([char[]]@(0x89e3,0x9501,0x5206,0x533a))
    Invoke-Control (Find-Control $widget $lockName)
    $lockedBounds = $widget.Current.BoundingRectangle
    Drag-Mouse (New-Object System.Windows.Point(($lockedBounds.Left+90),($lockedBounds.Top+20*$script:scale))) (New-Object System.Windows.Point(($lockedBounds.Left+200),($lockedBounds.Top+90*$script:scale)))
    Drag-Mouse (New-Object System.Windows.Point(($lockedBounds.Right-12),($lockedBounds.Bottom-12))) (New-Object System.Windows.Point(($lockedBounds.Right+60),($lockedBounds.Bottom+45)))
    $afterLockDrag = $widget.Current.BoundingRectangle
    if ([Math]::Abs($afterLockDrag.Left-$lockedBounds.Left) -gt 2 -or [Math]::Abs($afterLockDrag.Top-$lockedBounds.Top) -gt 2 -or [Math]::Abs($afterLockDrag.Width-$lockedBounds.Width) -gt 2 -or [Math]::Abs($afterLockDrag.Height-$lockedBounds.Height) -gt 2) { throw 'Locked window moved or resized.' }
    $report.LockBlocksMoveAndResize = $true
    if (!(Read-TestSettings).Zones.Where({$_.CategoryKey -eq $docKey})[0].Locked) { throw 'Zone lock was not saved.' }
    $report.LockSaved = $true
    Drag-Mouse (Item-Point $widget 'drag-three.txt') (Item-Point $widget 'drag-one.txt')
    Wait-Condition { $settings = Read-TestSettings; ($settings.Zones | Where-Object CategoryKey -eq $docKey).Items[0].Name -eq 'drag-three.txt' } 'manual reorder persisted'
    $report.ManualItemOrder = $true
    $report.LockedZoneAllowsItemReorder = $true
    Invoke-Control (Find-Control $widget $unlockName)
    $bounds = $widget.Current.BoundingRectangle
    Drag-Mouse (New-Object System.Windows.Point(($bounds.Right-12),($bounds.Bottom-12))) (New-Object System.Windows.Point(($bounds.Right+65),($bounds.Bottom+45)))
    Wait-Condition { $widget.Current.BoundingRectangle.Width -gt $bounds.Width + 40 -and $widget.Current.BoundingRectangle.Height -gt $bounds.Height + 30 } 'resize grip expands both dimensions'
    $report.ManualResize = $true
    $script:receiverProcess = Start-Process -FilePath (Join-Path $releaseRoot 'ClearDesk.DropReceiver.exe') -ArgumentList ('"' + $exportFolder + '"') -WindowStyle Hidden -PassThru
    $script:receiver = $null
    Wait-Condition {
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:receiverProcess.Id)
        $script:receiver = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,$condition)
        $null -ne $script:receiver
    } 'external file drop target'
    $receiverBounds = $script:receiver.Current.BoundingRectangle
    Drag-Mouse (Item-Point $widget 'drag-two.txt') (New-Object System.Windows.Point(($receiverBounds.Left+80),($receiverBounds.Top+80)))
    Wait-Condition { Test-Path -LiteralPath (Join-Path $exportFolder 'drag-two.txt') } 'external standard file drop copies file'
    $report.ExternalFileDrag = $true
    $script:receiverProcess.CloseMainWindow() | Out-Null
    Wait-Condition { $script:receiverProcess.HasExited } 'external test window closed'
    $script:desktopPoint = $null
    function Find-DesktopPoint {
        for ($hitX = 40; $hitX -lt 1800; $hitX += 80) {
            for ($hitY = 140; $hitY -lt 1000; $hitY += 80) {
                $point = New-Object System.Windows.Point($hitX,$hitY)
                if ([ClearDesk.DesktopWindows]::IsDesktopPoint($point)) { $script:desktopPoint = $point; return }
            }
        }
    }
    Find-DesktopPoint
    if ($null -eq $script:desktopPoint) {
        $script:desktopShell = New-Object -ComObject Shell.Application
        $script:desktopShell.MinimizeAll()
        Start-Sleep -Milliseconds 600
        [SmokeMouse]::ShowWindow([IntPtr]$widget.Current.NativeWindowHandle,4) | Out-Null
        Find-DesktopPoint
    }
    if ($null -eq $script:desktopPoint) { throw 'No uncovered desktop point is available for desktop extraction.' }
    Drag-Mouse (Item-Point $widget 'drag-one.txt') $script:desktopPoint
    Wait-Condition { (Read-TestSettings).DesktopItems.Count -eq 1 -and (Test-Path -LiteralPath (Join-Path $fixtureDesktop 'drag-one.txt')) } 'desktop extraction restores fixture file'
    $report.DragOutToDesktop = $true
    Start-Sleep -Milliseconds 2300
    $settings = Read-TestSettings
    if (@(($settings.Zones | Where-Object CategoryKey -eq $docKey).Items | Where-Object Name -eq 'drag-one.txt').Count -ne 0) { throw 'Extracted item was recaptured.' }
    $report.ExtractedItemNotRecaptured = $true
    $widget = Get-Widgets | Where-Object { $_.Current.Name -eq ($prefix + $docKey) } | Select-Object -First 1
    Invoke-Control (Find-Control $widget ([char]0x25be).ToString())
    Wait-Condition { [Math]::Abs($widget.Current.BoundingRectangle.Height - $foldedHeight) -lt 2 } 'widget completely folded'
    $report.ArrowFoldsCompletely = $true
    $manager = Get-Manager
    $manager.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
    Exit-TestApp
    $report.NormalExitRestoresFiles = @(Get-ChildItem -LiteralPath $fixtureDesktop -Filter '*.txt').Count -eq 3
    if (!$report.NormalExitRestoresFiles) { throw 'Normal exit did not restore organized fixture files.' }
    if ($iconsOriginallyVisible) { Wait-Condition { [ClearDesk.DesktopIconNative]::IsWindowVisible([ClearDesk.DesktopIconNative]::Find()) } 'icons restored after exit' }
    $report.NormalExitRestoresIcons = $true
    Start-TestApp
    Wait-Condition { ![ClearDesk.DesktopIconNative]::IsWindowVisible([ClearDesk.DesktopIconNative]::Find()) } 'icons hidden in crash test'
    $script:testProcess.Kill()
    Wait-Condition { $script:testProcess.HasExited } 'test process terminated'
    if ($iconsOriginallyVisible) { Wait-Condition { [ClearDesk.DesktopIconNative]::IsWindowVisible([ClearDesk.DesktopIconNative]::Find()) } 'guard restores icons after crash' }
    $report.CrashGuardRestoresIcons = $true
    $report.WallpaperProcessesPreserved = @($wallpaperIds | Where-Object { $null -eq (Get-Process -Id $_ -ErrorAction SilentlyContinue) }).Count -eq 0
    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseRoot 'desktop-smoke.json') -Encoding UTF8
    $report | ConvertTo-Json
} finally {
    # Only the test process created by this script can be stopped here. No user process is touched.
    if ($null -ne $script:testProcess -and !$script:testProcess.HasExited) {
        $script:testProcess.Kill()
        Start-Sleep -Milliseconds 800
    }
    if ($null -ne $script:receiverProcess -and !$script:receiverProcess.HasExited) { $script:receiverProcess.Kill() }
    if ($null -ne $script:desktopShell) { $script:desktopShell.UndoMinimizeAll() }
}
