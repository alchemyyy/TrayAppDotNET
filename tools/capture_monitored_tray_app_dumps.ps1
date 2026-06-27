#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$ProcDumpPath,
    [string]$OutputRoot,
    [string[]]$AppExeNames = @(
        'BatteryTrayAppDotNET.exe',
        'BrightnessTrayAppDotNET.exe',
        'FanControlTrayAppDotNET.exe',
        'NetworkTrayAppDotNET.exe',
        'TaskManagerTrayAppDotNET.exe',
        'VolumeTrayAppDotNET.exe'
    ),
    [string]$RequiredArgument = '--monitored',
    [bool]$ClonePass = $true,
    [bool]$DirectPass = $true,
    [switch]$KernelStacksOnDirectPass,
    [switch]$SkipBinaryCopy,
    [switch]$ListOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-SafePathSegment {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    [string]$safeValue = $Value -replace '[<>:"/\\|?*]', '_'
    return $safeValue.Trim()
}

function Test-CommandLineContainsArgument {
    param(
        [string]$CommandLine,
        [Parameter(Mandatory = $true)]
        [string]$Argument
    )

    if ([string]::IsNullOrWhiteSpace($CommandLine)) {
        return $false
    }

    return $CommandLine.IndexOf($Argument, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
}

function Resolve-ProcDumpPath {
    param(
        [string]$RequestedProcDumpPath,
        [bool]$RequireTool
    )

    if (-not $RequireTool) {
        return $null
    }

    if (-not [string]::IsNullOrWhiteSpace($RequestedProcDumpPath)) {
        if (-not (Test-Path -LiteralPath $RequestedProcDumpPath -PathType Leaf)) {
            throw "ProcDumpPath does not exist: $RequestedProcDumpPath"
        }

        return (Resolve-Path -LiteralPath $RequestedProcDumpPath).Path
    }

    [string]$scriptDirectoryProcDumpPath = Join-Path -Path $PSScriptRoot -ChildPath 'procdump64.exe'
    if (Test-Path -LiteralPath $scriptDirectoryProcDumpPath -PathType Leaf) {
        return (Resolve-Path -LiteralPath $scriptDirectoryProcDumpPath).Path
    }

    [string]$currentDirectoryProcDumpPath = Join-Path -Path (Get-Location).Path -ChildPath 'procdump64.exe'
    if (Test-Path -LiteralPath $currentDirectoryProcDumpPath -PathType Leaf) {
        return (Resolve-Path -LiteralPath $currentDirectoryProcDumpPath).Path
    }

    [System.Management.Automation.CommandInfo]$procDump64Command = Get-Command -Name 'procdump64.exe' -ErrorAction SilentlyContinue
    if ($null -ne $procDump64Command) {
        return $procDump64Command.Source
    }

    throw 'procdump64.exe was not found beside this script, in the current directory, or on PATH. Run .tadn_tools\download_procdump.ps1 or pass -ProcDumpPath with the full path to procdump64.exe.'
}

function New-TrayProcessRecord {
    param(
        [Parameter(Mandatory = $true)]
        [object]$CimProcess
    )

    [int]$processId = [int]$CimProcess.ProcessId
    [string]$processName = [string]$CimProcess.Name
    [string]$executablePath = [string]$CimProcess.ExecutablePath
    [string]$commandLine = [string]$CimProcess.CommandLine
    [string]$sourceDirectory = $null

    if (-not [string]::IsNullOrWhiteSpace($executablePath)) {
        $sourceDirectory = [System.IO.Path]::GetDirectoryName($executablePath)
    }

    [object]$creationTime = $null
    [object]$creationDateValue = $CimProcess.CreationDate
    if ($creationDateValue -is [datetime]) {
        $creationTime = [datetime]$creationDateValue
    }
    elseif (-not [string]::IsNullOrWhiteSpace([string]$creationDateValue)) {
        try {
            $creationTime = [System.Management.ManagementDateTimeConverter]::ToDateTime([string]$creationDateValue)
        }
        catch {
            $creationTime = [string]$creationDateValue
        }
    }

    [Int64]$privateBytes = 0
    [Int64]$workingSetBytes = 0
    [int]$handleCount = 0
    [bool]$isRunning = $true

    try {
        [System.Diagnostics.Process]$diagnosticProcess = Get-Process -Id $processId -ErrorAction Stop
        $privateBytes = [Int64]$diagnosticProcess.PrivateMemorySize64
        $workingSetBytes = [Int64]$diagnosticProcess.WorkingSet64
        $handleCount = [int]$diagnosticProcess.HandleCount
    }
    catch {
        $isRunning = $false
    }

    return [pscustomobject]@{
        Name = $processName
        ProcessId = $processId
        AppBaseName = [System.IO.Path]::GetFileNameWithoutExtension($processName)
        ExecutablePath = $executablePath
        SourceDirectory = $sourceDirectory
        CommandLine = $commandLine
        CreationTimeLocal = $creationTime
        PrivateBytes = $privateBytes
        WorkingSetBytes = $workingSetBytes
        HandleCount = $handleCount
        IsRunning = $isRunning
    }
}

function Get-MonitoredTrayProcesses {
    param(
        [string[]]$ExecutableNames,
        [string]$Argument,
        [System.Collections.Generic.List[object]]$SkippedProcesses
    )

    [System.Collections.Generic.List[object]]$matchingProcesses = New-Object 'System.Collections.Generic.List[object]'

    foreach ($executableName in $ExecutableNames) {
        [string]$normalizedExecutableName = $executableName
        if (-not $normalizedExecutableName.EndsWith('.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
            $normalizedExecutableName = "$normalizedExecutableName.exe"
        }

        [string]$escapedExecutableName = $normalizedExecutableName.Replace("'", "''")
        [string]$wmiFilter = "Name = '$escapedExecutableName'"
        [Microsoft.Management.Infrastructure.CimInstance[]]$candidateProcesses = @(Get-CimInstance -ClassName Win32_Process -Filter $wmiFilter)

        foreach ($candidateProcess in $candidateProcesses) {
            [string]$candidateCommandLine = [string]$candidateProcess.CommandLine
            if (Test-CommandLineContainsArgument -CommandLine $candidateCommandLine -Argument $Argument) {
                $matchingProcesses.Add((New-TrayProcessRecord $candidateProcess))
                continue
            }

            $SkippedProcesses.Add([pscustomobject]@{
                Name = [string]$candidateProcess.Name
                ProcessId = [int]$candidateProcess.ProcessId
                Reason = "Command line does not contain $Argument"
                CommandLine = $candidateCommandLine
            })
        }
    }

    return @($matchingProcesses | Sort-Object -Property PrivateBytes -Descending)
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [object]$InputObject,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    ConvertTo-Json -InputObject $InputObject -Depth 8 | Out-File -LiteralPath $Path -Encoding UTF8
}

function Save-ProcessModules {
    param(
        [Parameter(Mandatory = $true)]
        [int]$ProcessId,
        [Parameter(Mandatory = $true)]
        [string]$OutputPath
    )

    [System.Collections.Generic.List[object]]$moduleRecords = New-Object 'System.Collections.Generic.List[object]'

    try {
        [System.Diagnostics.Process]$diagnosticProcess = Get-Process -Id $ProcessId -ErrorAction Stop
        foreach ($processModule in $diagnosticProcess.Modules) {
            $moduleRecords.Add([pscustomobject]@{
                ModuleName = [string]$processModule.ModuleName
                FileName = [string]$processModule.FileName
                BaseAddress = ('0x{0:X16}' -f $processModule.BaseAddress.ToInt64())
                ModuleMemorySize = [int]$processModule.ModuleMemorySize
                FileVersion = [string]$processModule.FileVersionInfo.FileVersion
                ProductVersion = [string]$processModule.FileVersionInfo.ProductVersion
            })
        }
    }
    catch {
        $moduleRecords.Add([pscustomobject]@{
            ModuleName = '<failed>'
            FileName = $_.Exception.Message
            BaseAddress = $null
            ModuleMemorySize = 0
            FileVersion = $null
            ProductVersion = $null
        })
    }

    $moduleRecords | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8
}

function Invoke-BinaryDirectoryCopy {
    param(
        [Parameter(Mandatory = $true)]
        [object]$ProcessRecord,
        [Parameter(Mandatory = $true)]
        [string]$DestinationRoot
    )

    if ([string]::IsNullOrWhiteSpace([string]$ProcessRecord.SourceDirectory)) {
        return [pscustomobject]@{
            ProcessId = [int]$ProcessRecord.ProcessId
            Name = [string]$ProcessRecord.Name
            SourceDirectory = $null
            DestinationDirectory = $null
            ExitCode = $null
            Status = 'Skipped'
            Message = 'ExecutablePath was unavailable'
        }
    }

    [string]$safeProcessName = ConvertTo-SafePathSegment -Value ([string]$ProcessRecord.AppBaseName)
    [string]$destinationDirectory = Join-Path -Path $DestinationRoot -ChildPath ('{0}_{1}' -f $safeProcessName, [int]$ProcessRecord.ProcessId)
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

    [string]$robocopyLogPath = Join-Path -Path $destinationDirectory -ChildPath 'robocopy.log'
    [string[]]$robocopyArguments = @(
        [string]$ProcessRecord.SourceDirectory,
        $destinationDirectory,
        '/E',
        '/COPY:DAT',
        '/DCOPY:DAT',
        '/R:2',
        '/W:1',
        '/NFL',
        '/NDL',
        '/NP'
    )

    [string[]]$robocopyOutput = @(robocopy @robocopyArguments)
    [int]$robocopyExitCode = [int]$LASTEXITCODE
    $robocopyOutput | Out-File -LiteralPath $robocopyLogPath -Encoding UTF8

    [string]$copyStatus = 'Succeeded'
    if ($robocopyExitCode -gt 7) {
        $copyStatus = 'Failed'
    }

    return [pscustomobject]@{
        ProcessId = [int]$ProcessRecord.ProcessId
        Name = [string]$ProcessRecord.Name
        SourceDirectory = [string]$ProcessRecord.SourceDirectory
        DestinationDirectory = $destinationDirectory
        ExitCode = $robocopyExitCode
        Status = $copyStatus
        Message = "robocopy exit code $robocopyExitCode"
    }
}

function Invoke-ProcDumpCapture {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResolvedProcDumpPath,
        [Parameter(Mandatory = $true)]
        [object]$ProcessRecord,
        [Parameter(Mandatory = $true)]
        [string]$PassName,
        [Parameter(Mandatory = $true)]
        [string]$ProcessOutputDirectory,
        [bool]$UseClone,
        [bool]$UseKernelStacks
    )

    [int]$processId = [int]$ProcessRecord.ProcessId
    [string]$safeProcessName = ConvertTo-SafePathSegment -Value ([string]$ProcessRecord.AppBaseName)
    [string]$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    [string]$dumpFileName = '{0}_{1}_{2}_{3}.dmp' -f $safeProcessName, $processId, $PassName, $timestamp
    [string]$dumpPath = Join-Path -Path $ProcessOutputDirectory -ChildPath $dumpFileName
    [string]$logPath = Join-Path -Path $ProcessOutputDirectory -ChildPath ($dumpFileName + '.procdump.log')

    [System.Collections.Generic.List[string]]$procDumpArguments = New-Object 'System.Collections.Generic.List[string]'
    $procDumpArguments.Add('-accepteula')
    $procDumpArguments.Add('-ma')

    if ($UseClone) {
        $procDumpArguments.Add('-r')
        $procDumpArguments.Add('1')
    }

    if ($UseKernelStacks) {
        $procDumpArguments.Add('-mk')
    }

    $procDumpArguments.Add([string]$processId)
    $procDumpArguments.Add($dumpPath)

    [datetime]$startedAt = Get-Date
    Write-Host ("[{0}] Dumping PID {1} ({2}) to {3}" -f $PassName, $processId, [string]$ProcessRecord.Name, $dumpPath)
    [string[]]$procDumpOutput = @(& $ResolvedProcDumpPath @procDumpArguments 2>&1)
    [int]$exitCode = [int]$LASTEXITCODE
    [datetime]$endedAt = Get-Date
    $procDumpOutput | Out-File -LiteralPath $logPath -Encoding UTF8

    [System.IO.FileInfo]$dumpFileInfo = Get-Item -LiteralPath $dumpPath -ErrorAction SilentlyContinue
    [bool]$dumpExists = $null -ne $dumpFileInfo
    [Int64]$dumpLength = 0
    if ($dumpExists) {
        $dumpLength = [Int64]$dumpFileInfo.Length
    }

    [string]$status = 'Succeeded'
    [string]$message = 'Dump written'
    if ($exitCode -ne 0) {
        $status = 'Failed'
        $message = "ProcDump exited with $exitCode"
    }
    elseif (-not $dumpExists) {
        $status = 'Failed'
        $message = 'Expected dump file was not created'
    }
    elseif ($dumpLength -le 0) {
        $status = 'Failed'
        $message = 'Expected dump file is empty'
    }

    return [pscustomobject]@{
        ProcessId = $processId
        Name = [string]$ProcessRecord.Name
        PassName = $PassName
        UseClone = $UseClone
        UseKernelStacks = $UseKernelStacks
        DumpPath = $dumpPath
        LogPath = $logPath
        ExitCode = $exitCode
        Status = $status
        Message = $message
        StartedAtLocal = $startedAt
        EndedAtLocal = $endedAt
        DurationSeconds = [math]::Round(($endedAt - $startedAt).TotalSeconds, 3)
        DumpBytes = $dumpLength
    }
}

[string]$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
[string]$repoRoot = Resolve-Path -LiteralPath (Join-Path -Path $scriptRoot -ChildPath '..')

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    [string]$outputStamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $OutputRoot = Join-Path -Path $repoRoot -ChildPath (Join-Path -Path 'dumps' -ChildPath "monitored_tray_procdump_$outputStamp")
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
[string]$processesRoot = Join-Path -Path $OutputRoot -ChildPath 'processes'
[string]$binariesRoot = Join-Path -Path $OutputRoot -ChildPath 'binaries'
[string]$msSymbolCacheRoot = Join-Path -Path $OutputRoot -ChildPath 'ms-symbol-cache'
New-Item -ItemType Directory -Path $processesRoot -Force | Out-Null
New-Item -ItemType Directory -Path $msSymbolCacheRoot -Force | Out-Null

[System.Collections.Generic.List[object]]$skippedProcesses = New-Object 'System.Collections.Generic.List[object]'
[object[]]$targetProcesses = @(Get-MonitoredTrayProcesses -ExecutableNames $AppExeNames -Argument $RequiredArgument -SkippedProcesses $skippedProcesses)

[string]$inventoryJsonPath = Join-Path -Path $OutputRoot -ChildPath 'target_processes.json'
[string]$inventoryCsvPath = Join-Path -Path $OutputRoot -ChildPath 'target_processes.csv'
[string]$skippedJsonPath = Join-Path -Path $OutputRoot -ChildPath 'skipped_processes.json'
[string]$skippedCsvPath = Join-Path -Path $OutputRoot -ChildPath 'skipped_processes.csv'

Write-JsonFile -InputObject $targetProcesses -Path $inventoryJsonPath
$targetProcesses | Export-Csv -LiteralPath $inventoryCsvPath -NoTypeInformation -Encoding UTF8
Write-JsonFile -InputObject @($skippedProcesses) -Path $skippedJsonPath
@($skippedProcesses) | Export-Csv -LiteralPath $skippedCsvPath -NoTypeInformation -Encoding UTF8

Write-Host "Output root: $OutputRoot"
Write-Host ("Matched monitored processes: {0}" -f $targetProcesses.Count)
Write-Host ("Skipped same-name non-monitored processes: {0}" -f $skippedProcesses.Count)

foreach ($targetProcess in $targetProcesses) {
    Write-Host ("  PID {0} {1} PrivateBytes={2:N0} WorkingSet={3:N0}" -f [int]$targetProcess.ProcessId, [string]$targetProcess.Name, [Int64]$targetProcess.PrivateBytes, [Int64]$targetProcess.WorkingSetBytes)
}

if ($targetProcesses.Count -eq 0) {
    throw "No target processes matched the configured exe names and required argument '$RequiredArgument'."
}

if ($ListOnly) {
    Write-Host 'ListOnly was specified. No dumps were captured.'
    exit 0
}

if (-not $ClonePass -and -not $DirectPass) {
    throw 'At least one dump pass must be enabled. Set -ClonePass or -DirectPass to true.'
}

[string]$resolvedProcDumpPath = Resolve-ProcDumpPath -RequestedProcDumpPath $ProcDumpPath -RequireTool $true
Write-Host "Using ProcDump: $resolvedProcDumpPath"

[System.Collections.Generic.List[object]]$captureRecords = New-Object 'System.Collections.Generic.List[object]'
[System.Collections.Generic.List[object]]$binaryCopyRecords = New-Object 'System.Collections.Generic.List[object]'

foreach ($targetProcess in $targetProcesses) {
    [string]$safeProcessName = ConvertTo-SafePathSegment -Value ([string]$targetProcess.AppBaseName)
    [string]$processOutputDirectory = Join-Path -Path $processesRoot -ChildPath ('{0}_{1}' -f $safeProcessName, [int]$targetProcess.ProcessId)
    New-Item -ItemType Directory -Path $processOutputDirectory -Force | Out-Null

    [string]$processMetadataPath = Join-Path -Path $processOutputDirectory -ChildPath 'process_metadata.json'
    [string]$moduleListPath = Join-Path -Path $processOutputDirectory -ChildPath 'modules.csv'
    Write-JsonFile -InputObject $targetProcess -Path $processMetadataPath
    Save-ProcessModules -ProcessId ([int]$targetProcess.ProcessId) -OutputPath $moduleListPath
}

# Preserve one clone-backed full dump for every process before taking slower direct dumps.
if ($ClonePass) {
    foreach ($targetProcess in $targetProcesses) {
        [string]$safeProcessName = ConvertTo-SafePathSegment -Value ([string]$targetProcess.AppBaseName)
        [string]$processOutputDirectory = Join-Path -Path $processesRoot -ChildPath ('{0}_{1}' -f $safeProcessName, [int]$targetProcess.ProcessId)

        try {
            $captureRecords.Add((Invoke-ProcDumpCapture -ResolvedProcDumpPath $resolvedProcDumpPath -ProcessRecord $targetProcess -PassName 'A_full_clone' -ProcessOutputDirectory $processOutputDirectory -UseClone $true -UseKernelStacks $false))
        }
        catch {
            $captureRecords.Add([pscustomobject]@{
                ProcessId = [int]$targetProcess.ProcessId
                Name = [string]$targetProcess.Name
                PassName = 'A_full_clone'
                UseClone = $true
                UseKernelStacks = $false
                DumpPath = $null
                LogPath = $null
                ExitCode = $null
                Status = 'Failed'
                Message = $_.Exception.Message
                StartedAtLocal = $null
                EndedAtLocal = Get-Date
                DurationSeconds = 0
                DumpBytes = 0
            })
            Write-Warning ("Clone dump failed for PID {0}: {1}" -f [int]$targetProcess.ProcessId, $_.Exception.Message)
        }
    }
}

if ($DirectPass) {
    [string]$directPassName = 'B_full_direct'
    if ($KernelStacksOnDirectPass) {
        $directPassName = 'B_full_direct_kernel'
    }

    foreach ($targetProcess in $targetProcesses) {
        [string]$safeProcessName = ConvertTo-SafePathSegment -Value ([string]$targetProcess.AppBaseName)
        [string]$processOutputDirectory = Join-Path -Path $processesRoot -ChildPath ('{0}_{1}' -f $safeProcessName, [int]$targetProcess.ProcessId)

        try {
            $captureRecords.Add((Invoke-ProcDumpCapture -ResolvedProcDumpPath $resolvedProcDumpPath -ProcessRecord $targetProcess -PassName $directPassName -ProcessOutputDirectory $processOutputDirectory -UseClone $false -UseKernelStacks ([bool]$KernelStacksOnDirectPass)))
        }
        catch {
            $captureRecords.Add([pscustomobject]@{
                ProcessId = [int]$targetProcess.ProcessId
                Name = [string]$targetProcess.Name
                PassName = $directPassName
                UseClone = $false
                UseKernelStacks = [bool]$KernelStacksOnDirectPass
                DumpPath = $null
                LogPath = $null
                ExitCode = $null
                Status = 'Failed'
                Message = $_.Exception.Message
                StartedAtLocal = $null
                EndedAtLocal = Get-Date
                DurationSeconds = 0
                DumpBytes = 0
            })
            Write-Warning ("Direct dump failed for PID {0}: {1}" -f [int]$targetProcess.ProcessId, $_.Exception.Message)
        }
    }
}

if (-not $SkipBinaryCopy) {
    New-Item -ItemType Directory -Path $binariesRoot -Force | Out-Null

    foreach ($targetProcess in $targetProcesses) {
        try {
            $binaryCopyRecords.Add((Invoke-BinaryDirectoryCopy -ProcessRecord $targetProcess -DestinationRoot $binariesRoot))
        }
        catch {
            $binaryCopyRecords.Add([pscustomobject]@{
                ProcessId = [int]$targetProcess.ProcessId
                Name = [string]$targetProcess.Name
                SourceDirectory = [string]$targetProcess.SourceDirectory
                DestinationDirectory = $null
                ExitCode = $null
                Status = 'Failed'
                Message = $_.Exception.Message
            })
            Write-Warning ("Binary copy failed for PID {0}: {1}" -f [int]$targetProcess.ProcessId, $_.Exception.Message)
        }
    }
}

[string]$capturesJsonPath = Join-Path -Path $OutputRoot -ChildPath 'captures.json'
[string]$capturesCsvPath = Join-Path -Path $OutputRoot -ChildPath 'captures.csv'
[string]$binaryCopiesJsonPath = Join-Path -Path $OutputRoot -ChildPath 'binary_copies.json'
[string]$binaryCopiesCsvPath = Join-Path -Path $OutputRoot -ChildPath 'binary_copies.csv'
[string]$symbolPathTextPath = Join-Path -Path $OutputRoot -ChildPath 'symbol_path.txt'
[string]$windbgCommandsPath = Join-Path -Path $OutputRoot -ChildPath 'windbg_symbol_commands.txt'

Write-JsonFile -InputObject @($captureRecords) -Path $capturesJsonPath
@($captureRecords) | Export-Csv -LiteralPath $capturesCsvPath -NoTypeInformation -Encoding UTF8
Write-JsonFile -InputObject @($binaryCopyRecords) -Path $binaryCopiesJsonPath
@($binaryCopyRecords) | Export-Csv -LiteralPath $binaryCopiesCsvPath -NoTypeInformation -Encoding UTF8

[string]$symbolPath = '{0};srv*{1}*https://msdl.microsoft.com/download/symbols' -f $binariesRoot, $msSymbolCacheRoot
$symbolPath | Out-File -LiteralPath $symbolPathTextPath -Encoding UTF8
@(
    ".sympath $symbolPath",
    '.reload'
) | Out-File -LiteralPath $windbgCommandsPath -Encoding UTF8

[object[]]$failedCaptures = @($captureRecords | Where-Object { [string]$_.Status -ne 'Succeeded' })
[object[]]$succeededCaptures = @($captureRecords | Where-Object { [string]$_.Status -eq 'Succeeded' })

Write-Host ("Successful captures: {0}" -f $succeededCaptures.Count)
Write-Host ("Failed captures: {0}" -f $failedCaptures.Count)
Write-Host "Symbol path: $symbolPath"
Write-Host "Capture manifest: $capturesJsonPath"

if ($failedCaptures.Count -gt 0) {
    exit 2
}

exit 0
