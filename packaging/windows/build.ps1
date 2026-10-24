# Builds the Windows packages: the installer (Inno Setup) and a portable .zip with the app and gbcopy.
#
#   pwsh packaging/windows/build.ps1 [-Version 0.1.0] [-Output artifacts]
#
# Needs Inno Setup 6 (preinstalled on the GitHub Windows runners; otherwise: winget install JRSoftware.InnoSetup).
param(
    [string] $Version,
    [string] $Output
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
if (-not $Version) {
    $Version = ([xml](Get-Content "$root/Directory.Build.props")).Project.PropertyGroup.VersionPrefix | Where-Object { $_ } | Select-Object -First 1
}
if (-not $Output) { $Output = "$root/artifacts" }
# Windows file versions must be numeric: 0.2.0-beta.1 -> 0.2.0
$numericVersion = ($Version -split '[-+]')[0]
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$Output = (Resolve-Path $Output).Path
$work = Join-Path $Output 'windows-work'
$files = Join-Path $work 'GBFileCopier'

Write-Host "==> GBFileCopier $Version for Windows"
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue

# 1. Native AOT builds of the app and of the command line tool, in the same folder
foreach ($project in 'GBFileCopier.App', 'GBFileCopier.Cli') {
    dotnet publish "$root/src/$project" -c Release -r win-x64 -p:Version=$Version -o $files -nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $project failed" }
}
Remove-Item "$files/*.pdb" -ErrorAction SilentlyContinue
Copy-Item "$root/LICENSE" "$files/LICENSE.txt"

# 2. Portable zip
$zip = Join-Path $Output "GBFileCopier-$Version-windows-x64.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path "$files/*" -DestinationPath $zip

# 3. Installer
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found' }
& $iscc /Qp "/DAppVersion=$Version" "/DNumericVersion=$numericVersion" "/DSourceDir=$files" "/DRepoDir=$root" "/DOutputDir=$Output" "$PSScriptRoot/GBFileCopier.iss"
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }

Remove-Item -Recurse -Force $work
Get-ChildItem $Output -File | ForEach-Object { Write-Host "==> $($_.FullName)" }
