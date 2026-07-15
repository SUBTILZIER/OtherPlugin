[CmdletBinding()]
param(
    [string]$ManifestPath,
    [string]$CacheDirectory,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $PSScriptRoot 'vendor-manifest.json'
}
if ([string]::IsNullOrWhiteSpace($CacheDirectory)) {
    $CacheDirectory = Join-Path $PSScriptRoot '.cache'
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-Artifact([string]$Path, [string]$ExpectedHash) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    return (Get-Sha256 $Path) -eq $ExpectedHash.ToLowerInvariant()
}

function Get-Artifact($Artifact) {
    $target = Join-Path $CacheDirectory $Artifact.fileName
    if (Assert-Artifact $target $Artifact.sha256) {
        return $target
    }

    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Force
    }

    Write-Host "Downloading $($Artifact.id) $($Artifact.version)..."
    Invoke-WebRequest -UseBasicParsing -Uri $Artifact.url -OutFile $target
    if (-not (Assert-Artifact $target $Artifact.sha256)) {
        Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
        throw "SHA256 mismatch: $($Artifact.fileName)"
    }

    return $target
}

function Write-Utf8NoBom([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
    throw "Unsupported vendor manifest schema: $($manifest.schemaVersion)"
}

New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null
$outputFullPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputFullPath) {
    Remove-Item -LiteralPath $outputFullPath -Recurse -Force
}
New-Item -ItemType Directory -Path $outputFullPath -Force | Out-Null

$resolvedArtifacts = @{}
foreach ($artifact in $manifest.artifacts) {
    $resolvedArtifacts[$artifact.id] = Get-Artifact $artifact
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($resolvedArtifacts['python-embed'], $outputFullPath)

$sitePackages = Join-Path $outputFullPath 'Lib\site-packages'
New-Item -ItemType Directory -Path $sitePackages -Force | Out-Null
foreach ($artifact in $manifest.artifacts | Where-Object kind -eq 'wheel') {
    [IO.Compression.ZipFile]::ExtractToDirectory($resolvedArtifacts[$artifact.id], $sitePackages)
}

$pthPath = Join-Path $outputFullPath 'python314._pth'
$pthLines = @(Get-Content -LiteralPath $pthPath | Where-Object { $_ -ne '#import site' -and $_ -ne 'import site' -and $_ -ne 'Lib\site-packages' })
$pthLines += 'Lib\site-packages'
$pthLines += 'import site'
[IO.File]::WriteAllLines($pthPath, $pthLines, [Text.Encoding]::ASCII)

$removeDirectories = @(Get-ChildItem -LiteralPath $sitePackages -Recurse -Directory -Force |
    Where-Object { $_.Name -eq '__pycache__' } |
    Sort-Object { $_.FullName.Length } -Descending)
foreach ($directory in $removeDirectories) {
    Remove-Item -LiteralPath $directory.FullName -Recurse -Force -ErrorAction SilentlyContinue
}
$numpyRoot = Join-Path $sitePackages 'numpy'
if (Test-Path -LiteralPath $numpyRoot) {
    $numpyTestDirectories = @(Get-ChildItem -LiteralPath $numpyRoot -Recurse -Directory -Force |
        Where-Object { $_.Name -eq 'tests' } |
        Sort-Object { $_.FullName.Length } -Descending)
    foreach ($directory in $numpyTestDirectories) {
        Remove-Item -LiteralPath $directory.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}
Get-ChildItem -LiteralPath $sitePackages -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -eq '.pyc' -or $_.Extension -eq '.pyo' } |
    Remove-Item -Force -ErrorAction SilentlyContinue

$runtimeManifest = [ordered]@{
    schemaVersion = 1
    pythonVersion = [string]$manifest.runtime.pythonVersion
    architecture = [string]$manifest.runtime.architecture
    packages = [ordered]@{
        'opencv-python-headless' = [string]$manifest.runtime.packages.'opencv-python-headless'
        numpy = [string]$manifest.runtime.packages.numpy
        Pillow = [string]$manifest.runtime.packages.Pillow
    }
}
Write-Utf8NoBom (Join-Path $outputFullPath 'runtime-manifest.json') ($runtimeManifest | ConvertTo-Json -Depth 5)

$pythonExe = Join-Path $outputFullPath 'python.exe'
$env:PYTHONNOUSERSITE = '1'
$env:PYTHONDONTWRITEBYTECODE = '1'
$env:PYTHONUTF8 = '1'
Remove-Item Env:PYTHONPATH -ErrorAction SilentlyContinue
Remove-Item Env:PYTHONHOME -ErrorAction SilentlyContinue
& $pythonExe -I -c "import sys,cv2,numpy,PIL; assert sys.version_info[:3] == (3,14,6); assert cv2.__version__ == '4.13.0'; assert numpy.__version__ == '2.4.6'; assert PIL.__version__ == '12.2.0'"
if ($LASTEXITCODE -ne 0) {
    throw "Bundled Python validation failed with exit code $LASTEXITCODE"
}

Write-Host "Private Python ready: $outputFullPath"
