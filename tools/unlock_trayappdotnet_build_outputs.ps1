param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectories,

    [Parameter(Mandatory = $true)]
    [string]$AllowedProcessNames,

    [Parameter(Mandatory = $false)]
    [int]$MaxAttempts = 4,

    [Parameter(Mandatory = $false)]
    [int]$RetryDelayMilliseconds = 500
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class RestartManagerNative
{
    public const int ErrorMoreData = 234;
    public const int MaxAppName = 255;
    public const int MaxServiceName = 63;

    public enum RM_APP_TYPE
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxAppName + 1)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxServiceName + 1)]
        public string strServiceShortName;

        public RM_APP_TYPE ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    public static extern int RmStartSession(
        out uint pSessionHandle,
        int dwSessionFlags,
        string strSessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    public static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[] rgsFilenames,
        uint nApplications,
        RM_UNIQUE_PROCESS[] rgApplications,
        uint nServices,
        string[] rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    public static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[] rgAffectedApps,
        ref uint lpdwRebootReasons);

    [DllImport("rstrtmgr.dll")]
    public static extern int RmEndSession(uint pSessionHandle);
}
'@

function Split-List {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    [string[]]$parts = $Value -split '[;|,]'
    [string[]]$items = @()
    foreach ($part in $parts) {
        [string]$item = $part.Trim()
        if ([string]::IsNullOrWhiteSpace($item)) {
            continue
        }

        $items += $item
    }

    return $items
}

function Normalize-ProcessName {
    param(
        [Parameter(Mandatory = $false)]
        [AllowNull()]
        [string]$ProcessName
    )

    if ([string]::IsNullOrWhiteSpace($ProcessName)) {
        return ''
    }

    [string]$normalized = $ProcessName.Trim()
    if ($normalized.EndsWith('.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
        $normalized = $normalized.Substring(0, $normalized.Length - 4)
    }

    return $normalized
}

function New-AllowedProcessNameSet {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Names
    )

    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    [string[]]$splitNames = Split-List -Value $Names
    foreach ($name in $splitNames) {
        [string]$normalizedName = Normalize-ProcessName -ProcessName $name
        if ([string]::IsNullOrWhiteSpace($normalizedName)) {
            continue
        }

        [void]$set.Add($normalizedName)
    }

    return $set
}

function Get-OutputDirectoryPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DirectoryList
    )

    [string[]]$directories = Split-List -Value $DirectoryList
    [string[]]$resolvedDirectories = @()
    foreach ($directory in $directories) {
        if ($directory -eq '.') {
            continue
        }

        try {
            [string]$resolvedDirectory = [System.IO.Path]::GetFullPath($directory)
        } catch {
            Write-Host "Skipping invalid output directory '$directory': $($_.Exception.Message)"
            continue
        }

        if ($resolvedDirectories -contains $resolvedDirectory) {
            continue
        }

        $resolvedDirectories += $resolvedDirectory
    }

    return $resolvedDirectories
}

function Get-OutputFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Directories
    )

    $files = @()
    foreach ($directory in $Directories) {
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            continue
        }

        try {
            $files += @(Get-ChildItem -LiteralPath $directory -File -Recurse -Force -ErrorAction Stop)
        } catch {
            Write-Host "Could not enumerate output directory '$directory': $($_.Exception.Message)"
        }
    }

    return $files
}

function Test-FileCanBeOverwritten {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )

    [System.IO.FileStream]$stream = $null
    try {
        $stream = [System.IO.File]::Open(
            $FilePath,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
        return $true
    } catch [System.IO.IOException] {
        return $false
    } catch [System.UnauthorizedAccessException] {
        return $false
    } finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

function Get-LockingProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )

    [uint32]$sessionHandle = 0
    [string]$sessionKey = [System.Guid]::NewGuid().ToString('N')
    [int]$result = [RestartManagerNative]::RmStartSession([ref]$sessionHandle, 0, $sessionKey)
    if ($result -ne 0) {
        Write-Host "Restart Manager could not start a session for '$FilePath' (error $result)."
        return @()
    }

    try {
        [string[]]$resources = @($FilePath)
        $emptyApplications = [System.Array]::CreateInstance([RestartManagerNative+RM_UNIQUE_PROCESS], 0)
        [string[]]$emptyServices = @()

        $result = [RestartManagerNative]::RmRegisterResources(
            $sessionHandle,
            [uint32]$resources.Length,
            $resources,
            0,
            $emptyApplications,
            0,
            $emptyServices)
        if ($result -ne 0) {
            Write-Host "Restart Manager could not register '$FilePath' (error $result)."
            return @()
        }

        [uint32]$needed = 0
        [uint32]$count = 0
        [uint32]$rebootReasons = 0
        $emptyProcessInfos = [System.Array]::CreateInstance([RestartManagerNative+RM_PROCESS_INFO], 0)
        $result = [RestartManagerNative]::RmGetList(
            $sessionHandle,
            [ref]$needed,
            [ref]$count,
            $emptyProcessInfos,
            [ref]$rebootReasons)

        if ($result -eq [RestartManagerNative]::ErrorMoreData -and $needed -gt 0) {
            $count = $needed
            $processInfos = [System.Array]::CreateInstance([RestartManagerNative+RM_PROCESS_INFO], [int]$count)
            $result = [RestartManagerNative]::RmGetList(
                $sessionHandle,
                [ref]$needed,
                [ref]$count,
                $processInfos,
                [ref]$rebootReasons)
        } else {
            $processInfos = $emptyProcessInfos
        }

        if ($result -ne 0) {
            Write-Host "Restart Manager could not list lockers for '$FilePath' (error $result)."
            return @()
        }

        $lockingProcesses = @()
        for ([int]$processIndex = 0; $processIndex -lt [int]$count; $processIndex++) {
            $processInfo = $processInfos.GetValue($processIndex)
            $lockingProcesses += [pscustomobject]@{
                ProcessId = [int]$processInfo.Process.dwProcessId
                AppName = [string]$processInfo.strAppName
            }
        }

        return $lockingProcesses
    } finally {
        [void][RestartManagerNative]::RmEndSession($sessionHandle)
    }
}

function Test-IsAllowedLocker {
    param(
        [Parameter(Mandatory = $true)]
        $AllowedNames,

        [Parameter(Mandatory = $true)]
        [int]$ProcessId,

        [Parameter(Mandatory = $false)]
        [AllowNull()]
        [string]$ReportedName
    )

    [string]$normalizedReportedName = Normalize-ProcessName -ProcessName $ReportedName
    if (-not [string]::IsNullOrWhiteSpace($normalizedReportedName) -and $AllowedNames.Contains($normalizedReportedName)) {
        return $true
    }

    try {
        $process = Get-Process -Id $ProcessId -ErrorAction Stop
        [string]$normalizedActualName = Normalize-ProcessName -ProcessName $process.ProcessName
        return $AllowedNames.Contains($normalizedActualName)
    } catch {
        return $false
    }
}

function Get-TrayAppDotNETLockedFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Directories,

        [Parameter(Mandatory = $true)]
        $AllowedNames
    )

    $lockedFiles = @()
    $outputFiles = @(Get-OutputFiles -Directories $Directories)
    foreach ($outputFile in $outputFiles) {
        [string]$filePath = $outputFile.FullName
        if (Test-FileCanBeOverwritten -FilePath $filePath) {
            continue
        }

        $lockingProcesses = @(Get-LockingProcesses -FilePath $filePath)
        $allowedLockingProcesses = @()
        foreach ($lockingProcess in $lockingProcesses) {
            [int]$processId = [int]$lockingProcess.ProcessId
            if (Test-IsAllowedLocker -AllowedNames $AllowedNames -ProcessId $processId -ReportedName $lockingProcess.AppName) {
                $allowedLockingProcesses += $lockingProcess
            }
        }

        if ($allowedLockingProcesses.Count -eq 0) {
            continue
        }

        $lockedFiles += [pscustomobject]@{
            FilePath = $filePath
            LockingProcesses = $allowedLockingProcesses
        }
    }

    return $lockedFiles
}

function Stop-LockingProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$LockedFiles
    )

    $stoppedProcessIds = New-Object 'System.Collections.Generic.HashSet[int]'
    foreach ($lockedFile in $LockedFiles) {
        foreach ($lockingProcess in @($lockedFile.LockingProcesses)) {
            [int]$processId = [int]$lockingProcess.ProcessId
            if ($processId -le 0 -or $processId -eq $PID) {
                continue
            }

            if ($stoppedProcessIds.Contains($processId)) {
                continue
            }

            [string]$processName = ''
            try {
                $process = Get-Process -Id $processId -ErrorAction Stop
                $processName = Normalize-ProcessName -ProcessName $process.ProcessName
            } catch {
                $processName = Normalize-ProcessName -ProcessName $lockingProcess.AppName
            }

            if ([string]::IsNullOrWhiteSpace($processName)) {
                $processName = 'unknown'
            }

            Write-Host "Killing $processName ($processId) locking '$($lockedFile.FilePath)'."
            try {
                Stop-Process -Id $processId -Force -ErrorAction Stop
                [void]$stoppedProcessIds.Add($processId)
            } catch {
                Write-Host "Could not kill process ${processId}: $($_.Exception.Message)"
            }
        }
    }

    return $stoppedProcessIds.Count
}

if ($MaxAttempts -lt 1) {
    throw 'MaxAttempts must be at least 1.'
}

if ($RetryDelayMilliseconds -lt 0) {
    throw 'RetryDelayMilliseconds cannot be negative.'
}

$allowedNames = New-AllowedProcessNameSet -Names $AllowedProcessNames
if ($allowedNames.Count -eq 0) {
    throw 'At least one allowed TrayAppDotNET process name is required.'
}

[string[]]$outputDirectoryPaths = Get-OutputDirectoryPaths -DirectoryList $OutputDirectories
if ($outputDirectoryPaths.Count -eq 0) {
    exit 0
}

$remainingLockedFiles = @()
for ([int]$attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    $remainingLockedFiles = @(Get-TrayAppDotNETLockedFiles -Directories $outputDirectoryPaths -AllowedNames $allowedNames)
    if ($remainingLockedFiles.Count -eq 0) {
        if ($attempt -gt 1) {
            Write-Host "TrayAppDotNET output files unlocked after $attempt attempt(s)."
        }

        exit 0
    }

    Write-Host "Found $($remainingLockedFiles.Count) TrayAppDotNET-locked output file(s) on attempt $attempt of $MaxAttempts."
    [int]$stoppedCount = Stop-LockingProcesses -LockedFiles $remainingLockedFiles
    if ($attempt -lt $MaxAttempts) {
        Start-Sleep -Milliseconds $RetryDelayMilliseconds
    }

    if ($stoppedCount -eq 0 -and $attempt -eq $MaxAttempts) {
        break
    }
}

$remainingLockedFiles = @(Get-TrayAppDotNETLockedFiles -Directories $outputDirectoryPaths -AllowedNames $allowedNames)
if ($remainingLockedFiles.Count -eq 0) {
    exit 0
}

Write-Error "TrayAppDotNET output files are still locked after $MaxAttempts attempt(s)."
foreach ($lockedFile in $remainingLockedFiles) {
    [string[]]$lockerDescriptions = @()
    foreach ($lockingProcess in @($lockedFile.LockingProcesses)) {
        [string]$name = Normalize-ProcessName -ProcessName $lockingProcess.AppName
        try {
            $process = Get-Process -Id ([int]$lockingProcess.ProcessId) -ErrorAction Stop
            [string]$actualName = Normalize-ProcessName -ProcessName $process.ProcessName
            if (-not [string]::IsNullOrWhiteSpace($actualName)) {
                $name = $actualName
            }
        } catch {
            Write-Host "Could not resolve process name for $($lockingProcess.ProcessId): $($_.Exception.Message)"
        }

        if ([string]::IsNullOrWhiteSpace($name)) {
            $name = 'unknown'
        }

        $lockerDescriptions += "$name ($($lockingProcess.ProcessId))"
    }

    Write-Error "Still locked: '$($lockedFile.FilePath)' by $($lockerDescriptions -join ', ')"
}

exit 2
