Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

[string]$RepositoryRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Get-Location).Path
}

[string]$BuildToolsRoot = Join-Path $RepositoryRoot '.buildtools'
[string]$DownloadsRoot = Join-Path $BuildToolsRoot 'downloads'
[string]$DotNetInstallRoot = Join-Path $BuildToolsRoot 'dotnet'
[string]$DotNetExecutable = Join-Path $DotNetInstallRoot 'dotnet.exe'
[string]$NuGetPackageRoot = Join-Path $BuildToolsRoot 'nuget'
[string]$NativeAotInstallRoot = Join-Path $BuildToolsRoot 'VSBuildTools'
[string]$TempRoot = Join-Path $BuildToolsRoot 'temp'
[string]$RequiredDotNetMajorVersion = '10'
[string]$RuntimeIdentifier = 'win-x64'
[string]$BuildTypeDebug = 'Debug'
[string]$BuildTypeNativeAot = 'NativeAot'
[string]$ConfigurationDebug = 'Debug'
[string]$ConfigurationRelease = 'Release'
[string]$PlatformProperty = '-p:Platform=x64'
[string]$SkipKillProperty = '-p:SkipKillRunningInstance=true'
[string]$SkipPublishProperty = '-p:SkipPublishAfterBuild=true'
[string]$DisableAggregateBuildProperty = '-p:TrayAppDotNETAggregateBuild=false'
[string]$DisableRootOutputProperty = '-p:TrayAppDotNETUseRootOutput=false'
[string]$DisableSharedReferencesProperty = '-p:TrayAppDotNETBuildSharedProjectReferences=false'
[string]$PublishAotProperty = '-p:PublishAot=true'
[string]$AllowIlcWarningsProperty = '-p:IlcTreatWarningsAsErrors=false'
[string]$NoLogoArgument = '--nologo'
[string]$NoRestoreArgument = '--no-restore'
[string]$InvalidListMessage = 'That list input was invalid.'
[string]$InvalidBuildTypeMessage = 'That build type input was invalid.'
[string]$XmlSourceGeneratorProjectRelativePath = 'TrayAppDotNETCommon\generators\XmlSourceGenerator\TrayAppDotNETCommon.XmlSourceGenerator.csproj'
[string]$CommonProjectRelativePath = 'TrayAppDotNETCommon\src\TrayAppDotNETCommon.csproj'
[string]$VisualStudioInstallerRelativePath = 'Microsoft Visual Studio\Installer\vswhere.exe'
[string]$VcVarsRelativePath = 'VC\Auxiliary\Build\vcvars64.bat'
[string]$VisualStudioVcToolsComponentId = 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64'
[string]$Windows11SdkComponentId = 'Microsoft.VisualStudio.Component.Windows11SDK.22621'
[string]$VisualStudioVcRedistComponentId = 'Microsoft.VisualStudio.Component.VC.Redist.14.Latest'
[string]$VisualStudioBuildToolsInstallerUrl = 'https://aka.ms/vs/17/release/vs_BuildTools.exe'
[string]$BuildStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
[string]$BuildArtifactsRoot = Join-Path $RepositoryRoot ".artifacts\build\$BuildStamp"
[string]$BuildLogRoot = Join-Path $BuildArtifactsRoot 'logs'

[string[]]$AppNames = @(
    'BatteryTrayAppDotNET',
    'BrightnessTrayAppDotNET',
    'FanControlTrayAppDotNET',
    'NetworkTrayAppDotNET',
    'VolumeTrayAppDotNET'
)

[object[]]$AppDefinitions = @()
for ([int]$appIndex = 0; $appIndex -lt $AppNames.Count; $appIndex++) {
    [string]$appName = $AppNames[$appIndex]
    $AppDefinitions += [pscustomobject]@{
        Number = $appIndex + 1
        Name = $appName
        ProjectRelativePath = "$appName\src\$appName.csproj"
    }
}

function Initialize-BuildDirectories {
    [string[]]$directoryPaths = @(
        $BuildToolsRoot,
        $DownloadsRoot,
        $DotNetInstallRoot,
        $NuGetPackageRoot,
        $TempRoot,
        $BuildArtifactsRoot,
        $BuildLogRoot
    )

    foreach ($directoryPath in $directoryPaths) {
        if (-not (Test-Path -LiteralPath $directoryPath)) {
            [void](New-Item -ItemType Directory -Path $directoryPath)
        }
    }
}

function Set-BuildEnvironment {
    param(
        [Parameter(Mandatory = $false)]
        [string]$DotNetPath = ''
    )

    [string]$currentPath = [Environment]::GetEnvironmentVariable('PATH', 'Process')
    if (-not [string]::IsNullOrWhiteSpace($DotNetPath)) {
        [string]$dotNetRoot = Split-Path -Parent $DotNetPath
        [string[]]$pathParts = $currentPath.Split(';')
        if ($pathParts -notcontains $dotNetRoot) {
            [Environment]::SetEnvironmentVariable('PATH', "$dotNetRoot;$currentPath", 'Process')
        }

        [Environment]::SetEnvironmentVariable('DOTNET_ROOT', $dotNetRoot, 'Process')
    }

    [Environment]::SetEnvironmentVariable('DOTNET_CLI_HOME', $BuildToolsRoot, 'Process')
    [Environment]::SetEnvironmentVariable('DOTNET_NOLOGO', '1', 'Process')
    [Environment]::SetEnvironmentVariable('DOTNET_SKIP_FIRST_TIME_EXPERIENCE', '1', 'Process')

    [string]$nuGetPackagesPath = $NuGetPackageRoot
    if (-not $nuGetPackagesPath.EndsWith('\')) {
        $nuGetPackagesPath = "$nuGetPackagesPath\"
    }

    [Environment]::SetEnvironmentVariable('NUGET_PACKAGES', $nuGetPackagesPath, 'Process')
}

function Enable-Tls12 {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}

function Read-Confirmation {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prompt
    )

    [string]$response = Read-Host "$Prompt [y/N]"
    switch -Regex ($response.Trim()) {
        '^(y|yes)$' {
            return $true
        }
        default {
            return $false
        }
    }
}

function Get-CommandPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandName
    )

    $command = Get-Command -Name $CommandName -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $command) {
        return $null
    }

    return $command.Source
}

function Get-DotNetSdkInfo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath
    )

    if (-not (Test-Path -LiteralPath $ExecutablePath)) {
        return $null
    }

    [string[]]$sdkLines = & $ExecutablePath --list-sdks 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    [string[]]$matchingVersions = @()
    foreach ($sdkLine in $sdkLines) {
        if ($sdkLine -match "^$RequiredDotNetMajorVersion\.") {
            [string]$sdkVersion = ($sdkLine -split '\s+')[0]
            $matchingVersions += $sdkVersion
        }
    }

    if ($matchingVersions.Count -eq 0) {
        return $null
    }

    return [pscustomobject]@{
        Found = $true
        Path = $ExecutablePath
        Version = $matchingVersions[$matchingVersions.Count - 1]
    }
}

function Resolve-DotNetSdk {
    [string[]]$checkedPaths = @()
    $dotNetCommands = @(Get-Command -Name 'dotnet.exe' -All -ErrorAction SilentlyContinue)
    foreach ($dotNetCommand in $dotNetCommands) {
        [string]$dotNetCommandPath = $dotNetCommand.Source
        if ([string]::IsNullOrWhiteSpace($dotNetCommandPath) -or ($checkedPaths -contains $dotNetCommandPath)) {
            continue
        }

        $checkedPaths += $dotNetCommandPath
        $externalSdkInfo = Get-DotNetSdkInfo $dotNetCommandPath
        if ($null -ne $externalSdkInfo) {
            return $externalSdkInfo
        }
    }

    if ($checkedPaths -notcontains $DotNetExecutable) {
        $localSdkInfo = Get-DotNetSdkInfo $DotNetExecutable
        if ($null -ne $localSdkInfo) {
            return $localSdkInfo
        }
    }

    return [pscustomobject]@{
        Found = $false
        Path = ''
        Version = ''
    }
}

function Install-DotNetSdk {
    Enable-Tls12

    [string]$dotNetInstallScriptPath = Join-Path $DownloadsRoot 'dotnet-install.ps1'
    [string]$dotNetInstallScriptUrl = 'https://dot.net/v1/dotnet-install.ps1'

    if (-not (Test-Path -LiteralPath $dotNetInstallScriptPath)) {
        Write-Host "Downloading .NET install script to $dotNetInstallScriptPath"
        Invoke-WebRequest -UseBasicParsing -Uri $dotNetInstallScriptUrl -OutFile $dotNetInstallScriptPath
    }

    Write-Host "Installing .NET $RequiredDotNetMajorVersion SDK to $DotNetInstallRoot"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $dotNetInstallScriptPath `
        -Channel "$RequiredDotNetMajorVersion.0" `
        -Quality 'GA' `
        -InstallDir $DotNetInstallRoot `
        -Architecture 'x64' `
        -NoPath

    if ($LASTEXITCODE -ne 0) {
        throw ".NET SDK install failed with exit code $LASTEXITCODE."
    }

    $installedSdkInfo = Get-DotNetSdkInfo $DotNetExecutable
    if ($null -eq $installedSdkInfo) {
        throw ".NET $RequiredDotNetMajorVersion SDK was not found after install."
    }

    return $installedSdkInfo
}

function Get-UniqueExistingDirectories {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$DirectoryPaths
    )

    [string[]]$existingDirectories = @()
    foreach ($directoryPath in $DirectoryPaths) {
        if ([string]::IsNullOrWhiteSpace($directoryPath)) {
            continue
        }

        if (-not (Test-Path -LiteralPath $directoryPath -PathType Container)) {
            continue
        }

        [string]$resolvedDirectoryPath = (Resolve-Path -LiteralPath $directoryPath).Path
        if ($existingDirectories -notcontains $resolvedDirectoryPath) {
            $existingDirectories += $resolvedDirectoryPath
        }
    }

    return $existingDirectories
}

function Get-ProgramFilesRoots {
    [string[]]$environmentVariableNames = @(
        'ProgramFiles(x86)',
        'ProgramFiles'
    )

    [string[]]$candidatePaths = @()
    foreach ($environmentVariableName in $environmentVariableNames) {
        $candidatePaths += [Environment]::GetEnvironmentVariable($environmentVariableName)
    }

    return Get-UniqueExistingDirectories -DirectoryPaths $candidatePaths
}

function Find-FirstExistingFile {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$FilePaths
    )

    foreach ($filePath in ($FilePaths | Select-Object -Unique)) {
        if ((-not [string]::IsNullOrWhiteSpace($filePath)) -and (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            return $filePath
        }
    }

    return $null
}

function Get-VsWherePath {
    [string]$vsWhereCommandPath = Get-CommandPath 'vswhere.exe'
    if (-not [string]::IsNullOrWhiteSpace($vsWhereCommandPath)) {
        return $vsWhereCommandPath
    }

    [string[]]$candidatePaths = @()
    foreach ($programFilesRoot in (Get-ProgramFilesRoots)) {
        $candidatePaths += (Join-Path $programFilesRoot $VisualStudioInstallerRelativePath)
    }

    return Find-FirstExistingFile -FilePaths $candidatePaths
}

function Get-VisualStudioInstallRoots {
    [string[]]$installRoots = @($NativeAotInstallRoot)

    if (-not [string]::IsNullOrWhiteSpace($env:VSINSTALLDIR)) {
        $installRoots += $env:VSINSTALLDIR
    }

    [string]$vsWherePath = Get-VsWherePath
    if (-not [string]::IsNullOrWhiteSpace($vsWherePath)) {
        [string[]]$vsWhereInstallRoots = & $vsWherePath -products * -requires $VisualStudioVcToolsComponentId -property installationPath 2>$null
        $installRoots += $vsWhereInstallRoots
    }

    return Get-UniqueExistingDirectories -DirectoryPaths $installRoots
}

function Get-VcVarsCandidatePaths {
    [string[]]$candidatePaths = @()
    foreach ($installRoot in (Get-VisualStudioInstallRoots)) {
        $candidatePaths += (Join-Path $installRoot $VcVarsRelativePath)
    }

    return $candidatePaths | Select-Object -Unique
}

function Get-NativeAotToolchain {
    [string[]]$vcVarsCandidatePaths = Get-VcVarsCandidatePaths
    foreach ($vcVarsCandidatePath in $vcVarsCandidatePaths) {
        if (Test-Path -LiteralPath $vcVarsCandidatePath) {
            return [pscustomobject]@{
                Found = $true
                Kind = 'VsDevEnvironment'
                Path = $vcVarsCandidatePath
            }
        }
    }

    [string]$clPath = Get-CommandPath 'cl.exe'
    [string]$linkPath = Get-CommandPath 'link.exe'
    [string]$libPath = Get-CommandPath 'lib.exe'
    if ((-not [string]::IsNullOrWhiteSpace($clPath)) -and
        (-not [string]::IsNullOrWhiteSpace($linkPath)) -and
        (-not [string]::IsNullOrWhiteSpace($libPath))) {
        return [pscustomobject]@{
            Found = $true
            Kind = 'Path'
            Path = "cl=$clPath; link=$linkPath; lib=$libPath"
        }
    }

    return [pscustomobject]@{
        Found = $false
        Kind = ''
        Path = ''
    }
}

function Install-NativeAotToolchain {
    Enable-Tls12

    [string]$buildToolsInstallerPath = Join-Path $DownloadsRoot 'vs_BuildTools.exe'

    if (-not (Test-Path -LiteralPath $buildToolsInstallerPath)) {
        Write-Host "Downloading Visual Studio Build Tools bootstrapper to $buildToolsInstallerPath"
        Invoke-WebRequest -UseBasicParsing -Uri $VisualStudioBuildToolsInstallerUrl -OutFile $buildToolsInstallerPath
    }

    if (-not (Test-Path -LiteralPath $NativeAotInstallRoot)) {
        [void](New-Item -ItemType Directory -Path $NativeAotInstallRoot)
    }

    Write-Host "Installing native build tools to $NativeAotInstallRoot"
    Write-Host 'This may require elevation. It installs C++ Build Tools and the Windows 11 SDK, not the Visual Studio IDE.'

    [string[]]$installerArguments = @(
        '--passive',
        '--wait',
        '--norestart',
        '--nocache',
        '--installPath',
        $NativeAotInstallRoot,
        '--add',
        $VisualStudioVcToolsComponentId,
        '--add',
        $Windows11SdkComponentId,
        '--add',
        $VisualStudioVcRedistComponentId
    )

    & $buildToolsInstallerPath @installerArguments
    [int]$exitCode = $LASTEXITCODE
    if (($exitCode -ne 0) -and ($exitCode -ne 3010)) {
        throw "Visual Studio Build Tools install failed with exit code $exitCode."
    }

    $nativeAotToolchain = Get-NativeAotToolchain
    if (-not $nativeAotToolchain.Found) {
        throw 'Native AOT toolchain was not found after Visual Studio Build Tools install.'
    }

    return $nativeAotToolchain
}

function Write-PrerequisiteReport {
    param(
        [Parameter(Mandatory = $true)]
        $DotNetSdkInfo,

        [Parameter(Mandatory = $true)]
        $NativeAotToolchain
    )

    Write-Host 'Prerequisite report:'
    Write-Host ("- PowerShell: Found {0}" -f $PSVersionTable.PSVersion)

    if ($DotNetSdkInfo.Found) {
        Write-Host ("- .NET {0} SDK: Found {1} at {2}" -f $RequiredDotNetMajorVersion, $DotNetSdkInfo.Version, $DotNetSdkInfo.Path)
    } else {
        Write-Host ("- .NET {0} SDK: Missing" -f $RequiredDotNetMajorVersion)
    }

    if ($NativeAotToolchain.Found) {
        Write-Host ("- Native AOT C++ toolchain: Found ({0}) at {1}" -f $NativeAotToolchain.Kind, $NativeAotToolchain.Path)
    } else {
        Write-Host '- Native AOT C++ toolchain: Missing (required only for Native AOT builds)'
    }

    Write-Host ("- Build tools folder: {0}" -f $BuildToolsRoot)
    Write-Host ''
}

function Read-AppSelection {
    Write-Host 'Available apps to build:'
    foreach ($appDefinition in $AppDefinitions) {
        Write-Host ("{0}. {1}" -f $appDefinition.Number, $appDefinition.Name)
    }

    Write-Host 'Reply with a comma-delimited list like this: 1,2,4,5'
    [string]$selectionInput = Read-Host 'Apps'
    if ([string]::IsNullOrWhiteSpace($selectionInput)) {
        Write-Host $InvalidListMessage
        exit 1
    }

    if ($selectionInput -notmatch '^\s*\d+\s*(,\s*\d+\s*)*$') {
        Write-Host $InvalidListMessage
        exit 1
    }

    [string[]]$selectionParts = $selectionInput.Split(',')
    [int[]]$selectedNumbers = @()
    foreach ($selectionPart in $selectionParts) {
        [int]$selectedNumber = 0
        if (-not [int]::TryParse($selectionPart.Trim(), [ref]$selectedNumber)) {
            Write-Host $InvalidListMessage
            exit 1
        }

        if ($selectedNumber -lt 1 -or $selectedNumber -gt $AppDefinitions.Count) {
            Write-Host $InvalidListMessage
            exit 1
        }

        if ($selectedNumbers -contains $selectedNumber) {
            Write-Host $InvalidListMessage
            exit 1
        }

        $selectedNumbers += $selectedNumber
    }

    $selectedApps = @()
    foreach ($selectedNumber in $selectedNumbers) {
        $selectedApps += ($AppDefinitions | Where-Object { $_.Number -eq $selectedNumber })
    }

    return $selectedApps
}

function Read-BuildType {
    Write-Host ''
    Write-Host 'Build type:'
    Write-Host '1. Debug build'
    Write-Host '2. Native AOT build'
    [string]$buildTypeInput = Read-Host 'Build type'

    switch ($buildTypeInput.Trim()) {
        '1' {
            return $BuildTypeDebug
        }
        '2' {
            return $BuildTypeNativeAot
        }
        default {
            Write-Host $InvalidBuildTypeMessage
            exit 1
        }
    }
}

function Get-BuildConfiguration {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BuildType
    )

    switch ($BuildType) {
        $BuildTypeDebug {
            return $ConfigurationDebug
        }
        $BuildTypeNativeAot {
            return $ConfigurationRelease
        }
        default {
            throw "Unknown build type: $BuildType"
        }
    }
}

function Get-AppOutputDirectory {
    param(
        [Parameter(Mandatory = $true)]
        $AppDefinition,

        [Parameter(Mandatory = $true)]
        [string]$BuildType
    )

    return (Join-Path $BuildArtifactsRoot "isolated\$BuildType\$($AppDefinition.Name)")
}

function Get-MergedOutputDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BuildType
    )

    return (Join-Path $BuildArtifactsRoot "merged\$BuildType")
}

function Get-ProjectPath {
    param(
        [Parameter(Mandatory = $true)]
        $AppDefinition
    )

    [string]$projectPath = Join-Path $RepositoryRoot $AppDefinition.ProjectRelativePath
    if (-not (Test-Path -LiteralPath $projectPath)) {
        throw "Project file not found: $projectPath"
    }

    return $projectPath
}

function Get-CommonMSBuildProperties {
    param(
        [Parameter(Mandatory = $false)]
        [bool]$DisableSharedReferences = $false,

        [Parameter(Mandatory = $false)]
        [bool]$NativeAot = $false
    )

    [string[]]$properties = @(
        $PlatformProperty,
        $SkipKillProperty,
        $SkipPublishProperty,
        $DisableAggregateBuildProperty,
        $DisableRootOutputProperty
    )

    if ($DisableSharedReferences) {
        $properties += $DisableSharedReferencesProperty
    }

    if ($NativeAot) {
        $properties += @(
            $PublishAotProperty,
            $AllowIlcWarningsProperty
        )
    }

    return $properties
}

function New-DotNetArguments {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,

        [Parameter(Mandatory = $false)]
        [string]$Configuration = '',

        [Parameter(Mandatory = $false)]
        [string]$OutputDirectory = '',

        [Parameter(Mandatory = $false)]
        [bool]$NoRestore = $false,

        [Parameter(Mandatory = $false)]
        [bool]$UseRuntimeIdentifier = $false,

        [Parameter(Mandatory = $false)]
        [bool]$DisableSharedReferences = $false,

        [Parameter(Mandatory = $false)]
        [bool]$NativeAot = $false
    )

    [string[]]$arguments = @(
        $Command,
        $ProjectPath
    )

    if (-not [string]::IsNullOrWhiteSpace($Configuration)) {
        $arguments += @(
            '--configuration',
            $Configuration
        )
    }

    if ($NoRestore) {
        $arguments += $NoRestoreArgument
    }

    if ($UseRuntimeIdentifier) {
        $arguments += @(
            '--runtime',
            $RuntimeIdentifier
        )
    }

    if (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $arguments += @(
            '--output',
            $OutputDirectory
        )
    }

    $arguments += $NoLogoArgument
    $arguments += Get-CommonMSBuildProperties -DisableSharedReferences $DisableSharedReferences -NativeAot $NativeAot
    return $arguments
}

function ConvertTo-CmdArgument {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Argument
    )

    if ($Argument -notmatch '[\s&()<>|^"]') {
        return $Argument
    }

    return '"' + ($Argument -replace '"', '""') + '"'
}

function Invoke-DotNetCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $false)]
        $NativeAotToolchain
    )

    if (($null -ne $NativeAotToolchain) -and $NativeAotToolchain.Found -and $NativeAotToolchain.Kind -eq 'VsDevEnvironment') {
        [string]$commandPath = New-DotNetCommandScript -CommandName 'sync-dotnet' -ExecutablePath $ExecutablePath -Arguments $Arguments -NativeAotToolchain $NativeAotToolchain
        & cmd.exe /d /c "`"$commandPath`"" 2>&1 | ForEach-Object { Write-Host $_ }
    } else {
        & $ExecutablePath @Arguments 2>&1 | ForEach-Object { Write-Host $_ }
    }

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE."
    }
}

function New-DotNetCommandScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandName,

        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $false)]
        $NativeAotToolchain,

        [Parameter(Mandatory = $false)]
        [string]$ExitCodePath = ''
    )

    [string]$safeCommandName = $CommandName -replace '[^A-Za-z0-9_.-]', '_'
    [string]$commandPath = Join-Path $BuildLogRoot "$safeCommandName.cmd"
    [string]$argumentsLine = ($Arguments | ForEach-Object { ConvertTo-CmdArgument $_ }) -join ' '
    [string[]]$commandLines = @('@echo off')

    if (($null -ne $NativeAotToolchain) -and $NativeAotToolchain.Found -and $NativeAotToolchain.Kind -eq 'VsDevEnvironment') {
        $commandLines += "call ""$($NativeAotToolchain.Path)"" >nul"
        $commandLines += 'if errorlevel 1 goto exit_with_code'
    }

    $commandLines += """$ExecutablePath"" $argumentsLine"
    $commandLines += ':exit_with_code'
    $commandLines += 'set TRAYAPPDOTNET_EXIT_CODE=%errorlevel%'
    if (-not [string]::IsNullOrWhiteSpace($ExitCodePath)) {
        $commandLines += "(echo %TRAYAPPDOTNET_EXIT_CODE%)>""$ExitCodePath"""
    }

    $commandLines += 'exit /b %TRAYAPPDOTNET_EXIT_CODE%'
    Set-Content -LiteralPath $commandPath -Value $commandLines -Encoding ASCII

    return $commandPath
}

function Start-DotNetCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandName,

        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $false)]
        $NativeAotToolchain
    )

    [string]$safeCommandName = $CommandName -replace '[^A-Za-z0-9_.-]', '_'
    [string]$exitCodePath = Join-Path $BuildLogRoot "$safeCommandName.exitcode"
    [string]$commandPath = New-DotNetCommandScript -CommandName $CommandName -ExecutablePath $ExecutablePath -Arguments $Arguments -NativeAotToolchain $NativeAotToolchain -ExitCodePath $exitCodePath
    [string]$standardOutputPath = Join-Path $BuildLogRoot "$safeCommandName.out.log"
    [string]$standardErrorPath = Join-Path $BuildLogRoot "$safeCommandName.err.log"
    [string]$processArguments = "/d /c ""$commandPath"""
    $process = Start-Process -FilePath 'cmd.exe' `
        -ArgumentList $processArguments `
        -RedirectStandardOutput $standardOutputPath `
        -RedirectStandardError $standardErrorPath `
        -PassThru `
        -WindowStyle Hidden

    return [pscustomobject]@{
        CommandName = $CommandName
        Process = $process
        StandardOutputPath = $standardOutputPath
        StandardErrorPath = $standardErrorPath
        ExitCodePath = $exitCodePath
    }
}

function Wait-DotNetCommands {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$CommandJobs
    )

    foreach ($commandJob in $CommandJobs) {
        $commandJob.Process.WaitForExit()
    }

    [bool]$failed = $false
    foreach ($commandJob in $CommandJobs) {
        $commandJob.Process.Refresh()
        Write-Host ''
        Write-Host ("Build log: {0}" -f $commandJob.CommandName)

        if (Test-Path -LiteralPath $commandJob.StandardOutputPath) {
            Get-Content -LiteralPath $commandJob.StandardOutputPath | ForEach-Object { Write-Host $_ }
        }

        if (Test-Path -LiteralPath $commandJob.StandardErrorPath) {
            [string[]]$standardErrorLines = @(Get-Content -LiteralPath $commandJob.StandardErrorPath)
            if ($standardErrorLines.Count -gt 0) {
                foreach ($standardErrorLine in $standardErrorLines) {
                    Write-Host $standardErrorLine
                }
            }
        }

        [int]$exitCode = 1
        if (Test-Path -LiteralPath $commandJob.ExitCodePath) {
            [string]$exitCodeText = ''
            [string]$exitCodeContent = Get-Content -LiteralPath $commandJob.ExitCodePath -TotalCount 1
            if ($null -ne $exitCodeContent) {
                $exitCodeText = $exitCodeContent.Trim()
            }

            [int]$parsedExitCode = 1
            if ([int]::TryParse($exitCodeText, [ref]$parsedExitCode)) {
                $exitCode = $parsedExitCode
            }
        } elseif ($null -ne $commandJob.Process.ExitCode) {
            $exitCode = $commandJob.Process.ExitCode
        }

        if ($exitCode -ne 0) {
            $failed = $true
            Write-Host ("Command failed with exit code {0}: {1}" -f $exitCode, $commandJob.CommandName)
        }
    }

    if ($failed) {
        throw "One or more parallel app builds failed. Logs are in $BuildLogRoot"
    }
}

function Restore-App {
    param(
        [Parameter(Mandatory = $true)]
        $AppDefinition,

        [Parameter(Mandatory = $true)]
        [string]$BuildType,

        [Parameter(Mandatory = $true)]
        [string]$DotNetPath
    )

    [string]$projectPath = Get-ProjectPath -AppDefinition $AppDefinition
    [bool]$nativeAot = ($BuildType -eq $BuildTypeNativeAot)
    [string[]]$dotNetArguments = New-DotNetArguments `
        -Command 'restore' `
        -ProjectPath $projectPath `
        -UseRuntimeIdentifier $nativeAot `
        -NativeAot $nativeAot

    Write-Host ("Restoring {0}..." -f $AppDefinition.Name)
    Invoke-DotNetCommand -ExecutablePath $DotNetPath -Arguments $dotNetArguments
}

function Build-SharedProjects {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BuildType,

        [Parameter(Mandatory = $true)]
        [string]$DotNetPath
    )

    [string]$buildConfiguration = Get-BuildConfiguration -BuildType $BuildType
    [string]$generatorProjectPath = Join-Path $RepositoryRoot $XmlSourceGeneratorProjectRelativePath
    [string]$commonProjectPath = Join-Path $RepositoryRoot $CommonProjectRelativePath

    Write-Host ''
    Write-Host ("Building shared projects once ({0})..." -f $buildConfiguration)

    [string[]]$generatorArguments = New-DotNetArguments `
        -Command 'build' `
        -ProjectPath $generatorProjectPath `
        -Configuration $buildConfiguration
    Invoke-DotNetCommand -ExecutablePath $DotNetPath -Arguments $generatorArguments

    [string[]]$commonArguments = New-DotNetArguments `
        -Command 'build' `
        -ProjectPath $commonProjectPath `
        -Configuration $buildConfiguration `
        -DisableSharedReferences $true
    Invoke-DotNetCommand -ExecutablePath $DotNetPath -Arguments $commonArguments
}

function Get-AppBuildCommand {
    param(
        [Parameter(Mandatory = $true)]
        $AppDefinition,

        [Parameter(Mandatory = $true)]
        [string]$BuildType,

        [Parameter(Mandatory = $true)]
        [string]$DotNetPath
    )

    [string]$projectPath = Get-ProjectPath -AppDefinition $AppDefinition
    [string]$outputDirectory = Get-AppOutputDirectory -AppDefinition $AppDefinition -BuildType $BuildType
    [string[]]$dotNetArguments = @()

    switch ($BuildType) {
        $BuildTypeDebug {
            $dotNetArguments = New-DotNetArguments `
                -Command 'build' `
                -ProjectPath $projectPath `
                -Configuration $ConfigurationDebug `
                -NoRestore $true `
                -OutputDirectory $outputDirectory `
                -DisableSharedReferences $true
        }
        $BuildTypeNativeAot {
            $dotNetArguments = New-DotNetArguments `
                -Command 'publish' `
                -ProjectPath $projectPath `
                -Configuration $ConfigurationRelease `
                -NoRestore $true `
                -UseRuntimeIdentifier $true `
                -OutputDirectory $outputDirectory `
                -DisableSharedReferences $true `
                -NativeAot $true
        }
        default {
            throw "Unknown build type: $BuildType"
        }
    }

    return [pscustomobject]@{
        Name = $AppDefinition.Name
        DotNetPath = $DotNetPath
        Arguments = $dotNetArguments
        OutputDirectory = $outputDirectory
        ExecutablePath = Join-Path $outputDirectory "$($AppDefinition.Name).exe"
    }
}

function Build-AppsInParallel {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$AppDefinitions,

        [Parameter(Mandatory = $true)]
        [string]$BuildType,

        [Parameter(Mandatory = $true)]
        [string]$DotNetPath,

        [Parameter(Mandatory = $false)]
        $NativeAotToolchain
    )

    Write-Host ''
    Write-Host ("Building {0} app(s) in parallel with isolated app output directories..." -f $AppDefinitions.Count)

    $buildCommands = @()
    foreach ($appDefinition in $AppDefinitions) {
        $buildCommands += (Get-AppBuildCommand -AppDefinition $appDefinition -BuildType $BuildType -DotNetPath $DotNetPath)
    }

    $commandJobs = @()
    foreach ($buildCommand in $buildCommands) {
        $toolchainForBuild = $null
        if ($BuildType -eq $BuildTypeNativeAot) {
            $toolchainForBuild = $NativeAotToolchain
        }

        $commandJobs += (Start-DotNetCommand -CommandName "build-$($buildCommand.Name)" -ExecutablePath $buildCommand.DotNetPath -Arguments $buildCommand.Arguments -NativeAotToolchain $toolchainForBuild)
    }

    Wait-DotNetCommands -CommandJobs $commandJobs

    foreach ($buildCommand in $buildCommands) {
        if (-not (Test-Path -LiteralPath $buildCommand.ExecutablePath)) {
            throw "Build completed, but expected executable was not found: $($buildCommand.ExecutablePath)"
        }
    }

    return $buildCommands
}

function Merge-AppOutputs {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$BuildResults,

        [Parameter(Mandatory = $true)]
        [string]$BuildType
    )

    [string]$mergedOutputDirectory = Get-MergedOutputDirectory -BuildType $BuildType
    if (-not (Test-Path -LiteralPath $mergedOutputDirectory)) {
        [void](New-Item -ItemType Directory -Path $mergedOutputDirectory)
    }

    foreach ($buildResult in $BuildResults) {
        [string]$sourceGlob = Join-Path $buildResult.OutputDirectory '*'
        Copy-Item -Path $sourceGlob -Destination $mergedOutputDirectory -Recurse -Force
    }

    return $mergedOutputDirectory
}

try {
    Set-Location -LiteralPath $RepositoryRoot
    Initialize-BuildDirectories
    Set-BuildEnvironment

    $dotNetSdkInfo = Resolve-DotNetSdk
    $nativeAotToolchain = Get-NativeAotToolchain
    Write-PrerequisiteReport -DotNetSdkInfo $dotNetSdkInfo -NativeAotToolchain $nativeAotToolchain

    if (-not $dotNetSdkInfo.Found) {
        if (-not (Read-Confirmation ".NET $RequiredDotNetMajorVersion SDK is missing. Download and install it to .buildtools now?")) {
            exit 1
        }

        $dotNetSdkInfo = Install-DotNetSdk
    }

    Set-BuildEnvironment -DotNetPath $dotNetSdkInfo.Path

    $selectedApps = @(Read-AppSelection)
    [string]$buildType = Read-BuildType

    if ($buildType -eq $BuildTypeNativeAot) {
        $nativeAotToolchain = Get-NativeAotToolchain
        if (-not $nativeAotToolchain.Found) {
            Write-Host ''
            Write-Host 'Native AOT on Windows requires Microsoft C++ Build Tools and the Windows SDK.'
            if (-not (Read-Confirmation 'Download the Build Tools bootstrapper and install the required C++ components now?')) {
                exit 1
            }

            $nativeAotToolchain = Install-NativeAotToolchain
        }
    }

    foreach ($selectedApp in $selectedApps) {
        Restore-App -AppDefinition $selectedApp -BuildType $buildType -DotNetPath $dotNetSdkInfo.Path
    }

    Build-SharedProjects -BuildType $buildType -DotNetPath $dotNetSdkInfo.Path
    $buildResults = @(Build-AppsInParallel -AppDefinitions $selectedApps -BuildType $buildType -DotNetPath $dotNetSdkInfo.Path -NativeAotToolchain $nativeAotToolchain)
    [string]$mergedOutputDirectory = Merge-AppOutputs -BuildResults $buildResults -BuildType $buildType

    Write-Host ''
    Write-Host 'Build outputs:'
    foreach ($buildResult in $buildResults) {
        Write-Host ("- {0}: {1}" -f $buildResult.Name, $buildResult.OutputDirectory)
        Write-Host ("  EXE: {0}" -f $buildResult.ExecutablePath)
    }

    Write-Host ("- Merged output: {0}" -f $mergedOutputDirectory)
    Write-Host ("- Build logs: {0}" -f $BuildLogRoot)
} catch {
    Write-Error $_.Exception.Message
    exit 1
}
