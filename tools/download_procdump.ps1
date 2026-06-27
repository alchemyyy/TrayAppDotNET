#requires -Version 5.1

<#
.SYNOPSIS
Downloads the reviewed Microsoft Sysinternals ProcDump x64 executable.

.DESCRIPTION
Downloads the official ProcDump archive, verifies the pinned archive and
executable SHA-256 digests, checks the executable version and Microsoft
Authenticode signature, and installs procdump64.exe beside this script.

The download URL is mutable. When Microsoft publishes a new ProcDump release,
review that release and update all pinned metadata together.
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

[string]$procDumpVersion = '12.01'
[string]$archiveName = 'Procdump.zip'
[string]$downloadUrl = 'https://download.sysinternals.com/files/Procdump.zip'
[string]$expectedArchiveSHA256 = '68E057587B0FD654EFA095F76D80D633C0E5C60EA26FD3E7C0011C076BB2D00C'
[string]$expectedExecutableSHA256 = 'D1FC99AE304BD1D2BF28ABEB62531DA959E2431916194981B88C958FD713A8E6'
[Int64]$expectedExecutableLength = 741216
[string]$expectedSignerSubject = 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US'
[string]$destinationPath = Join-Path -Path $PSScriptRoot -ChildPath 'procdump64.exe'

function Get-FileSHA256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    [System.Security.Cryptography.SHA256]$hashAlgorithm = [System.Security.Cryptography.SHA256]::Create()
    [System.IO.FileStream]$fileStream = [System.IO.File]::OpenRead($Path)

    try {
        [byte[]]$hashBytes = $hashAlgorithm.ComputeHash($fileStream)
        return [System.BitConverter]::ToString($hashBytes).Replace('-', '')
    }
    finally {
        $fileStream.Dispose()
        $hashAlgorithm.Dispose()
    }
}

function Assert-ProcDumpExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "ProcDump executable was not found: $Path"
    }

    [System.IO.FileInfo]$file = Get-Item -LiteralPath $Path
    if ($file.Length -ne $expectedExecutableLength) {
        throw "ProcDump executable length mismatch. Expected $expectedExecutableLength bytes, found $($file.Length)."
    }

    [string]$actualExecutableSHA256 = Get-FileSHA256 -Path $Path
    if ($actualExecutableSHA256 -ne $expectedExecutableSHA256) {
        throw "ProcDump executable SHA-256 mismatch. Expected $expectedExecutableSHA256, found $actualExecutableSHA256."
    }

    [System.Diagnostics.FileVersionInfo]$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if ($versionInfo.FileVersion -ne $procDumpVersion) {
        throw "ProcDump executable version mismatch. Expected $procDumpVersion, found $($versionInfo.FileVersion)."
    }

    [System.Management.Automation.Signature]$signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "ProcDump Authenticode signature is not valid: $($signature.StatusMessage)"
    }

    if ($null -eq $signature.SignerCertificate) {
        throw 'ProcDump Authenticode signature does not contain a signer certificate.'
    }

    if ($signature.SignerCertificate.Subject -ne $expectedSignerSubject) {
        throw "Unexpected ProcDump signer: $($signature.SignerCertificate.Subject)"
    }
}

if (Test-Path -LiteralPath $destinationPath -PathType Leaf) {
    try {
        Assert-ProcDumpExecutable -Path $destinationPath
        Write-Host "ProcDump $procDumpVersion is already installed at $destinationPath"
        return
    }
    catch {
        Write-Warning "Replacing the existing ProcDump executable because validation failed: $($_.Exception.Message)"
    }
}

[string]$directorySeparator = [System.IO.Path]::DirectorySeparatorChar
[string]$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
if (-not $temporaryRoot.EndsWith($directorySeparator, [System.StringComparison]::Ordinal)) {
    $temporaryRoot += $directorySeparator
}

[string]$temporaryDirectoryName = 'TrayAppDotNET-ProcDump-' + [System.Guid]::NewGuid().ToString('N')
[string]$temporaryDirectory = [System.IO.Path]::GetFullPath((Join-Path -Path $temporaryRoot -ChildPath $temporaryDirectoryName))
if (-not $temporaryDirectory.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a temporary directory outside the system temporary root: $temporaryDirectory"
}

try {
    [void](New-Item -ItemType Directory -Path $temporaryDirectory)

    [string]$archivePath = Join-Path -Path $temporaryDirectory -ChildPath $archiveName
    Write-Host "Downloading ProcDump $procDumpVersion from Microsoft..."
    Invoke-WebRequest -UseBasicParsing -Uri $downloadUrl -OutFile $archivePath

    [string]$actualArchiveSHA256 = Get-FileSHA256 -Path $archivePath
    if ($actualArchiveSHA256 -ne $expectedArchiveSHA256) {
        throw "ProcDump archive SHA-256 mismatch. Expected $expectedArchiveSHA256, found $actualArchiveSHA256. Microsoft may have replaced the mutable download; review it before updating this script."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [string]$extractDirectory = Join-Path -Path $temporaryDirectory -ChildPath 'extracted'
    [System.IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $extractDirectory)

    [string]$extractedExecutablePath = Join-Path -Path $extractDirectory -ChildPath 'procdump64.exe'
    Assert-ProcDumpExecutable -Path $extractedExecutablePath

    [System.IO.File]::Copy($extractedExecutablePath, $destinationPath, $true)
    Assert-ProcDumpExecutable -Path $destinationPath
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        [string]$resolvedTemporaryDirectory = [System.IO.Path]::GetFullPath($temporaryDirectory)
        if (-not $resolvedTemporaryDirectory.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a temporary directory outside the system temporary root: $resolvedTemporaryDirectory"
        }

        Remove-Item -LiteralPath $resolvedTemporaryDirectory -Recurse -Force
    }
}

Write-Host "Installed ProcDump $procDumpVersion at $destinationPath"
