param([string]$Version = '0.6.0', [string]$ReleaseDirectory = 'dist\v0.6.0')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a numeric version.' }
$projectRoot = Split-Path $PSScriptRoot -Parent
$distRoot = Join-Path $projectRoot 'dist'
$releaseRoot = Join-Path $projectRoot $ReleaseDirectory
$sourceRoot = Join-Path $distRoot ('github-source-' + $Version + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $sourceRoot | Out-Null
$sourceNames = @('src','tests','assets','tools','.github','.gitignore','README.md','LICENSE','CHANGELOG.md','CONTRIBUTING.md','build.ps1','ClearDesk.csproj')
foreach ($name in $sourceNames) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $sourceRoot -Recurse }
# Include only public documentation; never recursively package local review materials.
$docNames = @('RELEASE_NOTES_0.5.0.md','RELEASE_NOTES_0.5.1.md',('RELEASE_NOTES_' + $Version + '.md'),'RESEARCH.md','TESTING.md','desktop-smoke-0.5.0.json','manager-preview.png') | Select-Object -Unique
New-Item -ItemType Directory -Path (Join-Path $sourceRoot 'docs') | Out-Null
foreach ($name in $docNames) { Copy-Item -LiteralPath (Join-Path $projectRoot ('docs\' + $name)) -Destination (Join-Path $sourceRoot 'docs') }
$sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -File -Recurse -Force)
if ($sourceFiles | Where-Object { $_.Name -match '^(settings|moves)\.json|^\.env($|\.)|credentials|hosts\.yml|PUBLISH_REVIEW' -or $_.Extension -in @('.exe','.dll','.pfx','.key','.pem','.bak','.log') }) { throw 'Unexpected runtime, personal data, or credential file in source package.' }
# Use .NET ZIP creation so dotfiles such as .gitignore and .github are included reliably.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourceZip = Join-Path $distRoot ('ClearDesk-' + $Version + '-source.zip')
$tempZip = Join-Path $distRoot ([Guid]::NewGuid().ToString('N') + '.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($sourceRoot,$tempZip)
Move-Item -LiteralPath $tempZip -Destination $sourceZip -Force
$runtimeRoot = Join-Path $distRoot ('runtime-package-' + $Version + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $runtimeRoot | Out-Null
foreach ($name in @('ClearDesk.exe','ClearDesk.exe.config')) { Copy-Item -LiteralPath (Join-Path $releaseRoot $name) -Destination $runtimeRoot }
foreach ($name in @('README.md','LICENSE','CHANGELOG.md','assets')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $runtimeRoot -Recurse }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'docs') -Destination $runtimeRoot -Recurse
$runtimeZip = Join-Path $distRoot ('ClearDesk-' + $Version + '-windows.zip')
$tempZip = Join-Path $distRoot ([Guid]::NewGuid().ToString('N') + '.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($runtimeRoot,$tempZip)
Move-Item -LiteralPath $tempZip -Destination $runtimeZip -Force
$standalone = Join-Path $distRoot ('ClearDesk-' + $Version + '.exe')
Copy-Item -LiteralPath (Join-Path $releaseRoot 'ClearDesk.exe') -Destination $standalone -Force
Get-FileHash -LiteralPath $sourceZip,$runtimeZip,$standalone -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_.Path) } | Set-Content -LiteralPath (Join-Path $distRoot ('SHA256SUMS-' + $Version + '.txt')) -Encoding ASCII
Write-Host "Source package: $sourceZip"
Write-Host "Windows package: $runtimeZip"
