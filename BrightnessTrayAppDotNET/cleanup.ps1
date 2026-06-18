[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Alias('ProjectPath')]
    [string]$TargetPath,
    [string]$Include = '**/*.cs',
    [string]$SettingsPath = 'CleanupCode.DotSettings',
    [string]$Profile = 'CodeStyleIssuesAndLanguageUsage',
    [string]$EditorConfigOverlayPath,
    [string]$EditorConfigOverlayDirectory = '.',
    [string]$CleanupCodeExe,
    [switch]$NoBuild,
    [switch]$NoInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot

function ConvertTo-RelativePath {
    param([Parameter(Mandatory = $true)][string]$FullPath)

    $rootPath = [System.IO.Path]::GetFullPath($root)
    if (-not $rootPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $rootPath += [System.IO.Path]::DirectorySeparatorChar
    }

    $rootUri = [System.Uri]::new($rootPath)
    $pathUri = [System.Uri]::new([System.IO.Path]::GetFullPath($FullPath))
    $relativePath = $rootUri.MakeRelativeUri($pathUri).ToString()
    $relativePath = [System.Uri]::UnescapeDataString($relativePath)
    $relativePath = $relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)

    if ([string]::IsNullOrEmpty($relativePath)) {
        return '.'
    }

    return $relativePath
}

function Resolve-RepoPathInfo {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [switch]$MustExist
    )

    $fullPath = if ([System.IO.Path]::IsPathRooted($Path)) {
        [System.IO.Path]::GetFullPath($Path)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $root $Path))
    }

    if ($MustExist -and -not (Test-Path -LiteralPath $fullPath)) {
        throw "Path does not exist: $Path"
    }

    [pscustomobject]@{
        Full     = $fullPath
        Relative = ConvertTo-RelativePath -FullPath $fullPath
    }
}

function Get-DefaultTargetPath {
    $solutions = @(Get-ChildItem -LiteralPath $root -Filter '*.slnx' -File)
    if ($solutions.Count -eq 1) {
        return (Resolve-RepoPathInfo -Path $solutions[0].FullName -MustExist).Relative
    }

    if ($solutions.Count -gt 1) {
        $solutionList = ($solutions | ForEach-Object { $_.Name }) -join ', '
        throw "Multiple solution files found ($solutionList). Pass -TargetPath explicitly."
    }

    $projects = @(
        Get-ChildItem -LiteralPath $root -Filter '*.csproj' -File -Recurse |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
    )

    if ($projects.Count -eq 1) {
        return (Resolve-RepoPathInfo -Path $projects[0].FullName -MustExist).Relative
    }

    if ($projects.Count -gt 1) {
        $projectList = ($projects | ForEach-Object { (Resolve-RepoPathInfo -Path $_.FullName -MustExist).Relative }) -join ', '
        throw "No solution file found and multiple project files found ($projectList). Pass -TargetPath explicitly."
    }

    throw 'No solution or project file found. Pass -TargetPath explicitly.'
}

$targetInfo = Resolve-RepoPathInfo -Path $(if ([string]::IsNullOrWhiteSpace($TargetPath)) { Get-DefaultTargetPath } else { $TargetPath }) -MustExist
$settingsInfo = Resolve-RepoPathInfo -Path $SettingsPath -MustExist

function Resolve-CleanupRunner {
    if ($CleanupCodeExe) {
        $resolved = Resolve-Path -LiteralPath $CleanupCodeExe
        return [pscustomobject]@{
            Exe  = $resolved.Path
            Args = @()
            Name = 'cleanupcode.exe'
        }
    }

    $cleanupCode = Get-Command cleanupcode.exe -ErrorAction SilentlyContinue
    if ($cleanupCode) {
        return [pscustomobject]@{
            Exe  = $cleanupCode.Source
            Args = @()
            Name = 'cleanupcode.exe'
        }
    }

    $jb = Get-Command jb -ErrorAction SilentlyContinue
    if (-not $jb) {
        $jbPath = Join-Path $env:USERPROFILE '.dotnet\tools\jb.exe'
        if (Test-Path -LiteralPath $jbPath -PathType Leaf) {
            $jb = [pscustomobject]@{ Source = $jbPath }
        }
    }

    if (-not $jb) {
        if ($NoInstall) {
            throw 'Could not find cleanupcode.exe or jb. Install JetBrains ReSharper Command Line Tools, or rerun without -NoInstall to install JetBrains.ReSharper.GlobalTools.'
        }

        dotnet tool install -g JetBrains.ReSharper.GlobalTools
        $jbPath = Join-Path $env:USERPROFILE '.dotnet\tools\jb.exe'
        $jb = [pscustomobject]@{ Source = $jbPath }
    }

    return [pscustomobject]@{
        Exe  = $jb.Source
        Args = @('cleanupcode')
        Name = 'jb cleanupcode'
    }
}

$runner = Resolve-CleanupRunner
$targetPath = $targetInfo.Relative
$settingsPath = $settingsInfo.Relative

# Use solution mode by default. CleanupCode limits custom profiles in
# solution-less project mode to reformat stages, which would skip the
# redundancy/import stages this profile is meant to run.
$arguments = @(
    $runner.Args
    "--settings=$settingsPath"
    "--profile=$Profile"
    '--verbosity=INFO'
    '--no-updates'
)

if ($Include) {
    $arguments += "--include=$Include"
}

if ($NoBuild) {
    $arguments += '--no-build'
}

$arguments += $targetPath

Write-Host "Running $($runner.Name) on $targetPath"
Write-Host "Settings: $settingsPath"
Write-Host "Profile: $Profile"
if ($Include) {
    Write-Host "Include: $Include"
}

if ($EditorConfigOverlayPath) {
    Write-Host "EditorConfig overlay: $((Resolve-RepoPathInfo -Path $EditorConfigOverlayPath -MustExist).Relative)"
}

if (-not $PSCmdlet.ShouldProcess($targetPath, "Run $($runner.Name) with '$Profile'")) {
    return
}

$overlayDirectoryInfo = if ($EditorConfigOverlayPath) { Resolve-RepoPathInfo -Path $EditorConfigOverlayDirectory -MustExist } else { $null }
$overlayDestination = if ($overlayDirectoryInfo) { Join-Path $overlayDirectoryInfo.Full '.editorconfig' } else { $null }
$overlayBackup = $null
$overlayApplied = $false
$pushedLocation = $false

try {
    if ($EditorConfigOverlayPath) {
        $overlaySourceInfo = Resolve-RepoPathInfo -Path $EditorConfigOverlayPath -MustExist

        if (-not [System.StringComparer]::OrdinalIgnoreCase.Equals($overlaySourceInfo.Full, $overlayDestination)) {
            if (Test-Path -LiteralPath $overlayDestination -PathType Leaf) {
                $overlayBackup = Join-Path $overlayDirectoryInfo.Full ".editorconfig.cleanup-backup.$([guid]::NewGuid().ToString('N'))"
                Move-Item -LiteralPath $overlayDestination -Destination $overlayBackup
            }

            Copy-Item -LiteralPath $overlaySourceInfo.Full -Destination $overlayDestination
            $overlayApplied = $true
        }
    }

    Push-Location -LiteralPath $root
    $pushedLocation = $true
    & $runner.Exe @arguments
    $exitCode = if ($LASTEXITCODE -ne $null) { $LASTEXITCODE } else { 0 }
}
finally {
    if ($pushedLocation) {
        Pop-Location
    }

    if ($overlayApplied) {
        if (Test-Path -LiteralPath $overlayDestination -PathType Leaf) {
            Remove-Item -LiteralPath $overlayDestination -Force
        }

        if ($overlayBackup -and (Test-Path -LiteralPath $overlayBackup -PathType Leaf)) {
            Move-Item -LiteralPath $overlayBackup -Destination $overlayDestination
        }
    }
}

if ($exitCode -ne 0) {
    throw "CleanupCode failed with exit code $exitCode."
}
