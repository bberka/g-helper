<#
.SYNOPSIS
  Publishes G-Helper as a self-contained single file for one or more runtime identifiers (works with pwsh on
  Windows and Linux).

.EXAMPLE
  ./publish.ps1                              # every RID, output below artifacts/publish/<rid>
  ./publish.ps1 -Rid win-x64,linux-x64 -Archive
  ./publish.ps1 -Rid linux-arm64 -Version 0.287.0 -ReadyToRun

.NOTES
  -Archive writes artifacts/dist/GHelper-<version>-<rid>.zip for win-* RIDs. Linux archives and packages are built by
  packaging/linux/build-packages.sh (they need real file modes and symlinks), which -Archive calls when it runs on
  Linux.
#>
[CmdletBinding()]
param(
    [string[]] $Rid = @('win-x64', 'win-x86', 'win-arm64', 'linux-x64', 'linux-arm64', 'linux-arm', 'linux-musl-x64', 'linux-musl-arm64'),
    [string] $Configuration = 'Release',
    # default: <Version> of Directory.Build.props
    [string] $Version,
    [string] $OutDir,
    [switch] $Archive,
    # opt in: faster start up, ~35% larger files
    [switch] $ReadyToRun
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src/GHelper.App/GHelper.App.csproj'
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts' }
if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
$Version = $Version.TrimStart('v')

$results = foreach ($r in $Rid) {
    $dir = Join-Path $OutDir "publish/$r"
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }

    $dotnetArgs = @('publish', $project, '-r', $r, '-c', $Configuration, '-o', $dir, "-p:Version=$Version", '--nologo')
    if ($ReadyToRun) { $dotnetArgs += '-p:PublishReadyToRun=true' }

    $sw = [Diagnostics.Stopwatch]::StartNew()
    & dotnet @dotnetArgs | Out-Host
    $ok = $LASTEXITCODE -eq 0
    $sw.Stop()

    $main = if ($r -like 'win-*') { 'GHelper.exe' } else { 'GHelper' }
    $file = Join-Path $dir $main
    $size = if ($ok -and (Test-Path $file)) { [math]::Round((Get-Item $file).Length / 1MB, 1) } else { $null }
    $files = if (Test-Path $dir) { (Get-ChildItem $dir -File | Measure-Object).Count } else { 0 }

    if ($ok -and $Archive) {
        $dist = Join-Path $OutDir 'dist'
        New-Item -ItemType Directory -Force $dist | Out-Null
        if ($r -like 'win-*') {
            $zip = Join-Path $dist "GHelper-$Version-$r.zip"
            if (Test-Path $zip) { Remove-Item $zip }
            Compress-Archive -Path $file -DestinationPath $zip
        } elseif ($IsLinux) {
            & sh (Join-Path $root 'packaging/linux/build-packages.sh') --rid $r --version $Version --publish-dir $dir --out $dist | Out-Host
            if ($LASTEXITCODE -ne 0) { $ok = $false }
        } else {
            Write-Warning "$r archive/packages must be built on Linux (packaging/linux/build-packages.sh)"
        }
    }

    [pscustomobject]@{ Rid = $r; Ok = $ok; SizeMB = $size; Files = $files; Seconds = [int]$sw.Elapsed.TotalSeconds }
}

$results | Format-Table -AutoSize | Out-Host
if ($results | Where-Object { -not $_.Ok }) { exit 1 }
