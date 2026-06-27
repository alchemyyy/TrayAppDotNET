<#
.SYNOPSIS
Applies configured formatting and C# syntax styles across TrayAppDotNET.

.DESCRIPTION
Runs JetBrains CleanupCode with the built-in "Reformat & Apply Syntax Style"
profile. The solution's .editorconfig and .DotSettings files remain authoritative.
The shared .editorconfig is temporarily mounted at the solution root so projects
without a local copy receive the same rules. Vendor sources and build output are
excluded by default.

.EXAMPLE
.\cleanup.ps1

.EXAMPLE
.\cleanup.ps1 -NoBuild

.EXAMPLE
.\cleanup.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$SolutionPath = 'TrayAppDotNET.slnx',
    [string]$Profile = 'Built-in: Reformat & Apply Syntax Style',
    [string]$Include = '**/*.cs',
    [string]$Exclude = '**/vendor/**;**/bin/**;**/obj/**;**/.artifacts/**',
    [string]$Configuration = 'Debug',
    [string]$Platform = 'x64',
    [string]$SettingsPath,
    [string]$EditorConfigPath = 'TrayAppDotNETCommon\.editorconfig',
    [string]$CleanupCodePath,
    [string]$MSBuildPath,
    [switch]$NoBuild,
    [switch]$NoInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = $PSScriptRoot

function Resolve-RepositoryPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [switch]$MustExist
    )

    [string]$fullPath = if ([System.IO.Path]::IsPathRooted($Path)) {
        [System.IO.Path]::GetFullPath($Path)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
    }

    if ($MustExist -and -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "File does not exist: $Path"
    }

    return $fullPath
}

function Resolve-CleanupRunner {
    if (-not [string]::IsNullOrWhiteSpace($CleanupCodePath)) {
        [string]$executable = Resolve-RepositoryPath -Path $CleanupCodePath -MustExist
        [string]$fileName = [System.IO.Path]::GetFileNameWithoutExtension($executable)
        [string[]]$prefixArguments = if ($fileName -eq 'jb') { @('cleanupcode') } else { @() }

        return [pscustomobject]@{
            Executable      = $executable
            PrefixArguments = $prefixArguments
            DisplayName     = if ($fileName -eq 'jb') { 'jb cleanupcode' } else { $fileName }
        }
    }

    [System.Management.Automation.CommandInfo]$cleanupCodeCommand =
        Get-Command cleanupcode.exe -ErrorAction SilentlyContinue
    if ($null -ne $cleanupCodeCommand) {
        return [pscustomobject]@{
            Executable      = $cleanupCodeCommand.Source
            PrefixArguments = @()
            DisplayName     = 'cleanupcode.exe'
        }
    }

    [System.Management.Automation.CommandInfo]$jetBrainsCommand =
        Get-Command jb -ErrorAction SilentlyContinue
    if ($null -eq $jetBrainsCommand) {
        [string]$defaultJetBrainsPath = Join-Path $env:USERPROFILE '.dotnet\tools\jb.exe'
        if (Test-Path -LiteralPath $defaultJetBrainsPath -PathType Leaf) {
            $jetBrainsCommand = Get-Command $defaultJetBrainsPath
        }
    }

    if ($null -eq $jetBrainsCommand) {
        if ($NoInstall) {
            throw 'JetBrains CleanupCode is not installed. Install JetBrains.ReSharper.GlobalTools or omit -NoInstall.'
        }

        dotnet tool install --global JetBrains.ReSharper.GlobalTools | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Installing JetBrains.ReSharper.GlobalTools failed with exit code $LASTEXITCODE."
        }

        [string]$installedJetBrainsPath = Join-Path $env:USERPROFILE '.dotnet\tools\jb.exe'
        if (-not (Test-Path -LiteralPath $installedJetBrainsPath -PathType Leaf)) {
            throw "JetBrains CLI was installed but could not be found at $installedJetBrainsPath."
        }

        $jetBrainsCommand = Get-Command $installedJetBrainsPath
    }

    return [pscustomobject]@{
        Executable      = $jetBrainsCommand.Source
        PrefixArguments = @('cleanupcode')
        DisplayName     = 'jb cleanupcode'
    }
}

function Resolve-MSBuildExecutable {
    if (-not [string]::IsNullOrWhiteSpace($MSBuildPath)) {
        return Resolve-RepositoryPath -Path $MSBuildPath -MustExist
    }

    [string]$vsWherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vsWherePath -PathType Leaf) {
        # Full Visual Studio carries SDK resolver dependencies that may be absent
        # from a separately installed Build Tools instance
        [string[]]$visualStudioProductIDs = @(
            'Microsoft.VisualStudio.Product.Enterprise',
            'Microsoft.VisualStudio.Product.Professional',
            'Microsoft.VisualStudio.Product.Community',
            'Microsoft.VisualStudio.Product.BuildTools'
        )

        foreach ($productID in $visualStudioProductIDs) {
            [string]$installationPath = & $vsWherePath `
                -latest `
                -products $productID `
                -requires Microsoft.Component.MSBuild `
                -property installationPath

            if ([string]::IsNullOrWhiteSpace($installationPath)) {
                continue
            }

            [string[]]$installationCandidates = @(
                (Join-Path $installationPath 'MSBuild\Current\Bin\amd64\MSBuild.exe'),
                (Join-Path $installationPath 'MSBuild\Current\Bin\MSBuild.exe')
            )

            foreach ($candidate in $installationCandidates) {
                if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                    return $candidate
                }
            }
        }
    }

    [System.Management.Automation.CommandInfo]$MSBuildCommand =
        Get-Command MSBuild.exe -ErrorAction SilentlyContinue
    if ($null -ne $MSBuildCommand) {
        return $MSBuildCommand.Source
    }

    throw 'MSBuild.exe could not be found. Pass -MSBuildPath explicitly.'
}

[string]$resolvedSolutionPath = Resolve-RepositoryPath -Path $SolutionPath -MustExist
[string]$resolvedSettingsPath = if ([string]::IsNullOrWhiteSpace($SettingsPath)) {
    [string]::Empty
}
else {
    Resolve-RepositoryPath -Path $SettingsPath -MustExist
}
[string]$resolvedEditorConfigPath = if ([string]::IsNullOrWhiteSpace($EditorConfigPath)) {
    [string]::Empty
}
else {
    Resolve-RepositoryPath -Path $EditorConfigPath -MustExist
}

Write-Host "Solution: $resolvedSolutionPath"
Write-Host "Profile: $Profile"
Write-Host "Configuration: $Configuration"
Write-Host "Platform: $Platform"
Write-Host "Include: $Include"
Write-Host "Exclude: $Exclude"
if (-not [string]::IsNullOrWhiteSpace($resolvedSettingsPath)) {
    Write-Host "Settings: $resolvedSettingsPath"
}
if (-not [string]::IsNullOrWhiteSpace($resolvedEditorConfigPath)) {
    Write-Host "EditorConfig overlay: $resolvedEditorConfigPath"
}

if (-not $PSCmdlet.ShouldProcess($resolvedSolutionPath, "Run solution-wide code cleanup with '$Profile'")) {
    return
}

[pscustomobject]$cleanupRunner = Resolve-CleanupRunner
[string]$MSBuildExecutable = Resolve-MSBuildExecutable
[System.Collections.Generic.List[string]]$cleanupArguments = [System.Collections.Generic.List[string]]::new()

foreach ($prefixArgument in $cleanupRunner.PrefixArguments) {
    $cleanupArguments.Add($prefixArgument)
}

$cleanupArguments.Add("--profile=$Profile")
$cleanupArguments.Add("--toolset-path=$MSBuildExecutable")
$cleanupArguments.Add("--properties:Platform=$Platform;Configuration=$Configuration")
$cleanupArguments.Add('--verbosity=WARN')
$cleanupArguments.Add('--no-updates')

if (-not [string]::IsNullOrWhiteSpace($Include)) {
    $cleanupArguments.Add("--include=$Include")
}

if (-not [string]::IsNullOrWhiteSpace($Exclude)) {
    $cleanupArguments.Add("--exclude=$Exclude")
}

if (-not [string]::IsNullOrWhiteSpace($resolvedSettingsPath)) {
    $cleanupArguments.Add("--settings=$resolvedSettingsPath")
}

$cleanupArguments.Add('--no-build')
$cleanupArguments.Add($resolvedSolutionPath)

Write-Host "Runner: $($cleanupRunner.DisplayName)"
Write-Host "MSBuild: $MSBuildExecutable"

[string]$previousWorkloadResolver = $env:MSBuildEnableWorkloadResolver
[bool]$hadWorkloadResolver = Test-Path Env:MSBuildEnableWorkloadResolver
[int]$exitCode = 0
[bool]$locationPushed = $false
[string]$editorConfigDestination = Join-Path $repositoryRoot '.editorconfig'
[string]$editorConfigBackup = [string]::Empty
[bool]$editorConfigOverlayCreated = $false

try {
    $env:MSBuildEnableWorkloadResolver = 'false'
    Push-Location -LiteralPath $repositoryRoot
    $locationPushed = $true

    if (-not $NoBuild) {
        Write-Host "Prebuilding $Configuration|$Platform before applying the EditorConfig overlay"
        & $MSBuildExecutable `
            $resolvedSolutionPath `
            '/restore' `
            '/m' `
            '/verbosity:minimal' `
            "/p:Configuration=$Configuration" `
            "/p:Platform=$Platform"

        [int]$buildExitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
        if ($buildExitCode -ne 0) {
            throw "Pre-cleanup build failed with exit code $buildExitCode."
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($resolvedEditorConfigPath) -and
        -not [System.StringComparer]::OrdinalIgnoreCase.Equals(
            $resolvedEditorConfigPath,
            $editorConfigDestination)) {
        if (Test-Path -LiteralPath $editorConfigDestination -PathType Leaf) {
            $editorConfigBackup = Join-Path $repositoryRoot ".editorconfig.cleanup-backup.$([guid]::NewGuid().ToString('N'))"
            Move-Item -LiteralPath $editorConfigDestination -Destination $editorConfigBackup
        }

        Copy-Item -LiteralPath $resolvedEditorConfigPath -Destination $editorConfigDestination
        $editorConfigOverlayCreated = $true
    }

    & $cleanupRunner.Executable @cleanupArguments
    $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
}
finally {
    if ($locationPushed) {
        Pop-Location
    }

    if ($editorConfigOverlayCreated -and
        (Test-Path -LiteralPath $editorConfigDestination -PathType Leaf)) {
        Remove-Item -LiteralPath $editorConfigDestination -Force
    }

    if (-not [string]::IsNullOrWhiteSpace($editorConfigBackup) -and
        (Test-Path -LiteralPath $editorConfigBackup -PathType Leaf)) {
        if (Test-Path -LiteralPath $editorConfigDestination -PathType Leaf) {
            Remove-Item -LiteralPath $editorConfigDestination -Force
        }

        Move-Item -LiteralPath $editorConfigBackup -Destination $editorConfigDestination
    }

    if ($hadWorkloadResolver) {
        $env:MSBuildEnableWorkloadResolver = $previousWorkloadResolver
    }
    else {
        Remove-Item Env:MSBuildEnableWorkloadResolver -ErrorAction SilentlyContinue
    }
}

if ($exitCode -ne 0) {
    throw "CleanupCode failed with exit code $exitCode."
}
