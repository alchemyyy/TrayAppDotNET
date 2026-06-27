<#
.SYNOPSIS
Removes non-interactive, review-safe C# redundancies across TrayAppDotNET.

.DESCRIPTION
Runs the "Safe Code Redundancies" ReSharper CleanupCode profile from
QualityCleanup.DotSettings. The profile only enables removal of code
redundancies. Behavior-sensitive inspections remain disabled for this cleanup,
and RedundantArgumentDefaultValue is explicitly blacklisted so call-site intent
is preserved.

The existing cleanup.ps1 owns tool discovery, the pre-cleanup build, settings
overlay handling, and vendor/build-output exclusions. Unless -NoVerify is used,
this wrapper serially builds and tests the solution after cleanup.

.EXAMPLE
.\quality-cleanup.ps1 -WhatIf

.EXAMPLE
.\quality-cleanup.ps1

.EXAMPLE
.\quality-cleanup.ps1 -NoBuild -NoVerify -NoInstall
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$SolutionPath = 'TrayAppDotNET.slnx',
    [string]$Include = '**/*.cs',
    [string]$Exclude = '**/vendor/**;**/bin/**;**/obj/**;**/.artifacts/**',
    [string]$Configuration = 'Debug',
    [string]$Platform = 'x64',
    [string]$EditorConfigPath = 'TrayAppDotNETCommon\.editorconfig',
    [string]$CleanupCodePath,
    [string]$MSBuildPath,
    [switch]$NoBuild,
    [switch]$NoVerify,
    [switch]$NoInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

[string]$repositoryRoot = $PSScriptRoot
[string]$cleanupScriptPath = Join-Path $repositoryRoot 'cleanup.ps1'
[string]$settingsPath = Join-Path $repositoryRoot 'QualityCleanup.DotSettings'

if (-not (Test-Path -LiteralPath $cleanupScriptPath -PathType Leaf)) {
    throw "Base cleanup script not found: $cleanupScriptPath"
}

if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    throw "Quality cleanup settings not found: $settingsPath"
}

[hashtable]$cleanupParameters = @{
    SolutionPath     = $SolutionPath
    Profile          = 'Safe Code Redundancies'
    Include          = $Include
    Exclude          = $Exclude
    Configuration    = $Configuration
    Platform         = $Platform
    SettingsPath     = $settingsPath
    EditorConfigPath = $EditorConfigPath
}

if (-not [string]::IsNullOrWhiteSpace($CleanupCodePath)) {
    $cleanupParameters.CleanupCodePath = $CleanupCodePath
}

if (-not [string]::IsNullOrWhiteSpace($MSBuildPath)) {
    $cleanupParameters.MSBuildPath = $MSBuildPath
}

if ($NoBuild) {
    $cleanupParameters.NoBuild = $true
}

if ($NoInstall) {
    $cleanupParameters.NoInstall = $true
}

if ($WhatIfPreference) {
    $cleanupParameters.WhatIf = $true
}

if ($PSBoundParameters.ContainsKey('Confirm')) {
    $cleanupParameters.Confirm = $PSBoundParameters['Confirm']
}

Write-Host 'Blacklisted inspection: RedundantArgumentDefaultValue'
& $cleanupScriptPath @cleanupParameters

if ($WhatIfPreference -or $NoVerify) {
    return
}

[string]$resolvedSolutionPath = if ([System.IO.Path]::IsPathRooted($SolutionPath)) {
    [System.IO.Path]::GetFullPath($SolutionPath)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $SolutionPath))
}

if (-not (Test-Path -LiteralPath $resolvedSolutionPath -PathType Leaf)) {
    throw "Solution file not found: $resolvedSolutionPath"
}

[System.Management.Automation.CommandInfo]$dotnetCommand =
    Get-Command dotnet -ErrorAction Stop

Write-Host "Verifying post-cleanup build: $Configuration|$Platform"
& $dotnetCommand.Source `
    build `
    $resolvedSolutionPath `
    '--configuration' `
    $Configuration `
    "--property:Platform=$Platform" `
    '--no-restore' `
    '--maxcpucount:1'

[int]$buildExitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
if ($buildExitCode -ne 0) {
    throw "Post-cleanup build failed with exit code $buildExitCode."
}

Write-Host 'Running post-cleanup tests'
& $dotnetCommand.Source `
    test `
    $resolvedSolutionPath `
    '--configuration' `
    $Configuration `
    "--property:Platform=$Platform" `
    '--no-build' `
    '--no-restore' `
    '--maxcpucount:1'

[int]$testExitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
if ($testExitCode -ne 0) {
    throw "Post-cleanup tests failed with exit code $testExitCode."
}
