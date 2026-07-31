[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseRoot,
    [ValidateSet('Host', 'Vm')]
    [string]$Mode = 'Host',
    [string]$PreviousReleaseRoot,
    [string]$InstallDirectory,
    [switch]$AllowUnsigned,
    [switch]$PreserveArtifacts,
    [switch]$ConfirmDisposableVm,
    [switch]$RequireOffline,
    [switch]$RequireStandardUser,
    [switch]$RequireDefender
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$innoScript = Join-Path $PSScriptRoot 'AutomationStudio.iss'
$runId = [Guid]::NewGuid().ToString('N')
$allowedTestRoot = [IO.Path]::GetFullPath((Join-Path $env:TEMP 'AutomationStudio.ReleaseVerify'))
$testRoot = Join-Path $allowedTestRoot $runId
$markerPath = Join-Path $testRoot '.automationstudio-release-test'
$markerContent = 'AutomationStudio.ReleaseVerify.v1'
$releaseRoot = [IO.Path]::GetFullPath($ReleaseRoot)
$reportDirectory = Join-Path $releaseRoot 'verification'
$jsonReportPath = Join-Path $reportDirectory 'release-verification.json'
$markdownReportPath = Join-Path $reportDirectory 'release-verification.md'
$installed = $false
$installRoot = $null
$failure = $null
$checks = New-Object Collections.Generic.List[object]
$startedAtUtc = [DateTime]::UtcNow

function Fail([string]$Message) {
    throw "Release verification failed: $Message"
}

function Add-Check(
    [string]$Id,
    [string]$Status,
    [long]$DurationMs,
    [string]$Message,
    [object]$Details = $null) {
    $checks.Add([ordered]@{
        id = $Id
        status = $Status
        durationMs = $DurationMs
        message = $Message
        details = $Details
    })
}

function Invoke-Check([string]$Id, [scriptblock]$Action) {
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $details = & $Action
        Add-Check $Id 'Passed' $stopwatch.ElapsedMilliseconds 'Passed' $details
        return $details
    }
    catch {
        Add-Check $Id 'Failed' $stopwatch.ElapsedMilliseconds $_.Exception.Message @{
            exceptionType = $_.Exception.GetType().FullName
        }
        throw
    }
}

function Add-NotRun([string]$Id, [string]$Message) {
    Add-Check $Id 'NotRun' 0 $Message
}

function Quote-Argument([string]$Value) {
    return '"' + $Value.Replace('"', '\"') + '"'
}

function Wait-Until([scriptblock]$Condition, [int]$TimeoutSeconds = 60) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    return [bool](& $Condition)
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

function Invoke-ProcessChecked(
    [string]$FilePath,
    [string[]]$Arguments,
    [string]$Operation,
    [int]$TimeoutSeconds = 120) {
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -PassThru
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill() } catch { }
        Fail "$Operation timed out after $TimeoutSeconds seconds. PID=$($process.Id)"
    }
    if ($process.ExitCode -ne 0) {
        Fail "$Operation returned exit code $($process.ExitCode). PID=$($process.Id)"
    }
    return $process.ExitCode
}

function Assert-TestRoot([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $prefix = $allowedTestRoot.TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        Fail "unsafe test path: $resolved"
    }
    $marker = Join-Path $resolved '.automationstudio-release-test'
    if (-not (Test-Path -LiteralPath $marker) -or
        (Get-Content -LiteralPath $marker -Raw -Encoding UTF8).Trim() -ne $markerContent) {
        Fail "test path marker missing or invalid: $resolved"
    }
    return $resolved
}

function Remove-TestRootSafe([string]$Path) {
    $resolved = Assert-TestRoot $Path
    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

function Get-ProcessByExecutable([string]$ExecutablePath) {
    $expected = [IO.Path]::GetFullPath($ExecutablePath)
    return @(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($expected)) -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                $_.Path -and [IO.Path]::GetFullPath($_.Path).Equals($expected, [StringComparison]::OrdinalIgnoreCase)
            }
            catch {
                $false
            }
        })
}

function Get-PrivatePythonProcess([string]$InstalledRoot) {
    $expected = [IO.Path]::GetFullPath((Join-Path $InstalledRoot 'Runtime\Python\python.exe'))
    return @(Get-Process -Name 'python' -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -and [IO.Path]::GetFullPath($_.Path).Equals($expected, [StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $false
        }
    })
}

function Assert-NoExistingApplication() {
    if (@(Get-Process -Name 'AutomationStudioWpf' -ErrorAction SilentlyContinue).Count -gt 0) {
        Fail 'an AutomationStudio process is already running; release verification will not touch it'
    }
    try {
        $mutex = [Threading.Mutex]::OpenExisting('Local\SUBTILZIER.AutomationStudioWpf.SingleInstance')
        $mutex.Dispose()
        Fail 'the AutomationStudio single-instance mutex is already held'
    }
    catch [Threading.WaitHandleCannotBeOpenedException] {
    }
}

function Get-StageHashMap([string]$StageRoot) {
    $map = @{}
    Get-ChildItem -LiteralPath $StageRoot -File -Recurse | ForEach-Object {
        $relative = $_.FullName.Substring($StageRoot.Length).TrimStart('\', '/')
        $map[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $map
}

function Assert-InstalledStage([hashtable]$StageHashes, [string]$InstalledRoot) {
    foreach ($relative in $StageHashes.Keys) {
        $path = Join-Path $InstalledRoot $relative
        if (-not (Test-Path -LiteralPath $path)) {
            Fail "installed file missing: $relative"
        }
        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $StageHashes[$relative]) {
            Fail "installed file changed: $relative"
        }
    }
}

function Assert-HashMapsEqual(
    [hashtable]$Expected,
    [hashtable]$Actual,
    [string]$Description) {
    $missing = @($Expected.Keys | Where-Object { -not $Actual.ContainsKey($_) })
    $added = @($Actual.Keys | Where-Object { -not $Expected.ContainsKey($_) })
    $changed = @($Expected.Keys | Where-Object {
        $Actual.ContainsKey($_) -and $Actual[$_] -ne $Expected[$_]
    })
    if ($missing.Count -gt 0 -or $added.Count -gt 0 -or $changed.Count -gt 0) {
        Fail "$Description changed files. Missing=$($missing -join ','); Added=$($added -join ','); Changed=$($changed -join ',')"
    }
}

function Find-ProductionInstaller([string]$Root) {
    return Get-ChildItem -LiteralPath $Root -Filter 'AutomationStudio-*.exe' -File |
        Where-Object { $_.Name -notmatch '(?i)-VERIFY\.exe$' -and $_.Name -notmatch '^unins' } |
        Select-Object -First 1
}

function Compile-VerificationInstaller(
    [string]$StageRoot,
    [string]$Version,
    [string]$OutputRoot,
    [string]$BaseName) {
    $compiler = Find-InnoCompiler
    if ([string]::IsNullOrWhiteSpace($compiler)) {
        Fail 'Inno Setup 6 compiler not found'
    }
    New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
    & $compiler "/DVerificationBuild=1" "/DMyAppVersion=$Version" "/DSourceDir=$StageRoot" "/DProjectRoot=$projectRoot" "/DInstallerOutputDir=$OutputRoot" "/DInstallerBaseName=$BaseName" $innoScript | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Fail "verification installer compilation failed with exit code $LASTEXITCODE"
    }
    $result = Join-Path $OutputRoot ($BaseName + '.exe')
    if (-not (Test-Path -LiteralPath $result)) {
        Fail "verification installer missing: $result"
    }
    return $result
}

function Resolve-PreviousRelease([Version]$CurrentVersion) {
    if (-not [string]::IsNullOrWhiteSpace($PreviousReleaseRoot)) {
        return [IO.Path]::GetFullPath($PreviousReleaseRoot)
    }
    $parent = Split-Path $releaseRoot -Parent
    $candidates = @()
    foreach ($directory in Get-ChildItem -LiteralPath $parent -Directory -ErrorAction SilentlyContinue) {
        [Version]$parsed = $null
        if (-not [Version]::TryParse($directory.Name, [ref]$parsed) -or $parsed -ge $CurrentVersion) {
            continue
        }
        $candidateManifest = Join-Path $directory.FullName 'build-manifest.json'
        $candidateStage = Join-Path $directory.FullName 'stage'
        if (-not (Test-Path -LiteralPath $candidateManifest) -or -not (Test-Path -LiteralPath $candidateStage)) {
            continue
        }
        $candidateData = Get-Content -LiteralPath $candidateManifest -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($Mode -eq 'Host' -and
            (-not ($candidateData.PSObject.Properties.Name -contains 'releaseSelfTestSchema') -or
             [int]$candidateData.releaseSelfTestSchema -lt 1 -or
             -not ($candidateData.PSObject.Properties.Name -contains 'verification') -or
             [string]$candidateData.verification.hostStatus -ne 'passed')) {
            continue
        }
        $candidates += [pscustomobject]@{ Version = $parsed; Root = $directory.FullName }
    }
    return $candidates | Sort-Object Version -Descending | Select-Object -First 1 -ExpandProperty Root
}

function Install-Package([string]$InstallerPath, [string]$TargetDirectory, [string]$LogPath) {
    $arguments = @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        ('/DIR=' + (Quote-Argument $TargetDirectory)),
        ('/LOG=' + (Quote-Argument $LogPath))
    )
    Invoke-ProcessChecked $InstallerPath $arguments 'installer' 180 | Out-Null
}

function Stop-InstalledApplication([string]$InstalledRoot) {
    $exe = Join-Path $InstalledRoot 'AutomationStudioWpf.exe'
    if (@(Get-ProcessByExecutable $exe).Count -eq 0) {
        return
    }
    Invoke-ProcessChecked $exe @('--shutdown-for-update') 'shutdown-for-update' 15 | Out-Null
    if (-not (Wait-Until {
        @(Get-ProcessByExecutable $exe).Count -eq 0 -and
        @(Get-PrivatePythonProcess $InstalledRoot).Count -eq 0
    } 60)) {
        Fail 'application or private Python remained after shutdown-for-update'
    }
}

function Uninstall-Package([string]$InstalledRoot) {
    $uninstaller = Join-Path $InstalledRoot 'unins000.exe'
    if (-not (Test-Path -LiteralPath $uninstaller)) {
        Fail "uninstaller missing: $uninstaller"
    }
    Invoke-ProcessChecked $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') 'uninstaller' 180 | Out-Null
    if (-not (Wait-Until { -not (Test-Path -LiteralPath $InstalledRoot) } 30)) {
        Fail "uninstall left install directory behind: $InstalledRoot"
    }
}

function Write-Reports([object]$Manifest, [string]$InstallerPath) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    $passed = $null -eq $failure -and @($checks | Where-Object status -eq 'Failed').Count -eq 0
    $report = [ordered]@{
        schemaVersion = 1
        product = 'AutomationStudio'
        version = [string]$Manifest.version
        mode = $Mode
        runId = $runId
        passed = $passed
        startedAtUtc = $startedAtUtc.ToString('O')
        completedAtUtc = [DateTime]::UtcNow.ToString('O')
        os = [Environment]::OSVersion.VersionString
        architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        installer = $InstallerPath
        installerSha256 = if (Test-Path -LiteralPath $InstallerPath) { (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null }
        signed = [bool]$Manifest.signed
        checks = $checks
        failure = if ($null -eq $failure) { $null } else { $failure.Exception.ToString() }
    }
    [IO.File]::WriteAllText(
        $jsonReportPath,
        ($report | ConvertTo-Json -Depth 12),
        (New-Object Text.UTF8Encoding($false)))

    $lines = New-Object Collections.Generic.List[string]
    $lines.Add("# AutomationStudio Windows Release Verification")
    $lines.Add('')
    $lines.Add("- Version: $($Manifest.version)")
    $lines.Add("- Mode: $Mode")
    $lines.Add("- Passed: $passed")
    $lines.Add("- Installer SHA256: $($report.installerSha256)")
    $lines.Add('')
    $lines.Add('| Check | Status | Duration | Message |')
    $lines.Add('|---|---:|---:|---|')
    foreach ($check in $checks) {
        $message = ([string]$check.message).Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
        $lines.Add("| $($check.id) | $($check.status) | $($check.durationMs)ms | $message |")
    }
    [IO.File]::WriteAllLines($markdownReportPath, $lines, (New-Object Text.UTF8Encoding($false)))
}

New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
[IO.File]::WriteAllText($markerPath, $markerContent, (New-Object Text.UTF8Encoding($false)))
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null

if ($Mode -eq 'Vm' -and -not $ConfirmDisposableVm) {
    Fail 'VM mode requires -ConfirmDisposableVm because it uses the production AppId and real user-data paths'
}
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $unicodeDirectoryName = ([char]0x6D4B) + ([char]0x8BD5) + ' Path'
    $InstallDirectory = Join-Path $testRoot ($unicodeDirectoryName + '\AutomationStudio')
}
$installRoot = [IO.Path]::GetFullPath($InstallDirectory)
if ($Mode -eq 'Host') {
    $testPrefix = (Assert-TestRoot $testRoot).TrimEnd('\') + '\'
    if (-not $installRoot.StartsWith($testPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        Fail 'Host verification install directory must stay inside its marked test root'
    }
}

$manifestPath = Join-Path $releaseRoot 'build-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) {
    Fail "build-manifest.json missing: $manifestPath"
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$stageRoot = Join-Path $releaseRoot 'stage'
$stageExe = Join-Path $stageRoot 'AutomationStudioWpf.exe'
$stagePython = Join-Path $stageRoot 'Runtime\Python\python.exe'
$productionInstaller = Find-ProductionInstaller $releaseRoot
if ($null -eq $productionInstaller) {
    Fail 'production installer missing'
}
$installerToTest = $productionInstaller.FullName

try {
    Invoke-Check 'environment-safety' {
        Assert-NoExistingApplication
        if (-not [Environment]::Is64BitOperatingSystem) { Fail 'x64 Windows is required' }
        if ($RequireStandardUser -and
            ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            Fail 'verification must run as a standard user'
        }
        if ($RequireOffline) {
            $defaultRoutes = @(Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
                Where-Object { $_.State -eq 'Alive' })
            if ($defaultRoutes.Count -gt 0) { Fail 'offline verification requested but an active default network route exists' }
        }
        return @{ testRoot = $testRoot; installRoot = $installRoot }
    } | Out-Null

    Invoke-Check 'release-layout-and-hashes' {
        if (-not (Test-Path -LiteralPath $stageExe)) { Fail 'stage executable missing' }
        if (-not (Test-Path -LiteralPath $stagePython)) { Fail 'private Python missing' }
        if (-not (Test-Path -LiteralPath (Join-Path $stageRoot 'Python\find_image.py'))) { Fail 'find_image.py missing' }
        if (Get-ChildItem -LiteralPath $stageRoot -Recurse -File |
            Where-Object { $_.Extension -in '.pdb', '.pyc', '.pyo' -or $_.Name -eq '__pycache__' }) {
            Fail 'release stage contains debug symbols or Python cache files'
        }
        $sumFile = Join-Path $releaseRoot 'SHA256SUMS.txt'
        if (-not (Test-Path -LiteralPath $sumFile)) { Fail 'SHA256SUMS.txt missing' }
        foreach ($line in Get-Content -LiteralPath $sumFile -Encoding UTF8) {
            if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') { Fail "invalid SHA256SUMS line: $line" }
            $expectedHash = $matches[1].ToLowerInvariant()
            $name = $matches[2].Trim()
            if ($name -eq 'AutomationStudioWpf.exe') { $target = $stageExe }
            elseif ($name -eq 'python.exe') { $target = $stagePython }
            else { $target = Join-Path $releaseRoot $name }
            if (-not (Test-Path -LiteralPath $target)) { Fail "hash target missing: $name" }
            $actualHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actualHash -ne $expectedHash) { Fail "SHA256 mismatch: $name" }
        }
        if (-not [bool]$manifest.signed) {
            if (-not $AllowUnsigned) { Fail 'unsigned package requires -AllowUnsigned' }
            if ($productionInstaller.BaseName -notmatch '(?i)-UNSIGNED$') { Fail 'unsigned installer lacks -UNSIGNED suffix' }
        }
        else {
            $signature = Get-AuthenticodeSignature -LiteralPath $productionInstaller.FullName
            if ($signature.Status -ne 'Valid') { Fail "installer signature invalid: $($signature.Status)" }
        }
        return @{ stageFiles = (Get-ChildItem -LiteralPath $stageRoot -File -Recurse).Count }
    } | Out-Null

    if ($Mode -eq 'Host') {
        $installerToTest = Invoke-Check 'verification-installer-build' {
            $output = Join-Path $testRoot 'Installer'
            return Compile-VerificationInstaller $stageRoot ([string]$manifest.version) $output "AutomationStudio-$($manifest.version)-VERIFY"
        }
    }

    $currentVersion = [Version]([string]$manifest.version)
    $previousRoot = Resolve-PreviousRelease $currentVersion
    $previousInstaller = $null
    if (-not [string]::IsNullOrWhiteSpace($previousRoot)) {
        $previousManifest = Get-Content -LiteralPath (Join-Path $previousRoot 'build-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($Mode -eq 'Host') {
            $previousInstaller = Invoke-Check 'previous-verification-installer-build' {
                return Compile-VerificationInstaller (Join-Path $previousRoot 'stage') ([string]$previousManifest.version) (Join-Path $testRoot 'PreviousInstaller') "AutomationStudio-$($previousManifest.version)-VERIFY"
            }
        }
        else {
            $previousInstallerFile = Find-ProductionInstaller $previousRoot
            if ($null -eq $previousInstallerFile) { Fail 'previous production installer missing' }
            $previousInstaller = $previousInstallerFile.FullName
        }
    }

    $stageHashes = Get-StageHashMap $stageRoot
    $expectedDataRoot = if ($Mode -eq 'Host') { Join-Path $testRoot 'UserData' } else { Join-Path $env:APPDATA 'AutomationStudioWpf' }
    if ($previousInstaller) {
        Invoke-Check 'upgrade-running-instance' {
            Install-Package $previousInstaller $installRoot (Join-Path $testRoot 'install-previous.log')
            $script:installed = $true
            New-Item -ItemType Directory -Path $expectedDataRoot -Force | Out-Null
            [IO.File]::WriteAllText((Join-Path $expectedDataRoot 'release-verify-preserve.txt'), $runId)
            $oldExe = Join-Path $installRoot 'AutomationStudioWpf.exe'
            $oldArgs = @()
            if ($Mode -eq 'Host') { $oldArgs = @('--release-test-root', (Quote-Argument $testRoot)) }
            Start-Process -FilePath $oldExe -ArgumentList $oldArgs | Out-Null
            if (-not (Wait-Until { @(Get-ProcessByExecutable $oldExe).Count -eq 1 } 20)) { Fail 'previous application did not start' }
            Install-Package $installerToTest $installRoot (Join-Path $testRoot 'upgrade-current.log')
            if (-not (Wait-Until { @(Get-ProcessByExecutable $oldExe).Count -eq 0 } 60)) { Fail 'upgrade left previous application running' }
            if ((Get-Item $oldExe).VersionInfo.ProductVersion -notlike "$($manifest.version)*") { Fail 'upgrade did not install current version' }
            if ((Get-Content -LiteralPath (Join-Path $expectedDataRoot 'release-verify-preserve.txt') -Raw) -ne $runId) { Fail 'upgrade lost user data' }
            return @{ previousVersion = [string]$previousManifest.version; currentVersion = [string]$manifest.version }
        } | Out-Null
    }
    else {
        Add-NotRun 'upgrade-running-instance' 'No previous release with release self-test schema was found.';
        Invoke-Check 'fresh-install' {
            Install-Package $installerToTest $installRoot (Join-Path $testRoot 'install-current.log')
            $script:installed = $true
            if (-not (Test-Path -LiteralPath (Join-Path $installRoot 'unins000.exe'))) {
                Fail 'fresh install completed without an uninstaller'
            }
            return @{ installer = $installerToTest }
        } | Out-Null
    }

    Invoke-Check 'installed-files-match-stage' {
        Assert-InstalledStage $stageHashes $installRoot
        return @{ fileCount = $stageHashes.Count }
    } | Out-Null
    $installedBaselineHashes = Get-StageHashMap $installRoot

    $selfTestReport = Join-Path $testRoot 'self-test-report.json'
    Invoke-Check 'installed-runtime-self-test' {
        $exe = Join-Path $installRoot 'AutomationStudioWpf.exe'
        Invoke-ProcessChecked $exe @(
            '--release-self-test',
            '--release-test-root', (Quote-Argument $testRoot),
            '--report', (Quote-Argument $selfTestReport)
        ) 'release self-test' 120 | Out-Null
        if (-not (Test-Path -LiteralPath $selfTestReport)) { Fail 'release self-test report missing' }
        $selfReport = Get-Content -LiteralPath $selfTestReport -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not [bool]$selfReport.passed) { Fail 'installed runtime self-test reported failure' }
        Copy-Item -LiteralPath $selfTestReport -Destination (Join-Path $reportDirectory 'installed-self-test.json') -Force
        return @{ checks = @($selfReport.checks).Count }
    } | Out-Null

    Invoke-Check 'single-instance-and-shutdown' {
        $exe = Join-Path $installRoot 'AutomationStudioWpf.exe'
        $normalArgs = @()
        if ($Mode -eq 'Host') { $normalArgs = @('--release-test-root', (Quote-Argument $testRoot)) }
        Start-Process -FilePath $exe -ArgumentList $normalArgs | Out-Null
        if (-not (Wait-Until { @(Get-ProcessByExecutable $exe).Count -eq 1 } 20)) { Fail 'primary application did not start' }
        $primaryPid = @(Get-ProcessByExecutable $exe)[0].Id
        for ($index = 0; $index -lt 4; $index++) {
            Invoke-ProcessChecked $exe $normalArgs "secondary instance $($index + 2)" 15 | Out-Null
        }
        if (@(Get-ProcessByExecutable $exe).Count -ne 1) { Fail 'repeated startup created multiple application processes' }
        $shutdownArgs = @('--shutdown-for-update') + $normalArgs
        Invoke-ProcessChecked $exe $shutdownArgs 'shutdown-for-update' 20 | Out-Null
        if (-not (Wait-Until {
            @(Get-ProcessByExecutable $exe).Count -eq 0 -and
            @(Get-PrivatePythonProcess $installRoot).Count -eq 0
        } 60)) { Fail 'application or private Python remained after shutdown' }
        return @{ launches = 5; primaryPid = $primaryPid }
    } | Out-Null

    Invoke-Check 'install-directory-remains-read-only-by-contract' {
        Assert-InstalledStage $stageHashes $installRoot
        $installedAfterRunHashes = Get-StageHashMap $installRoot
        Assert-HashMapsEqual $installedBaselineHashes $installedAfterRunHashes 'runtime install directory'
        return @{ verifiedFiles = $installedAfterRunHashes.Count }
    } | Out-Null

    if ($Mode -eq 'Vm' -and $RequireDefender) {
        Invoke-Check 'windows-defender-scan' {
            $status = Get-MpComputerStatus
            if (-not $status.AntivirusEnabled) { Fail 'Windows Defender antivirus is not enabled' }
            $scanStart = Get-Date
            Start-MpScan -ScanType CustomScan -ScanPath $productionInstaller.FullName
            Start-MpScan -ScanType CustomScan -ScanPath $stageRoot
            $detections = @(Get-MpThreatDetection -ErrorAction SilentlyContinue |
                Where-Object { $_.InitialDetectionTime -ge $scanStart })
            if ($detections.Count -gt 0) { Fail "Defender detected $($detections.Count) threat(s)" }
            return @{ engineVersion = $status.AMEngineVersion }
        } | Out-Null
    }
    else {
        Add-NotRun 'windows-defender-scan' 'Not run on Host; use Vm mode with -RequireDefender.';
    }

    Invoke-Check 'uninstall-and-data-retention' {
        if (-not (Test-Path -LiteralPath $expectedDataRoot)) {
            New-Item -ItemType Directory -Path $expectedDataRoot -Force | Out-Null
        }
        $sentinel = Join-Path $expectedDataRoot 'release-verify-preserve.txt'
        [IO.File]::WriteAllText($sentinel, $runId)
        Uninstall-Package $installRoot
        $script:installed = $false
        if (-not (Test-Path -LiteralPath $sentinel)) { Fail 'uninstall removed user data' }
        return @{ retainedData = $sentinel }
    } | Out-Null
}
catch {
    $failure = $_
}
finally {
    if ($installed -and $installRoot) {
        try {
            Stop-InstalledApplication $installRoot
            Uninstall-Package $installRoot
            $installed = $false
        }
        catch {
            Add-Check 'emergency-cleanup' 'Failed' 0 $_.Exception.Message
            if ($null -eq $failure) { $failure = $_ }
        }
    }

    if ($null -ne $failure) {
        Add-Check 'failure-artifacts' 'Retained' 0 "Failure artifacts retained at $testRoot"
    }

    try {
        Write-Reports $manifest $productionInstaller.FullName
    }
    catch {
        if ($null -eq $failure) { $failure = $_ }
    }

    if (-not $PreserveArtifacts -and $null -eq $failure) {
        try { Remove-TestRootSafe $testRoot } catch {
            if ($null -eq $failure) { $failure = $_ }
        }
    }
}

if ($null -ne $failure) {
    Write-Error $failure.Exception.Message
    exit 1
}

Write-Host "Windows release verification passed: $jsonReportPath"
exit 0
