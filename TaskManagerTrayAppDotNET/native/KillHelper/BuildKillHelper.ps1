[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Sidecar', 'NativeAOT')]
    [string]$BuildKind,

    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $false)]
    [string]$MSBuildPath = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

[string]$VisualCppComponentID = 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64'
[string]$MSBuildRelativePath = 'MSBuild\Current\Bin\amd64\MSBuild.exe'
[string]$MSBuildX86RelativePath = 'MSBuild\Current\Bin\MSBuild.exe'
[string]$VcVarsRelativePath = 'VC\Auxiliary\Build\vcvars64.bat'

function Find-MSBuildInInstallation {
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallationPath
    )

    [string[]]$candidatePaths = @(
        (Join-Path $InstallationPath $MSBuildRelativePath),
        (Join-Path $InstallationPath $MSBuildX86RelativePath)
    )
    [string]$vcVarsPath = Join-Path $InstallationPath $VcVarsRelativePath
    if (-not (Test-Path -LiteralPath $vcVarsPath -PathType Leaf)) {
        return [string]::Empty
    }

    foreach ($candidatePath in $candidatePaths) {
        if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
            return $candidatePath
        }
    }

    return [string]::Empty
}

function Resolve-MSBuildExecutable {
    if (-not [string]::IsNullOrWhiteSpace($MSBuildPath)) {
        [string]$explicitPath = [System.IO.Path]::GetFullPath($MSBuildPath)
        if (-not (Test-Path -LiteralPath $explicitPath -PathType Leaf)) {
            throw "MSBuild.exe was not found at $explicitPath."
        }

        return $explicitPath
    }

    if (-not [string]::IsNullOrWhiteSpace($env:VSINSTALLDIR)) {
        [string]$environmentMSBuild = Find-MSBuildInInstallation -InstallationPath $env:VSINSTALLDIR
        if (-not [string]::IsNullOrWhiteSpace($environmentMSBuild)) {
            return $environmentMSBuild
        }
    }

    [string]$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
    [string]$portableBuildToolsRoot = Join-Path $repositoryRoot '.buildtools\VSBuildTools'
    [string]$portableMSBuild = Find-MSBuildInInstallation -InstallationPath $portableBuildToolsRoot
    if (-not [string]::IsNullOrWhiteSpace($portableMSBuild)) {
        return $portableMSBuild
    }

    [string]$programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    [string]$vsWherePath = Join-Path $programFilesX86 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vsWherePath -PathType Leaf) {
        [string]$installationPath = & $vsWherePath `
            -latest `
            -products * `
            -requires $VisualCppComponentID `
            -property installationPath

        if (($LASTEXITCODE -eq 0) -and (-not [string]::IsNullOrWhiteSpace($installationPath))) {
            [string]$discoveredMSBuild = Find-MSBuildInInstallation -InstallationPath $installationPath
            if (-not [string]::IsNullOrWhiteSpace($discoveredMSBuild)) {
                return $discoveredMSBuild
            }
        }
    }

    [System.Management.Automation.CommandInfo]$msBuildCommand =
        Get-Command MSBuild.exe -ErrorAction SilentlyContinue
    if ($null -ne $msBuildCommand) {
        return $msBuildCommand.Source
    }

    throw 'MSBuild.exe with the Visual C++ x64 tools could not be found. Install the Visual Studio C++ Build Tools workload.'
}

[string]$resolvedProjectPath = [System.IO.Path]::GetFullPath($ProjectPath)
if (-not (Test-Path -LiteralPath $resolvedProjectPath -PathType Leaf)) {
    throw "The kill helper project was not found at $resolvedProjectPath."
}

[string]$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
[void][System.IO.Directory]::CreateDirectory($resolvedOutputDirectory)
[string]$resolvedMSBuildPath = Resolve-MSBuildExecutable

[string[]]$arguments = @(
    $resolvedProjectPath,
    '-nologo',
    '-m:1',
    '-target:Build',
    "-property:Configuration=$Configuration",
    '-property:Platform=x64',
    "-property:KillHelperBuildKind=$BuildKind",
    "-property:KillHelperOutputDirectory=$resolvedOutputDirectory",
    '-verbosity:minimal'
)

& $resolvedMSBuildPath @arguments
[int]$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    throw "Building the Task Manager kill helper failed with exit code $exitCode."
}
