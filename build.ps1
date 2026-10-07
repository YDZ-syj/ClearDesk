param([switch]$Test, [string]$OutputDirectory = 'dist')
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework 4.x compiler is required.' }
$out = Join-Path $PSScriptRoot $OutputDirectory
New-Item -ItemType Directory -Path $out -Force | Out-Null
$refs = @('System.dll','System.Core.dll','System.Net.Http.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Runtime.Serialization.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$sources = @('Core.cs','Organizer.cs','MovePreview.cs','App.cs','AppOperations.cs','EntryTracking.cs','ProfileBackup.cs','DesktopIcons.cs','ZoneLayout.cs','RenameDialog.cs','DesktopInteraction.cs','AppBrand.cs','StartupRegistration.cs','UpdateChecker.cs') | ForEach-Object { Join-Path $PSScriptRoot ('src\' + $_) }
$icon = Join-Path $PSScriptRoot 'assets\ClearDesk.ico'
if (!(Test-Path -LiteralPath $icon)) { throw 'Missing app icon. Run tools/build-icon.ps1 first.' }
$branding = @(('/win32icon:' + $icon), ('/resource:' + $icon + ',ClearDesk.AppIcon'))
& $compiler /nologo /target:winexe /main:ClearDesk.IconGuard /out:"$out\ClearDesk.IconGuard.exe" $refs "$PSScriptRoot\src\DesktopIcons.cs"
if ($LASTEXITCODE -ne 0) { throw 'Desktop icon guard compilation failed.' }
if ($Test) {
    & $compiler /nologo /target:winexe /optimize+ /platform:anycpu /main:ClearDesk.DeskApp /win32manifest:"$PSScriptRoot\src\app.manifest" /out:"$out\ClearDesk.exe" $branding $refs $sources
    if ($LASTEXITCODE -ne 0) { throw 'Packaged-app test compilation failed.' }
    & $compiler /nologo /target:winexe /main:ClearDesk.DropReceiver /out:"$out\ClearDesk.DropReceiver.exe" $refs "$PSScriptRoot\tests\DropReceiver.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Drop receiver compilation failed.' }
    & $compiler /nologo /target:exe /main:ClearDesk.CoreTests /out:"$out\ClearDesk.Tests.exe" $branding $refs $sources "$PSScriptRoot\tests\CoreTests.cs" "$PSScriptRoot\tests\OrganizerTests.cs" "$PSScriptRoot\tests\ZoneTests.cs" "$PSScriptRoot\tests\StartupTests.cs" "$PSScriptRoot\tests\UpdateTests.cs" "$PSScriptRoot\tests\AdvantageTests.cs" "$PSScriptRoot\tests\StandaloneTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & "$out\ClearDesk.Tests.exe" "$out"
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
} else {
    & $compiler /nologo /target:winexe /optimize+ /platform:anycpu /main:ClearDesk.DeskApp /win32manifest:"$PSScriptRoot\src\app.manifest" /out:"$out\ClearDesk.exe" $branding $refs $sources
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
    Copy-Item "$PSScriptRoot\src\App.config" "$out\ClearDesk.exe.config" -Force
    Write-Host "Built $out\ClearDesk.exe"
}
