[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0',
    [string]$ArtifactsDirectory,
    [string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [switch]$AllowUnsigned,
    [switch]$AllowDirty,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repositoryRoot = [IO.Directory]::GetParent($projectRoot).FullName
if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory)) {
    $ArtifactsDirectory = Join-Path $PSScriptRoot 'artifacts'
}
$artifactsRoot = [IO.Path]::GetFullPath($ArtifactsDirectory)
$releaseRoot = Join-Path $artifactsRoot (Join-Path 'release' $Version)
$stageDirectory = Join-Path $releaseRoot 'stage'
$symbolsDirectory = Join-Path $releaseRoot 'symbols'
$projectFile = Join-Path $projectRoot 'AutomationStudioWpf.csproj'
$publishProfile = Join-Path $projectRoot 'Properties\PublishProfiles\WinX64Installer.pubxml'
$privatePythonScript = Join-Path $PSScriptRoot 'build-private-python.ps1'
$vendorManifest = Join-Path $PSScriptRoot 'vendor-manifest.json'
$noticesTemplate = Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.template.txt'
$innoScript = Join-Path $PSScriptRoot 'AutomationStudio.iss'

function Invoke-Checked([string]$FilePath, [string[]]$Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed ($LASTEXITCODE): $FilePath $($Arguments -join ' ')"
    }
}

function Remove-DirectorySafe([string]$Path, [string]$AllowedRoot) {
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $resolvedRoot = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside artifacts root: $resolvedPath"
    }
    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

function Find-InnoCompiler() {
    if (-not [string]::IsNullOrWhiteSpace($env:INNO_SETUP_COMPILER) -and
        (Test-Path -LiteralPath $env:INNO_SETUP_COMPILER)) {
        return $env:INNO_SETUP_COMPILER
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    return $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

function Find-SignTool() {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path -LiteralPath $sdkRoot)) {
        return $null
    }

    return Get-ChildItem -LiteralPath $sdkRoot -Filter signtool.exe -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -match '\\x64$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

function Sign-File([string]$Path, [string]$SignTool) {
    Invoke-Checked $SignTool @(
        'sign', '/sha1', $CertificateThumbprint,
        '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256',
        $Path)
}

function Assert-PackagingDependencies() {
    $manifest = Get-Content -LiteralPath $vendorManifest -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($dependency in $manifest.packagingDependencies) {
        foreach ($file in $dependency.files) {
            $path = Join-Path $projectRoot $file.path
            if (-not (Test-Path -LiteralPath $path)) {
                throw "Packaging dependency missing: $($file.path)"
            }

            $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actual -ne ([string]$file.sha256).ToLowerInvariant()) {
                throw "Packaging dependency SHA256 mismatch: $($file.path)"
            }
        }
    }
}

function Write-ThirdPartyNotices([string]$RuntimeRoot, [string]$OutputPath) {
    $builder = New-Object Text.StringBuilder
    [void]$builder.AppendLine([IO.File]::ReadAllText($noticesTemplate))
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('Bundled artifact manifest')
    [void]$builder.AppendLine('-------------------------')
    [void]$builder.AppendLine([IO.File]::ReadAllText($vendorManifest))

    $innoTranslationLicense = Join-Path $projectRoot 'Packaging\InnoLanguages\LICENSE'
    [void]$builder.AppendLine()
    [void]$builder.AppendLine(('=' * 78))
    [void]$builder.AppendLine('Inno Setup Chinese Simplified Translation - MIT License')
    [void]$builder.AppendLine(('=' * 78))
    [void]$builder.AppendLine([IO.File]::ReadAllText($innoTranslationLicense))

    $licenseFiles = Get-ChildItem -LiteralPath $RuntimeRoot -Recurse -File |
        Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE)(\..*)?$' } |
        Sort-Object FullName -Unique
    foreach ($licenseFile in $licenseFiles) {
        [void]$builder.AppendLine()
        [void]$builder.AppendLine(('=' * 78))
        [void]$builder.AppendLine($licenseFile.FullName.Substring($RuntimeRoot.Length).TrimStart('\'))
        [void]$builder.AppendLine(('=' * 78))
        try {
            [void]$builder.AppendLine([IO.File]::ReadAllText($licenseFile.FullName))
        }
        catch {
            [void]$builder.AppendLine("[License text could not be decoded: $($_.Exception.Message)]")
        }
    }

    [IO.File]::WriteAllText($OutputPath, $builder.ToString(), (New-Object Text.UTF8Encoding($false)))
}

function Assert-ReleaseStage([string]$StageRoot) {
    $forbiddenPattern = '(?i)(^|[\\/])(bin|obj|Tests|CodexSmoke|\.cache)([\\/]|$)'
    foreach ($entry in Get-ChildItem -LiteralPath $StageRoot -Recurse -Force) {
        $relative = $entry.FullName.Substring($StageRoot.Length).TrimStart('\', '/')
        if ($relative -match $forbiddenPattern) {
            throw "Forbidden release-stage path: $relative"
        }
        if ($entry.Name -match '(?i)(\.pdb|\.pyc|\.pyo)$' -or $entry.Name -eq '__pycache__') {
            throw "Forbidden release-stage file: $relative"
        }
    }
}

function Remove-PythonCaches([string]$RuntimeRoot) {
    Get-ChildItem -LiteralPath $RuntimeRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq '__pycache__' } |
        Sort-Object { $_.FullName.Length } -Descending |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }

    Get-ChildItem -LiteralPath $RuntimeRoot -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in '.pyc', '.pyo' } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
}

Invoke-Checked 'git' @('-C', $repositoryRoot, 'diff', '--check', '--', 'AutomationStudio')

$dirty = & git -C $repositoryRoot status --porcelain -- AutomationStudio
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect Git worktree.'
}
if (-not $AllowDirty -and $dirty) {
    throw 'Release build requires a clean AutomationStudio worktree. Commit/stash changes or pass -AllowDirty for a local test build.'
}
if ([string]::IsNullOrWhiteSpace($CertificateThumbprint) -and -not $AllowUnsigned) {
    throw 'No signing certificate supplied. Use -CertificateThumbprint for a release or explicitly pass -AllowUnsigned for a test package.'
}

Assert-PackagingDependencies

Remove-DirectorySafe $releaseRoot $artifactsRoot
New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $symbolsDirectory -Force | Out-Null

$fileVersion = "$Version.0"
Invoke-Checked 'dotnet' @(
    'publish', $projectFile,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    ('-p:PublishProfile=' + $publishProfile),
    ('-p:Version=' + $Version),
    ('-p:FileVersion=' + $fileVersion),
    ('-p:AssemblyVersion=' + $fileVersion),
    ('-p:InformationalVersion=' + $Version),
    '-o', $stageDirectory)

$runtimeDirectory = Join-Path $stageDirectory 'Runtime\Python'
Invoke-Checked 'powershell.exe' @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass',
    '-File', $privatePythonScript,
    '-ManifestPath', $vendorManifest,
    '-OutputDirectory', $runtimeDirectory)

$pdbFiles = @(Get-ChildItem -LiteralPath $stageDirectory -Filter *.pdb -File -Recurse)
foreach ($pdbFile in $pdbFiles) {
    $relative = $pdbFile.FullName.Substring($stageDirectory.Length).TrimStart('\')
    $symbolPath = Join-Path $symbolsDirectory $relative
    New-Item -ItemType Directory -Path (Split-Path $symbolPath -Parent) -Force | Out-Null
    Move-Item -LiteralPath $pdbFile.FullName -Destination $symbolPath -Force
}

$noticesPath = Join-Path $stageDirectory 'THIRD-PARTY-NOTICES.txt'
Write-ThirdPartyNotices $runtimeDirectory $noticesPath
Copy-Item -LiteralPath $vendorManifest -Destination (Join-Path $stageDirectory 'vendor-manifest.json') -Force

$pythonExe = Join-Path $runtimeDirectory 'python.exe'
Invoke-Checked $pythonExe @(
    '-I', '-c',
    "import sys,cv2,numpy,PIL; assert sys.version_info[:3] == (3,14,6); assert cv2.__version__ == '4.13.0'; assert numpy.__version__ == '2.4.6'; assert PIL.__version__ == '12.2.0'")
Remove-PythonCaches $runtimeDirectory

$applicationExe = Join-Path $stageDirectory 'AutomationStudioWpf.exe'
if (-not (Test-Path -LiteralPath $applicationExe)) {
    throw "Published executable missing: $applicationExe"
}
if (-not (Test-Path -LiteralPath (Join-Path $stageDirectory 'Python\find_image.py'))) {
    throw 'Published find_image.py is missing.'
}
if (Get-ChildItem -LiteralPath $stageDirectory -Filter *.pdb -File -Recurse) {
    throw 'PDB files remain in installer stage.'
}
Assert-ReleaseStage $stageDirectory

$signTool = $null
if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $signTool = Find-SignTool
    if ([string]::IsNullOrWhiteSpace($signTool)) {
        throw 'signtool.exe was not found.'
    }
    Sign-File $applicationExe $signTool
}

$installerPath = $null
if (-not $SkipInstaller) {
    $innoCompiler = Find-InnoCompiler
    if ([string]::IsNullOrWhiteSpace($innoCompiler)) {
        throw 'Inno Setup 6 compiler not found. Install it or set INNO_SETUP_COMPILER.'
    }

    $suffix = if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) { '-UNSIGNED' } else { '' }
    $baseName = "AutomationStudio-$Version-win-x64$suffix"
    Invoke-Checked $innoCompiler @(
        "/DMyAppVersion=$Version",
        "/DSourceDir=$stageDirectory",
        "/DProjectRoot=$projectRoot",
        "/DInstallerOutputDir=$releaseRoot",
        "/DInstallerBaseName=$baseName",
        $innoScript)
    $installerPath = Join-Path $releaseRoot ($baseName + '.exe')
    if (-not (Test-Path -LiteralPath $installerPath)) {
        throw "Installer output missing: $installerPath"
    }
    if ($signTool) {
        Sign-File $installerPath $signTool
    }
}

$hashTargets = @($applicationExe, $pythonExe)
if ($installerPath) {
    $hashTargets += $installerPath
}

$isSigned = -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)
if (-not $isSigned -and $installerPath -and ([IO.Path]::GetFileNameWithoutExtension($installerPath) -notmatch '(?i)-UNSIGNED$')) {
    throw 'Unsigned installer must include the -UNSIGNED suffix.'
}
$hashLines = foreach ($target in $hashTargets) {
    $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($target))"
}
[IO.File]::WriteAllLines(
    (Join-Path $releaseRoot 'SHA256SUMS.txt'),
    $hashLines,
    (New-Object Text.UTF8Encoding($false)))

$buildManifest = [ordered]@{
    schemaVersion = 1
    product = 'AutomationStudio'
    version = $Version
    runtimeIdentifier = 'win-x64'
    selfContained = $true
    singleFile = $false
    trimmed = $false
    signed = $isSigned
    builtAtUtc = [DateTime]::UtcNow.ToString('O')
    gitCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    dirty = [bool]$dirty
}
[IO.File]::WriteAllText(
    (Join-Path $releaseRoot 'build-manifest.json'),
    ($buildManifest | ConvertTo-Json -Depth 4),
    (New-Object Text.UTF8Encoding($false)))

Write-Host "Release stage ready: $stageDirectory"
if ($installerPath) {
    Write-Host "Installer ready: $installerPath"
} else {
    Write-Warning 'Installer compilation was skipped.'
}
